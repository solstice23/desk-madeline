using System;
using System.Collections.Generic;
using DeskMadeline;

// Moon blocks: windows as FloatySpaceBlocks.
//
// The numbers are FloatySpaceBlock.MoveToTarget's -- four pixels of drift on a sine that comes
// round every 2*pi seconds, twelve of sink eased in and out, eight along a dash and back --
// and what is checked here is that they come out of the state machine, since the part that
// moves real windows cannot be driven without a desktop to move them on.
//
// So the desktop is played here instead: a window that goes where it is told, and is read back
// afterwards the way the real one is. Feeding a rectangle that never moved would let the state
// machine believe anything at all about where it had put things, which is exactly the mistake
// that sent every real window walking off the side of the screen.
static class MoonChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    /// <summary>A window that obeys: it sits wherever MoonWindows last put it.</summary>
    sealed class Desktop
    {
        readonly MoonWindows moon;
        readonly IntPtr handle;
        readonly int scale;
        Win32.RECT rect;
        Win32.RECT stale;    // what a reader would have got a frame ago

        public Desktop(MoonWindows moon, IntPtr handle, int left, int top, int scale = 1)
        {
            this.moon = moon;
            this.handle = handle;
            this.scale = scale;
            rect = new Win32.RECT { Left = left, Top = top, Right = left + 300, Bottom = top + 200 };
            stale = rect;
        }

        public Win32.RECT Rect => rect;

        /// <summary>One frame: hand over where the window is, then move it where it was told.</summary>
        /// <param name="lag">
        /// Report the position from a frame ago, which is what happens for real -- a move is
        /// posted to the window's own thread and takes a moment to land.
        /// </param>
        public void Frame(HashSet<IntPtr> ridden, bool lag = false)
        {
            var seen = lag ? stale : rect;
            moon.Update(Dt, scale, new List<PolledWindowInfo>
                { new PolledWindowInfo(handle, seen, true) }, ridden);
            stale = rect;
            Win32.RECT home = moon.HomeOf(handle);
            var applied = moon.OffsetOfApplied(handle);
            int dx = home.Left + applied.X - rect.Left, dy = home.Top + applied.Y - rect.Top;
            rect = new Win32.RECT
            {
                Left = rect.Left + dx, Top = rect.Top + dy,
                Right = rect.Right + dx, Bottom = rect.Bottom + dy,
            };
        }

        /// <summary>Drag it, as its owner would.</summary>
        public void DragBy(int dx, int dy) => rect = new Win32.RECT
        {
            Left = rect.Left + dx, Top = rect.Top + dy,
            Right = rect.Right + dx, Bottom = rect.Bottom + dy,
        };
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("MOON BLOCKS: windows that will not hold still");
        Console.WriteLine(new string('=', 74));

        var handle = new IntPtr(1);
        var nobody = new HashSet<IntPtr>();
        var somebody = new HashSet<IntPtr> { handle };

        // One window, never ridden: it should drift and come back, and never wander off.
        var moon = new MoonWindows();
        var desk = new Desktop(moon, handle, 100, 100);
        float lowest = 0f, highest = 0f;
        int leftmost = 100, rightmost = 100;
        for (int i = 0; i < 60 * 7; i++)     // a little over one full turn of the sine
        {
            desk.Frame(nobody);
            float y = moon.OffsetOf(handle).Y;
            lowest = Math.Min(lowest, y);
            highest = Math.Max(highest, y);
            leftmost = Math.Min(leftmost, desk.Rect.Left);
            rightmost = Math.Max(rightmost, desk.Rect.Left);
        }
        Console.WriteLine($"      unridden, it drifted between {lowest:F0} and {highest:F0}");
        Check("it drifts four pixels each way and no further",
            lowest <= -3.5f && lowest >= -4.5f && highest >= 3.5f && highest <= 4.5f);

        // The one that matters on a real desktop: nothing pushes it sideways, and the place it
        // would be put back to is still the place its owner left it.
        Console.WriteLine($"      after seven seconds its left edge ranged {leftmost}..{rightmost}, "
            + $"home {moon.HomeOf(handle).Left},{moon.HomeOf(handle).Top}");
        Check("it does not wander sideways", leftmost == 100 && rightmost == 100);
        Check("and its home is still where its owner left it",
            moon.HomeOf(handle).Left == 100 && moon.HomeOf(handle).Top == 100);

        // Ridden: it sinks twelve, and comes back up when she steps off.
        for (int i = 0; i < 60 * 2; i++) desk.Frame(somebody);
        float sunk = moon.OffsetOf(handle).Y;
        Console.WriteLine($"      stood on for two seconds: {sunk:F0} (twelve of sink, plus drift)");
        Check("standing on it sinks it about twelve", sunk >= 7.5f && sunk <= 16.5f);

        for (int i = 0; i < 60 * 2; i++) desk.Frame(nobody);
        float risen = moon.OffsetOf(handle).Y;
        Console.WriteLine($"      two seconds after stepping off: {risen:F0}");
        Check("and it comes back up", risen <= 4.5f);

        // Dashed into: eight pixels out and back, and settled within a second.
        moon.Dashed(handle, new System.Drawing.PointF(1f, 0f));
        float furthest = 0f;
        for (int i = 0; i < 60; i++)
        {
            desk.Frame(nobody);
            furthest = Math.Max(furthest, moon.OffsetOf(handle).X);
        }
        float after = moon.OffsetOf(handle).X;
        Console.WriteLine($"      dashed into: out to {furthest:F0}, back to {after:F0}");
        Check("a dash shoves it eight sideways", furthest >= 7f && furthest <= 8.5f);
        Check("and it returns", Math.Abs(after) < 0.5f);
        Check("a dash leaves the home alone", moon.HomeOf(handle).Left == 100);

        // Its owner drags it: the home goes with it, by exactly the drag and no more. The
        // window is somewhere in its bob when the drag lands, and rehoming under the offset it
        // is already holding is what keeps that from being counted twice.
        desk.DragBy(600, 400);
        desk.Frame(nobody);
        Win32.RECT rehomed = moon.HomeOf(handle);
        Console.WriteLine($"      dragged 600,400: home {rehomed.Left},{rehomed.Top}");
        Check("a window dragged by its owner takes its new place as home",
            rehomed.Left == 700 && rehomed.Top == 500);

        int lowTop = int.MaxValue, highTop = int.MinValue;
        for (int i = 0; i < 60 * 7; i++)
        {
            desk.Frame(nobody);
            lowTop = Math.Min(lowTop, desk.Rect.Top);
            highTop = Math.Max(highTop, desk.Rect.Top);
        }
        Console.WriteLine($"      and then drifted {lowTop}..{highTop} about its new home");
        Check("and drifts about the new one", lowTop >= 495 && highTop <= 505);

        // Drawn six times over, it must still only ever stand on whole game pixels: half a
        // pixel of platform is half a pixel the ride cannot hand her, and a border that has
        // slid a third of a pixel out from under her feet is one she is no longer standing on.
        var zoomed = new MoonWindows();
        var big = new Desktop(zoomed, handle, 600, 600, scale: 6);
        bool onGrid = true;
        var steps = new HashSet<int>();
        int wasTop = big.Rect.Top;
        for (int i = 0; i < 60 * 9; i++)
        {
            big.Frame(i % 240 < 120 ? somebody : nobody);   // stood on, then stepped off
            var applied = zoomed.OffsetOfApplied(handle);
            if (applied.X % 6 != 0 || applied.Y % 6 != 0) onGrid = false;
            // The first frame is the window being adopted: its sine starts at a random point,
            // as every FloatySpaceBlock's does, so it takes its place on the bob in one hop.
            if (i > 0 && big.Rect.Top != wasTop) steps.Add(big.Rect.Top - wasTop);
            wasTop = big.Rect.Top;
        }
        Console.WriteLine($"      at six times over, it moved in steps of "
            + $"{string.Join(", ", steps)} screen pixels");
        Check("it only ever stands on whole game pixels", onGrid);
        Check("so every step it takes after the first is one game pixel",
            steps.Count == 2 && steps.Contains(6) && steps.Contains(-6));

        // And when the desktop answers a frame late, as a window whose move is posted to its
        // own thread does, the drift must be neither damped nor sent walking. Believing a late
        // answer meant somebody else had moved it cancelled the two against each other exactly,
        // and the window sat perfectly still.
        var slow = new MoonWindows();
        var lagging = new Desktop(slow, handle, 100, 100);
        int lowLate = int.MaxValue, highLate = int.MinValue;
        for (int i = 0; i < 60 * 7; i++)
        {
            lagging.Frame(nobody, lag: true);
            lowLate = Math.Min(lowLate, lagging.Rect.Top);
            highLate = Math.Max(highLate, lagging.Rect.Top);
        }
        Console.WriteLine($"      answering a frame late, it drifted {lowLate}..{highLate}, "
            + $"home {slow.HomeOf(handle).Left},{slow.HomeOf(handle).Top}");
        Check("a late answer neither damps the drift", highLate - lowLate >= 7);
        Check("nor sends it walking", lowLate >= 95 && highLate <= 105 &&
            slow.HomeOf(handle).Left == 100 && slow.HomeOf(handle).Top == 100);

        failed += StandingOnOne();
        failed += HoldingOnToOne();
        return failed;
    }

    /// <summary>
    /// Grabbing the side of one. Player.IsRiding counts the wall she is holding, so the block
    /// sinks under a grab exactly as it does under her feet -- and carries her down with it.
    /// </summary>
    static int HoldingOnToOne()
    {
        const int Scale = 6;
        var handle = new IntPtr(9);
        var moon = new MoonWindows();

        Console.WriteLine();
        Console.WriteLine("  Holding on to one");

        // A wall to her right and nothing under her: her hand is the only thing holding her
        // up, so what carries her can only be the wall.
        var rect = new Win32.RECT { Left = 24, Top = -3000, Right = 624, Bottom = 3000 };
        float ToGame(int physical) => (float)Math.Floor(physical / (double)Scale + 0.5);
        Solid Wall() => new Solid
        {
            Id = handle,
            L = ToGame(rect.Left), T = ToGame(rect.Top),
            R = ToGame(rect.Right), B = ToGame(rect.Bottom),
        };

        var player = new Player
        {
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new System.Drawing.PointF(0f, 0f),
            Solids = new List<Solid> { Wall() },
        };
        for (int i = 0; i < 5; i++) player.Update(Dt, new PetInput());

        int climbing = 0, held = 0;
        float wasWallTop = Wall().T, carried = 0f;
        for (int frame = 0; frame < 60 * 3; frame++)
        {
            // As PetWindow does it: every solid asks whether she is riding it.
            var ridden = new HashSet<IntPtr>();
            foreach (Solid piece in player.Solids) if (player.IsRiding(piece)) ridden.Add(piece.Id);
            if (player.State == Player.StClimb) climbing++;
            if (player.RidingId == handle) held++;

            moon.Update(Dt, Scale, new List<PolledWindowInfo>
                { new PolledWindowInfo(handle, rect, true) }, ridden);

            Win32.RECT home = moon.HomeOf(handle);
            var applied = moon.OffsetOfApplied(handle);
            int dx = home.Left + applied.X - rect.Left, dy = home.Top + applied.Y - rect.Top;
            rect = new Win32.RECT
            {
                Left = rect.Left + dx, Top = rect.Top + dy,
                Right = rect.Right + dx, Bottom = rect.Bottom + dy,
            };

            Solid wall = Wall();
            player.Solids = new List<Solid> { wall };
            float rose = wall.T - wasWallTop;
            wasWallTop = wall.T;
            if (player.RidingId == handle) { player.RideAlong(0f, rose); carried += rose; }
            else player.EndRide();
            player.SweptInto(wall, 0f, rose);
            // Pressed into the wall with the grab held: vanilla's climb, hands and all.
            player.Update(Dt, new PetInput { MoveX = 1, AimX = 1, GrabHeld = true });
        }

        float sunk = moon.OffsetOf(handle).Y;
        Console.WriteLine($"      three seconds hanging off it: climbing on {climbing} of 180"
            + $" frames, riding it on {held}");
        Console.WriteLine($"      it sank {sunk:F0} and took her {carried:F0} down with it");
        Check("holding on to one counts as riding it", held > 150);
        Check("so it sinks under a grab as it does under her feet", sunk >= 7.5f);
        Check("and she is carried down with it", carried >= 7.5f);
        return 0;
    }

    /// <summary>
    /// Her, on one, for as long as it takes the block to sink under her and come back up --
    /// and then dashed into. The report was that she falls through them.
    /// </summary>
    /// <remarks>
    /// This is the desktop loop rather than the game's: the block is a window, the window is
    /// turned into a platform in game pixels, she is carried by the ride-along and settled by
    /// the push, and only then does she update -- the order PetWindow does them in.
    /// </remarks>
    static int StandingOnOne()
    {
        const int Scale = 6;
        var handle = new IntPtr(7);
        var moon = new MoonWindows();

        Console.WriteLine();
        Console.WriteLine("  Standing on one");

        // A window whose top edge is at game y=0, wide enough to stand well inside.
        var rect = new Win32.RECT { Left = -1200, Top = 0, Right = 1200, Bottom = 1200 };
        float ToGame(int physical) => (float)Math.Floor(physical / (double)Scale + 0.5);

        var player = new Player
        {
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Pos = new System.Drawing.PointF(0f, 0f),
        };
        float top = ToGame(rect.Top);
        var platform = new Solid
        { Id = handle, L = ToGame(rect.Left), T = top, R = ToGame(rect.Right), B = top + 8f };
        player.Solids = new List<Solid> { platform };
        for (int i = 0; i < 5; i++) player.Update(Dt, new PetInput());

        int airborne = 0, lowest = 0, attacking = 0, dashAt = -1;
        var lastDashDir = System.Drawing.PointF.Empty;
        bool fellThrough = false, dashed = false;
        for (int frame = 0; frame < 60 * 6 && !fellThrough; frame++)
        {
            var ridden = new HashSet<IntPtr>();
            if (player.GroundId == handle) ridden.Add(handle);

            // Up, and then straight back down into it, which is what shoves a FloatySpaceBlock
            // along: eight pixels out from under her and back. A dash down while she is
            // standing on it is turned into a level one, the way vanilla's is, so it has to be
            // thrown from the air to arrive as a dash into the block at all.
            var input = new PetInput();
            if (frame == 120) player.BufferJump();
            if (frame >= 120 && frame < 132) input.JumpHeld = true;
            // Thrown on the way back down, close enough that the dash is still going when she
            // arrives -- which is the only way it ever meets the block.
            if (dashAt < 0 && frame > 130 && !player.onGround && player.Speed.Y > 0f &&
                top - player.Pos.Y < 10f)
            { player.BufferDash(); dashAt = frame; }
            if (dashAt >= 0 && frame - dashAt < 14) { input.MoveY = 1; input.AimY = 1; }
            // SampleInput re-derives these from the buffers every frame.
            input.JumpPressed = player.HasJumpBuffer;
            input.DashPressed = player.HasDashBuffer;

            if (player.IsDashAttacking) attacking++;
            // What PetWindow does with them, and in its order: whatever she hit on the frame
            // just gone gets shoved, and then every block takes its step.
            foreach (DashCollision hit in player.DashCollisions)
            {
                moon.Dashed(hit.Id, hit.Direction);
                lastDashDir = hit.Direction;
                dashed = true;
            }
            moon.Update(Dt, Scale, new List<PolledWindowInfo>
                { new PolledWindowInfo(handle, rect, true) }, ridden);

            // The window goes where it was told, and the desktop is read back from it.
            Win32.RECT home = moon.HomeOf(handle);
            var applied = moon.OffsetOfApplied(handle);
            int dx = home.Left + applied.X - rect.Left, dy = home.Top + applied.Y - rect.Top;
            rect = new Win32.RECT
            {
                Left = rect.Left + dx, Top = rect.Top + dy,
                Right = rect.Right + dx, Bottom = rect.Bottom + dy,
            };

            float wasTop = platform.T;
            top = ToGame(rect.Top);
            platform = new Solid
            { Id = handle, L = ToGame(rect.Left), T = top, R = ToGame(rect.Right), B = top + 8f };
            player.Solids = new List<Solid> { platform };

            if (player.GroundId == handle) player.RideAlong(0f, top - wasTop);
            else player.EndRide();
            player.SweptInto(platform, 0f, top - wasTop);
            player.Update(Dt, input);

            // Only while she is meant to be standing on it: the jump and the dash that
            // follows are airborne on purpose.
            if ((frame < 115 || frame > 220) && !player.onGround) airborne++;
            lowest = Math.Max(lowest, (int)(player.Pos.Y - top));
            if (player.Pos.Y > platform.B + 2f) fellThrough = true;
        }

        Console.WriteLine($"      six seconds on a bobbing, sinking, dashed-into window:"
            + $" she ended {player.Pos.Y - top:F0} from its top, at worst {lowest} below it");
        Console.WriteLine($"      off the ground on {airborne} frames she should have been"
            + $" standing, dash landed={dashed}"
            + $" (attacking on {attacking} frames, shoved {lastDashDir.X},{lastDashDir.Y})");
        Check("she does not fall through a moon block", !fellThrough);
        Check("she stays standing on its top edge", Math.Abs(player.Pos.Y - top) < 2f);
        Check("and is carried rather than left in the air", airborne < 5);
        Check("the dash reached the block", dashed);
        return 0;
    }
}
