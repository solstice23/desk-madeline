using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// Windows as kevin blocks: dash a border and the window charges, crushes, and crawls home.
//
// As with the moon checks, the state machine is driven against a pretend desktop -- a window
// that sits wherever KevinWindows last put it, shake and all -- since the part that moves real
// windows cannot be driven without a desktop to move them on. The rebound she gets off an
// activated face goes through the real Player, because that half is vanilla's Player.Rebound.
static class KevinChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    /// <summary>A desktop of obedient windows: each sits wherever the kevin machine put it.</summary>
    sealed class Desktop
    {
        readonly KevinWindows kevin;
        readonly Dictionary<IntPtr, Win32.RECT> rects = new Dictionary<IntPtr, Win32.RECT>();
        public Win32.RECT Bounds = new Win32.RECT
        { Left = -100000, Top = -100000, Right = 100000, Bottom = 100000 };
        /// <summary>
        /// Windows hidden behind something bigger: still windows, but contributing nothing to
        /// the world's solids, exactly as RebuildSolids leaves a fully occluded one.
        /// </summary>
        public readonly HashSet<IntPtr> Shadowed = new HashSet<IntPtr>();

        public Desktop(KevinWindows kevin) { this.kevin = kevin; }

        public void Add(IntPtr handle, int left, int top, int width, int height)
            => rects[handle] = new Win32.RECT
            { Left = left, Top = top, Right = left + width, Bottom = top + height };

        public Win32.RECT Rect(IntPtr handle) => rects[handle];

        /// <summary>Drag one, as its owner would.</summary>
        public void DragTo(IntPtr handle, int left, int top)
        {
            Win32.RECT r = rects[handle];
            rects[handle] = new Win32.RECT
            { Left = left, Top = top, Right = left + (r.Right - r.Left), Bottom = top + (r.Bottom - r.Top) };
        }

        public void Frame()
        {
            var info = new List<PolledWindowInfo>();
            var solids = new List<Solid>();
            foreach (var pair in rects)
            {
                info.Add(new PolledWindowInfo(pair.Key, pair.Value, true));
                if (Shadowed.Contains(pair.Key)) continue;
                solids.Add(new Solid
                {
                    Id = pair.Key, L = pair.Value.Left, T = pair.Value.Top,
                    R = pair.Value.Right, B = pair.Value.Bottom
                });
            }
            kevin.SetScale(1);
            kevin.Update(Dt, 1, info, solids, Bounds);
            foreach (var pair in new List<KeyValuePair<IntPtr, Win32.RECT>>(rects))
            {
                Win32.RECT home = kevin.HomeOf(pair.Key);
                if (home.Right == home.Left) continue;      // not adopted yet
                Point offset = kevin.OffsetOf(pair.Key);
                Point shake = kevin.ShakeOf(pair.Key);
                int left = home.Left + offset.X + shake.X;
                int top = home.Top + offset.Y + shake.Y;
                Win32.RECT r = pair.Value;
                rects[pair.Key] = new Win32.RECT
                { Left = left, Top = top, Right = left + (r.Right - r.Left), Bottom = top + (r.Bottom - r.Top) };
            }
        }
    }

    static string Drain(KevinWindows kevin)
    {
        var heard = new List<string>();
        while (kevin.SoundEvents.Count > 0)
            heard.Add(kevin.SoundEvents.Dequeue().Path.Replace("event:/game/06_reflection/", ""));
        while (kevin.LoopEvents.Count > 0)
        {
            KevinLoopEvent loop = kevin.LoopEvents.Dequeue();
            heard.Add(loop.Command.ToString().ToLowerInvariant() + ":" +
                loop.Path.Replace("event:/game/06_reflection/", ""));
        }
        return string.Join(", ", heard);
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("KEVIN BLOCKS: dash a window and it dashes back");
        Console.WriteLine(new string('=', 74));

        var handle = new IntPtr(1);
        var wall = new IntPtr(2);

        Console.WriteLine("  The charge, the crush, and the crawl home");
        var kevin = new KevinWindows();
        var desk = new Desktop(kevin);
        desk.Add(handle, 1000, 1000, 300, 200);
        // A wall window whose right edge is 300 short of the kevin's left.
        desk.Add(wall, 400, 1000, 300, 200);
        desk.Frame();

        // She dashed right into its left face: the collision carries her movement, and the
        // block charges back the way she came -- left, at the wall.
        DashCollisionResults answer = kevin.Dashed(handle, new PointF(1f, 0f));
        Check("a dash into a face answers Rebound", answer == DashCollisionResults.Rebound);
        string startSounds = Drain(kevin);
        Check("it sounds the activation and starts its move loop",
            startSounds.Contains("crushblock_activate") && startSounds.Contains("start:crushblock_move_loop"));

        // Four tenths of windup: shaking, but going nowhere.
        int maxWindupDrift = 0;
        for (int i = 0; i < 22; i++)
        {
            desk.Frame();
            maxWindupDrift = Math.Max(maxWindupDrift, Math.Abs(kevin.OffsetOf(handle).X));
        }
        Console.WriteLine($"      during the windup it strayed {maxWindupDrift}, "
            + $"phase {kevin.PhaseOf(handle)}");
        Check("the windup holds it still", maxWindupDrift <= 1);

        // Then it charges: gone from home, and quickly.
        for (int i = 0; i < 30; i++) desk.Frame();
        int midCharge = kevin.OffsetOf(handle).X;
        Console.WriteLine($"      half a second in it is {midCharge} from home");
        Check("it charges the way she came", midCharge < -20);

        for (int i = 0; i < 120 && kevin.PhaseOf(handle) == KevinWindows.Phase.Attack; i++)
            desk.Frame();
        string impactSounds = Drain(kevin);
        Console.WriteLine($"      stopped at {kevin.OffsetOf(handle).X} "
            + $"(the wall is 300 away), phase {kevin.PhaseOf(handle)}");
        Check("it stops exactly against the wall window", kevin.OffsetOf(handle).X == -300);
        Check("with the impact, the move loop winding down and the return loop starting",
            impactSounds.Contains("crushblock_impact") &&
            impactSounds.Contains("ending:crushblock_move_loop") &&
            impactSounds.Contains("start:crushblock_return_loop"));

        // The crawl home: 300 pixels at sixty a second, plus the pauses.
        for (int i = 0; i < 60 * 8 && kevin.PhaseOf(handle) != KevinWindows.Phase.Idle; i++)
            desk.Frame();
        string restSounds = Drain(kevin);
        Console.WriteLine($"      home again at offset {kevin.OffsetOf(handle).X},"
            + $"{kevin.OffsetOf(handle).Y}, phase {kevin.PhaseOf(handle)}");
        Check("it crawls all the way home", kevin.OffsetOf(handle).X == 0 &&
            kevin.PhaseOf(handle) == KevinWindows.Phase.Idle);
        Check("and rests, stopping the return loop",
            restSounds.Contains("crushblock_rest") && restSounds.Contains("stop:crushblock_return_loop"));
        Check("its home never moved", kevin.HomeOf(handle).Left == 1000);

        Console.WriteLine();
        Console.WriteLine("  Re-arming, and the turns it will and will not take");
        var kevin2 = new KevinWindows();
        var desk2 = new Desktop(kevin2);
        var lone = new IntPtr(3);
        desk2.Add(lone, 1000, 1000, 300, 200);
        desk2.Frame();
        kevin2.Dashed(lone, new PointF(-1f, 0f));
        Drain(kevin2);
        desk2.Frame();
        Check("during the windup it cannot be re-activated",
            kevin2.Dashed(lone, new PointF(0f, -1f)) == DashCollisionResults.NormalCollision);
        for (int i = 0; i < 26; i++) desk2.Frame();
        Check("dashing along the way it is already going does nothing",
            kevin2.Dashed(lone, new PointF(-1f, 0f)) == DashCollisionResults.NormalCollision);
        Check("but a dash from another side turns it mid-flight",
            kevin2.Dashed(lone, new PointF(0f, -1f)) == DashCollisionResults.Rebound);
        Check("and the new leg joins the way home", kevin2.ReturnLegsOf(lone) == 2);
        Check("it winds up again for the new direction",
            kevin2.PhaseOf(lone) == KevinWindows.Phase.Windup);

        Console.WriteLine();
        Console.WriteLine("  What an owner's drag overrules");
        var kevin3 = new KevinWindows();
        var desk3 = new Desktop(kevin3);
        var grabbed = new IntPtr(4);
        desk3.Add(grabbed, 1000, 1000, 300, 200);
        desk3.Frame();
        kevin3.Dashed(grabbed, new PointF(1f, 0f));
        for (int i = 0; i < 60; i++) desk3.Frame();
        Check("(it is away from home)", kevin3.OffsetOf(grabbed).X < -10);
        desk3.DragTo(grabbed, 2000, 500);
        desk3.Frame();
        Drain(kevin3);
        Check("a drag by its owner ends everything and rehomes it",
            kevin3.PhaseOf(grabbed) == KevinWindows.Phase.Idle &&
            kevin3.HomeOf(grabbed).Left == 2000 && kevin3.OffsetOf(grabbed).X == 0);

        Console.WriteLine();
        Console.WriteLine("  The desktop's edge is the level's edge");
        var kevin4 = new KevinWindows();
        var desk4 = new Desktop(kevin4);
        var cornered = new IntPtr(5);
        desk4.Bounds = new Win32.RECT { Left = 900, Top = 0, Right = 5000, Bottom = 5000 };
        desk4.Add(cornered, 1000, 1000, 300, 200);
        desk4.Frame();
        kevin4.Dashed(cornered, new PointF(1f, 0f));
        for (int i = 0; i < 200 && kevin4.PhaseOf(cornered) == KevinWindows.Phase.Attack ||
             kevin4.PhaseOf(cornered) == KevinWindows.Phase.Windup; i++) desk4.Frame();
        Console.WriteLine($"      it stopped {kevin4.OffsetOf(cornered).X} from home,"
            + $" a hundred short of the edge");
        Check("it smashes into the desktop's edge and no further",
            kevin4.OffsetOf(cornered).X == -100);

        Console.WriteLine();
        Console.WriteLine("  A window nobody can see stops nothing");
        // Three windows: C on top, B maximized beneath it, A at the bottom, entirely under B.
        // With maximized windows ignored, B is an occluder and A contributes no solids at all
        // -- she cannot stand on it, so it must not stop a charge either. The kevin machine
        // collides with the world's solids, so shadowed A simply is not there.
        var kevin5 = new KevinWindows();
        var desk5 = new Desktop(kevin5);
        var c = new IntPtr(6);
        var a = new IntPtr(7);
        desk5.Bounds = new Win32.RECT { Left = 100, Top = 0, Right = 5000, Bottom = 5000 };
        desk5.Add(c, 1000, 1000, 300, 200);
        desk5.Add(a, 500, 1000, 300, 200);      // squarely in the charge's path
        desk5.Shadowed.Add(a);                  // and squarely behind the maximized B
        desk5.Frame();
        kevin5.Dashed(c, new PointF(1f, 0f));
        for (int i = 0; i < 400 && kevin5.PhaseOf(c) != KevinWindows.Phase.ImpactPause; i++)
            desk5.Frame();
        Console.WriteLine($"      it charged to {kevin5.OffsetOf(c).X}: past the shadowed"
            + $" window at -200, to the desktop's edge at -900");
        Check("a window shadowed by a maximized one does not stop the charge",
            kevin5.OffsetOf(c).X == -900);

        // The same window with one visible border piece is a wall again.
        var kevin6 = new KevinWindows();
        var desk6 = new Desktop(kevin6);
        desk6.Add(c, 1000, 1000, 300, 200);
        desk6.Add(a, 500, 1000, 300, 200);
        desk6.Frame();
        kevin6.Dashed(c, new PointF(1f, 0f));
        for (int i = 0; i < 400 && kevin6.PhaseOf(c) != KevinWindows.Phase.ImpactPause; i++)
            desk6.Frame();
        Check("and the same window visible is a wall again", kevin6.OffsetOf(c).X == -200);

        Console.WriteLine();
        Console.WriteLine("  The rebound she gets off it");
        var player = new Player
        {
            Solids = new List<Solid>
            {
                new Solid { Id = new IntPtr(1), L = -500f, T = 0f, R = 500f, B = 40f },
                new Solid { Id = new IntPtr(9), L = 20f, T = -80f, R = 90f, B = 0f },
            },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(0f, 0f)
        };
        for (int i = 0; i < 5; i++) player.Update(Dt, new PetInput());
        var answers = new List<PointF>();
        player.OnDashCollide = (id, direction) =>
        {
            if (id != new IntPtr(9)) return DashCollisionResults.NormalCollision;
            answers.Add(direction);
            return DashCollisionResults.Rebound;
        };
        player.BufferDash();
        var input = new PetInput { MoveX = 1, AimX = 1 };
        for (int i = 0; i < 30 && answers.Count == 0; i++)
        {
            input.DashPressed = player.HasDashBuffer;
            player.Update(Dt, input);
        }
        Console.WriteLine($"      after the dash met the block: speed "
            + $"{player.Speed.X:F0},{player.Speed.Y:F0}, state {player.State}");
        Check("the collision was offered with its own direction",
            answers.Count == 1 && answers[0].X == 1f);
        Check("she is thrown back the way she came at 120",
            Math.Abs(player.Speed.X + 120f) < .001f);
        Check("and up at 120", Math.Abs(player.Speed.Y + 120f) < .001f);
        Check("back in the normal state, dash over", player.State == Player.StNormal);

        return failed;
    }
}
