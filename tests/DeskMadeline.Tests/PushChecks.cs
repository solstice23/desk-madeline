using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// What happens when a window moves into her, as opposed to under her.
//
// Two different things get called "pushing". One is the ride-along: stand on a window and
// drag it, and she travels with it, which PetWindow does by moving her with the rect. The
// other is a solid arriving where she already is -- which is what a crush would grow out of,
// and which is what this measures.
static class PushChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static Solid Rect(int id, float l, float t, float r, float b) =>
        new Solid { Id = new IntPtr(id), L = l, T = t, R = r, B = b };

    static Player OnFloor(float x)
    {
        var player = new Player
        {
            Solids = new List<Solid> { Rect(1, -500f, 0f, 500f, 40f) },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Pos = new PointF(x, 0f)
        };
        for (int i = 0; i < 5; i++) player.Update(Dt, new PetInput());
        return player;
    }

    /// <summary>Sweep a solid into her a pixel at a time, pushing as Solid.MoveHExact does.</summary>
    static void Sweep(Player player, List<Solid> world, int id, Func<int, Solid> at, int steps)
    {
        for (int step = 1; step <= steps; step++)
        {
            Solid moved = at(step);
            var solids = new List<Solid>(world) { moved };
            player.Solids = solids;
            player.SweptInto(moved, -1f, 0f);   // the wall steps one pixel left each time
            player.Update(Dt, new PetInput());
        }
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("PUSHING: a window edge arriving where she stands");
        Console.WriteLine(new string('=', 74));

        // A wall to her right, walked one pixel at a time into the space she occupies.
        var floor = new List<Solid> { Rect(1, -500f, 0f, 500f, 40f) };
        var player = OnFloor(0f);
        float startX = player.Pos.X;
        Sweep(player, floor, 2, step => Rect(2, 20f - step, -60f, 60f - step, 0f), 20);
        Console.WriteLine($"      wall swept from x=20 to x=0; she is at {player.Pos.X:F0}" +
            $" (started {startX:F0})");
        Check("a solid sweeping into her pushes her along", player.Pos.X < startX - 3f);
        Check($"and she ends up clear of its left edge at x=0 ({player.Pos.X + 4f:F0})",
            player.Pos.X + 4f <= 0.001f);

        Console.WriteLine();
        Console.WriteLine("  Crushed against something behind her");
        var trapped = OnFloor(0f);
        trapped.Solids = new List<Solid>
        {
            Rect(1, -500f, 0f, 500f, 40f),
            Rect(3, -40f, -60f, -6f, 0f),          // wall close behind her
        };
        trapped.Update(Dt, new PetInput());
        Sweep(trapped, new List<Solid>
        {
            Rect(1, -500f, 0f, 500f, 40f),
            Rect(3, -40f, -60f, -6f, 0f),
        }, 2, step => Rect(2, 20f - step, -60f, 60f - step, 0f), 26);
        Console.WriteLine($"      squeezed against a wall 6px behind her: dead={trapped.IsDead}");
        Check("with nowhere left to go she is crushed", trapped.IsDead);

        Console.WriteLine();
        Console.WriteLine("  A window arriving fast");
        // A big jump lands her deep inside. She must be swept along by what the window moved,
        // not thrown to the far edge of it -- the check is that the move is bounded by 30, the
        // distance the window travelled, rather than by the 400 it is wide.
        var fast = OnFloor(0f);
        var arriving = Rect(5, -12f, -60f, 388f, 0f);      // 400 wide, its left edge past her
        fast.Solids = new List<Solid> { Rect(1, -500f, 0f, 500f, 40f), arriving };
        float wasX = fast.Pos.X;
        fast.SweptInto(arriving, -30f, 0f);
        Console.WriteLine($"      window 400 wide jumped 30px onto her: she moved" +
            $" {Math.Abs(fast.Pos.X - wasX):F0}px");
        Check("she is swept by what the window moved, not by how wide it is",
            Math.Abs(fast.Pos.X - wasX) <= 30f);

        Console.WriteLine();
        Console.WriteLine("  A window she was already standing inside");
        // Clearing a wide window from the middle would be a throw; the clamp is what stops it,
        // so she is carried along by what the window moved and no more, frame after frame.
        var wide = Rect(4, -200f, -60f, 200f, 0f);       // 400 wide, she is deep inside it
        var standing = OnFloor(0f);
        standing.Solids = new List<Solid> { Rect(1, -500f, 0f, 500f, 40f), wide };
        Console.WriteLine($"      clearing it outright would be {standing.ClearanceX(wide, 1)}px");
        float carriedFrom = standing.Pos.X;
        standing.SweptInto(wide, 3f, 0f);
        Console.WriteLine($"      swept 3px: she moved {Math.Abs(standing.Pos.X - carriedFrom):F0}px");
        Check("she is carried by what it moved, not cleared out of it",
            Math.Abs(standing.Pos.X - carriedFrom) <= 3f);

        Console.WriteLine();
        Console.WriteLine("  A border sweeping through her, frame after frame");
        // The report that brought this about: one push, and then the edge walked through her.
        // Sweep a border across her a few pixels at a time and she must stay outside it for
        // every one of them, not just the first.
        var swept = OnFloor(0f);
        int insideFrames = 0;
        for (int step = 1; step <= 30; step++)
        {
            var edge = Rect(6, 40f - step * 3f, -60f, 48f - step * 3f, 0f);   // 8px wide border
            swept.Solids = new List<Solid> { Rect(1, -500f, 0f, 500f, 40f), edge };
            swept.SweptInto(edge, -3f, 0f);
            if (swept.OverlapsHitbox(edge)) insideFrames++;
            swept.Update(Dt, new PetInput());
        }
        Console.WriteLine($"      30 frames of a border crossing her: inside it on {insideFrames}," +
            $" ended at {swept.Pos.X:F0}");
        Check("the border never ends a frame overlapping her", insideFrames == 0);
        Check("and it took her along with it", swept.Pos.X < -20f);

        Console.WriteLine();
        Console.WriteLine("  The ride-along, now that it can be reached");
        // A drag of two thirds of a pixel a frame: three frames to the pixel, and the fraction
        // must be kept between them or a slow drag rounds away to nothing at all.
        var carried = OnFloor(0f);
        float startedAt = carried.Pos.X;
        for (int i = 0; i < 30; i++) carried.RideAlong(2f / 3f, 0f);
        Console.WriteLine($"      30 frames at two thirds of a pixel: moved" +
            $" {carried.Pos.X - startedAt:F0} of 20");
        Check("a slow drag is carried whole rather than rounded away",
            Math.Abs(carried.Pos.X - startedAt - 20f) <= 1f);
        // The fraction belongs to the window she is on; stepping onto another starts afresh.
        carried.RideAlong(0.4f, 0f);
        float beforeSwap = carried.Pos.X;
        carried.GroundId = new IntPtr(99);
        carried.RideAlong(0.4f, 0f);
        Check("and it does not carry over to the next window she stands on",
            Math.Abs(carried.Pos.X - beforeSwap) < 0.5f);

        Console.WriteLine();
        Console.WriteLine("  A platform rising under her feet");
        // The ride-along moves her in whole game pixels and keeps the fraction for later, so a
        // window rising faster than the rounding can follow leaves her a fraction inside its
        // border -- and inside is where a solid stops holding her up. The push is what settles
        // that fraction; without it she falls through the window she is standing on.
        var lifted = OnFloor(0f);
        float carriedRemainder = 0f;
        float platformTop = 0f;
        bool fellThrough = false;
        for (int frame = 0; frame < 60; frame++)
        {
            platformTop -= 2.4f;                      // rising 2.4px a frame: never a whole one
            var top = Rect(8, -200f, platformTop, 200f, platformTop + 8f);
            lifted.Solids = new List<Solid> { top };

            carriedRemainder += -2.4f;                // the ride-along, whole pixels only
            int ride = (int)Math.Round(carriedRemainder, MidpointRounding.ToEven);
            carriedRemainder -= ride;
            lifted.Pos = new PointF(lifted.Pos.X, lifted.Pos.Y + ride);

            lifted.SweptInto(top, 0f, -2.4f);         // and the push, settling the fraction
            lifted.Update(Dt, new PetInput());
            if (lifted.Pos.Y > platformTop + 8f) { fellThrough = true; break; }
        }
        Console.WriteLine($"      platform rose to {platformTop:F0}, she is at {lifted.Pos.Y:F0}");
        Check("she rides a platform that rises under her rather than falling through it",
            !fellThrough);
        Check("and is still standing on it at the end", Math.Abs(lifted.Pos.Y - platformTop) < 2f);

        Console.WriteLine();
        Console.WriteLine("  Crushed at any speed a hand can drag");
        // The report: only one speed killed her. Fast drags were being read as teleports and
        // slow ones give her room to wiggle clear, so the crush had a window in the middle.
        // This asks the same question at four speeds, against a wall two pixels behind her.
        foreach (int speed in new[] { 1, 4, 12, 40 })
        {
            var pressed = OnFloor(0f);
            pressed.Solids = new List<Solid>
            {
                Rect(1, -500f, 0f, 500f, 40f),
                Rect(3, -60f, -60f, -5f, 0f),        // wall just behind her, no room to wiggle
                Rect(7, -60f, -80f, 60f, -55f),      // ceiling, so up is no escape either
            };
            pressed.Update(Dt, new PetInput());
            for (int step = 1; step <= 200 / speed && !pressed.IsDead; step++)
            {
                var edge = Rect(2, 30f - step * speed, -60f, 90f - step * speed, 0f);
                pressed.Solids = new List<Solid>
                {
                    Rect(1, -500f, 0f, 500f, 40f),
                    Rect(3, -60f, -60f, -5f, 0f),
                    Rect(7, -60f, -80f, 60f, -55f),
                    edge,
                };
                pressed.SweptInto(edge, -speed, 0f);
                pressed.Update(Dt, new PetInput());
            }
            Console.WriteLine($"      {speed,2}px a frame ({speed * 60,4}px/s): dead={pressed.IsDead}");
            Check($"a window closing at {speed}px a frame crushes her", pressed.IsDead);
        }

        Console.WriteLine();
        Console.WriteLine("  A window that jumped rather than moved");
        // Past the teleport threshold PetWindow displaces her instead of pushing. The move
        // must be small and it must be the nearest way out, not the first one a fixed search
        // order happens to reach.
        var jumped = OnFloor(0f);
        var wasAt = jumped.Pos;
        jumped.Solids = new List<Solid>
        {
            Rect(1, -500f, 0f, 500f, 40f),
            Rect(2, -3f, -14f, 60f, -2f),          // dropped straight onto her, 12px thick
        };
        jumped.DisplaceOutOfSolids();
        float dx = Math.Abs(jumped.Pos.X - wasAt.X), dy = Math.Abs(jumped.Pos.Y - wasAt.Y);
        float radius = Math.Max(dx, dy);   // what the search minimises: rings, nearest first
        Console.WriteLine($"      displaced from {wasAt.X:F0},{wasAt.Y:F0}" +
            $" to {jumped.Pos.X:F0},{jumped.Pos.Y:F0} (ring {radius:F0})");
        Check("she is put somewhere free", !jumped.CollidesHere);
        Check($"and it is a short move, not a throw (ring {radius:F0} of 12)", radius <= 12f);
        // Nothing nearer was free: every spot in the rings before it is inside something.
        bool nearerFree = false;
        for (int r = 1; r < (int)radius && !nearerFree; r++)
            for (int x = -r; x <= r && !nearerFree; x++)
                for (int y = -r; y <= r && !nearerFree; y++)
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) == r &&
                        !jumped.CollidesAt(wasAt.X + x, wasAt.Y + y)) nearerFree = true;
        Check("and nothing nearer was free", !nearerFree);
        Check("she is alive", !jumped.IsDead);

        Console.WriteLine();
        Console.WriteLine("  Where a crush puts her back");
        var crushed = OnFloor(0f);
        // Somewhere else entirely, so that the stale respawn point is unmistakable if it wins.
        crushed.ResetTo(new PointF(400f, 0f));
        crushed.Pos = new PointF(0f, 0f);
        crushed.Solids = new List<Solid>
        {
            Rect(1, -500f, 0f, 500f, 40f),
            Rect(3, -40f, -60f, -6f, 0f),
        };
        crushed.Update(Dt, new PetInput());
        var died = crushed.Pos;
        Sweep(crushed, new List<Solid>
        {
            Rect(1, -500f, 0f, 500f, 40f),
            Rect(3, -40f, -60f, -6f, 0f),
        }, 2, step => Rect(2, 20f - step, -60f, 60f - step, 0f), 26);
        for (int i = 0; i < 240 && (crushed.IsDead || crushed.IsRespawning); i++)
            crushed.Update(Dt, new PetInput());
        float fromDeath = Math.Abs(crushed.Pos.X - died.X);
        float fromStale = Math.Abs(crushed.Pos.X - 400f);
        Console.WriteLine($"      crushed at {died.X:F0}, last reset at 400," +
            $" back at {crushed.Pos.X:F0},{crushed.Pos.Y:F0}");
        Check($"she comes back beside the crush ({fromDeath:F0}px) rather than at the stale" +
            $" reset point ({fromStale:F0}px away)", fromDeath < fromStale / 4f);
        Check("and somewhere she is not inside anything", !crushed.CollidesHere);

        Console.WriteLine();
        Console.WriteLine("  Everything else on the desktop, three pixels of wiggle and then gone");
        // Solid.MoveHExact walks every Actor. The crystal and the jellyfish get the plain
        // squish -- three by three, and then the crystal breaks and the jellyfish is removed.
        var world = new List<Solid>
        {
            Rect(1, -500f, 0f, 500f, 40f),
            Rect(3, -40f, -60f, -6f, 0f),        // wall behind
            Rect(7, -60f, -80f, 60f, -55f),      // ceiling above
        };
        var box = new PointF(0f, 0f);
        bool squished = false;
        for (int step = 1; step <= 30 && !squished; step++)
        {
            var edge = Rect(2, 20f - step * 2f, -60f, 80f - step * 2f, 0f);
            var solids = new List<Solid>(world) { edge };
            squished = !ActorSweep.Push(solids, ref box, 4f, -10f, 0f, edge, -2f, 0f);
        }
        Console.WriteLine($"      a crystal-sized box pinned against a wall: squished={squished}");
        Check("what is not the player is squished by the same rule", squished);

        var loose = new PointF(0f, 0f);
        var open = new List<Solid> { Rect(1, -500f, 0f, 500f, 40f) };
        var sweeper = Rect(2, -2f, -60f, 60f, 0f);
        bool survived = ActorSweep.Push(new List<Solid>(open) { sweeper }, ref loose, 4f, -10f, 0f,
            sweeper, -4f, 0f);
        Console.WriteLine($"      with room to move it is pushed to {loose.X:F0} instead");
        Check("and pushed rather than squished when there is room", survived && loose.X < 0f);

        // A seeker is not hung the way the others are: its box is centred on its position
        // rather than standing on it, so a box measured downwards from there misses it
        // entirely, and it could not be crushed at all.
        var seekerAt = new PointF(0f, -20f);
        var pen = new List<Solid>
        {
            Rect(1, -500f, 0f, 500f, 40f),
            Rect(3, -40f, -60f, -8f, 0f),
            Rect(7, -60f, -80f, 60f, -30f),
        };
        bool seekerSquished = false;
        for (int step = 1; step <= 30 && !seekerSquished; step++)
        {
            var edge = Rect(2, 20f - step * 2f, -60f, 80f - step * 2f, 0f);
            var solids = new List<Solid>(pen) { edge };
            seekerSquished = !ActorSweep.Push(solids, ref seekerAt, Seeker.HalfSize,
                -Seeker.HalfSize, Seeker.HalfSize, edge, -2f, 0f);
        }
        Console.WriteLine($"      a seeker pinned the same way: squished={seekerSquished}");
        Check("a seeker can be crushed, its box being centred rather than standing",
            seekerSquished);

        // And it goes at once rather than lingering through its own death: vanilla's seeker
        // calls RemoveSelf and hands the burst to an entity of its own, so nothing of it is
        // left to be collided with. PetWindow owns the burst here for the same reason.
        var doomed = new Seeker(new PointF(0f, 0f));
        doomed.Crush();
        bool heardDeath = false;
        while (doomed.SoundEvents.Count > 0)
            heardDeath |= doomed.SoundEvents.Dequeue().Path.EndsWith("seeker_death");
        Check("a crushed seeker sounds its death", heardDeath);
        Check("and is gone at once, leaving nothing behind to collide with", doomed.Removed);

        Console.WriteLine();
        Console.WriteLine("  A platform yanked upwards, fast");
        // Dragging a window up is the case that drops things through it: the border rises into
        // whatever is standing on it, and anything left inside a solid is no longer held up by
        // it. Ride, push, then her own update, in the order PetWindow does them -- at speeds
        // from a crawl to faster than the ride can carry whole.
        foreach (float lift in new[] { 0.6f, 2.4f, 7f, 15f })
        {
            var riding2 = OnFloor(0f);
            float top = 0f, remainder = 0f;
            bool lost = false;
            for (int frame = 0; frame < 40; frame++)
            {
                top -= lift;
                var platform = Rect(9, -200f, top, 200f, top + 8f);
                riding2.Solids = new List<Solid> { platform };

                remainder += -lift;                       // the ride-along, whole pixels only
                int ride = (int)Math.Round(remainder, MidpointRounding.ToEven);
                remainder -= ride;
                riding2.Pos = new PointF(riding2.Pos.X, riding2.Pos.Y + ride);

                riding2.SweptInto(platform, 0f, -lift);
                riding2.Update(Dt, new PetInput());
                if (riding2.Pos.Y > top + 8f) { lost = true; break; }
            }
            Console.WriteLine($"      lifted {lift,4} a frame: she is at {riding2.Pos.Y:F0}," +
                $" the platform at {top:F0}");
            Check($"she stays on a platform pulled up {lift} a frame", !lost);
        }

        Console.WriteLine();
        Console.WriteLine("  A jellyfish with nowhere to go");
        // It is never squished, so it stays wherever the window leaves it -- and staying must
        // be quiet. Something wedged in a border that bounces off it every frame sounds its
        // landing every frame, which is the noise this is here to catch.
        var jelly = new Glider(new PointF(0f, -2f));
        var jellyPen = new List<Solid>
        {
            Rect(1, -500f, 0f, 500f, 40f),
            Rect(3, -40f, -60f, -6f, 0f),
            Rect(7, -60f, -80f, 60f, -20f),
        };
        var jellyHeard = new List<string>();
        for (int step = 1; step <= 60; step++)
        {
            var edge = Rect(2, 30f - step, -60f, 90f - step, 0f);
            var solids = new List<Solid>(jellyPen) { edge };
            var at = jelly.Pos;
            ActorSweep.Push(solids, ref at, Glider.HalfWidth, -Glider.ColliderHeight, 0f,
                edge, -1f, 0f);
            jelly.Pos = at;
            jelly.Update(Dt, new PetInput(), solids, -100000f, 100000f);
            while (jelly.SoundEvents.Count > 0)
                jellyHeard.Add(jelly.SoundEvents.Dequeue().Path.Replace(
                    "event:/new_content/game/10_farewell/", ""));
        }
        Console.WriteLine($"      60 frames wedged: {jellyHeard.Count} sounds" +
            (jellyHeard.Count == 0 ? "" : " -- " + string.Join(", ", jellyHeard.GetRange(0,
                Math.Min(6, jellyHeard.Count)))));
        Check($"a wedged jellyfish does not sound every frame ({jellyHeard.Count})",
            jellyHeard.Count <= 2);

        // The other kind: standing on a solid that moves is PetWindow's ride-along, which is
        // not in Player at all -- so a solid moving under her leaves her where she was.
        var riding = OnFloor(0f);
        float before = riding.Pos.X;
        for (int step = 0; step < 20; step++)
        {
            riding.Solids = new List<Solid> { Rect(1, -500f + step * 4f, 0f, 500f + step * 4f, 40f) };
            riding.Update(Dt, new PetInput());
        }
        Console.WriteLine($"      floor slid 80px right; she is at {riding.Pos.X:F0} (was {before:F0})");
        Check("the floor sliding under her still does not carry her (PetWindow does that)",
            Math.Abs(riding.Pos.X - before) < 0.5f);

        return failed;
    }
}
