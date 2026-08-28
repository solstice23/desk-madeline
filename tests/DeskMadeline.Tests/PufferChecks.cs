using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// The pufferfish of Farewell: landed on it is knocked away, walked into it goes off.
//
// Unlike the bumper it does reach past her: Puffer.Explode launches a Theo crystal caught in
// the blast as well, so that is checked here too. What it has nothing to say to -- the
// jellyfish, the seeker -- goes through it, which is again what the game does.
static class PufferChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static Solid Floor() =>
        new Solid { Id = new IntPtr(1), L = -500f, T = 0f, R = 500f, B = 40f };

    static Player OnFloor(float x)
    {
        var player = new Player
        {
            Solids = new List<Solid> { Floor() },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Pos = new PointF(x, 0f)
        };
        for (int i = 0; i < 5; i++) player.Update(Dt, new PetInput());
        return player;
    }

    /// <summary>One that is not swimming about, so that where it is is where it was put.</summary>
    static Puffer Still(PointF at) => new Puffer(at, true, 0f);

    static readonly List<Solid> World = new List<Solid> { Floor() };
    static readonly List<TheoCrystal> NoCrystals = new List<TheoCrystal>();

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("PUFFERFISH: bounce off the top, or set it off");
        Console.WriteLine(new string('=', 74));

        Console.WriteLine("  Landing on top of one");
        // Her feet above where it sits: Puffer.OnPlayer bounces her rather than bursting.
        var lander = OnFloor(0f);
        lander.Pos = new PointF(0f, -14f);
        var underfoot = Still(new PointF(0f, -8f));
        for (int i = 0; i < 4 && underfoot.State == Puffer.States.Idle; i++)
        {
            underfoot.Update(Dt, lander, World, NoCrystals);
            lander.Update(Dt, new PetInput());
        }
        Console.WriteLine($"      she is going {lander.Speed.Y:F0} up, it is {underfoot.State}");
        Check("she is bounced", lander.Speed.Y < -80f);
        Check("her dash comes back with it", lander.Dashes >= 1);
        Check("and it is knocked downwards", underfoot.State == Puffer.States.Hit);
        Check("with the boop", Heard(underfoot).Contains("puffer_boop"));

        Console.WriteLine();
        Console.WriteLine("  Walking into one");
        // Level with her, so her feet are below it: that is the burst rather than the bounce.
        var walker = OnFloor(0f);
        var beside = Still(new PointF(walker.Center.X + 9f, walker.Center.Y));
        beside.Update(Dt, walker, World, NoCrystals);
        Console.WriteLine($"      she is going {walker.Speed.X:F0},{walker.Speed.Y:F0},"
            + $" it is {beside.State}");
        Check("it goes off", beside.State == Puffer.States.Gone);
        Check("she is thrown away from it, sideways", walker.Speed.X < -200f);
        // sidesOnly: a puffer throws her along the ground rather than up over itself.
        Check("and level, which is what sidesOnly means",
            Math.Abs(walker.Speed.Y + 150f) < .001f);
        Check("she is launched", walker.State == Player.StLaunch);
        Check("with the bang", Heard(beside).Contains("puffer_splode"));

        Console.WriteLine();
        Console.WriteLine("  A crystal caught in the blast");
        // Puffer.Explode: a TheoCrystal within forty pixels is thrown too, at 120.
        var away = OnFloor(20f);
        var crystal = new TheoCrystal(new PointF(20f, -6f));
        var beside2 = Still(new PointF(0f, -10f));
        var crystals = new List<TheoCrystal> { crystal };
        // Set off by hand, since she is nowhere near it.
        var near = OnFloor(0f);
        near.Pos = new PointF(-6f, 0f);
        beside2.Update(Dt, near, World, crystals);
        Console.WriteLine($"      it went off; the crystal is going {crystal.Speed.X:F0},{crystal.Speed.Y:F0}");
        Check("the crystal is thrown as well", Math.Abs(crystal.Speed.X) > 1f);
        Check("away from it", crystal.Speed.X > 0f);
        Check("at a hundred and twenty",
            Math.Abs((float)Math.Sqrt(crystal.Speed.X * crystal.Speed.X +
                crystal.Speed.Y * crystal.Speed.Y) - 120f) < .5f);

        // One being carried is hers, and stays hers.
        var held = new TheoCrystal(new PointF(20f, 0f));
        bool picked = held.Pickup(away);
        Check("(she has hold of one)", picked);
        var beside3 = Still(new PointF(0f, -10f));
        var near2 = OnFloor(0f);
        near2.Pos = new PointF(-6f, 0f);
        beside3.Update(Dt, near2, World, new List<TheoCrystal> { held });
        Check("but one she is holding is not taken off her",
            Math.Abs(held.Speed.X) < .001f && Math.Abs(held.Speed.Y) < .001f);

        Console.WriteLine();
        Console.WriteLine("  What it has nothing to say to");
        var jelly = new Glider(new PointF(0f, -10f));
        var seeker = new Seeker(new PointF(0f, -10f));
        PointF jellyWas = jelly.Pos, seekerWas = seeker.Pos;
        var quiet = Still(new PointF(0f, -10f));
        var faraway = OnFloor(400f);
        for (int i = 0; i < 60; i++)
        {
            quiet.Update(Dt, faraway, World, NoCrystals);
            jelly.Update(Dt, new PetInput(), World, -100000f, 100000f);
        }
        Console.WriteLine($"      a second sitting inside one: it is {quiet.State},"
            + $" the seeker moved {Math.Abs(seeker.Pos.X - seekerWas.X):F0}");
        Check("a jellyfish and a seeker do not set it off", quiet.State == Puffer.States.Idle);
        Check("and it does not throw them", Math.Abs(seeker.Pos.X - seekerWas.X) < .001f);

        Console.WriteLine();
        Console.WriteLine("  Gone, and back again");
        var burst = Still(new PointF(0f, -10f));
        var setter = OnFloor(0f);
        setter.Pos = new PointF(-6f, 0f);
        burst.Update(Dt, setter, World, NoCrystals);
        Heard(burst);
        Check("it is gone", burst.State == Puffer.States.Gone);
        // Two and a half seconds, of which the last half is the swim home.
        var alone = OnFloor(400f);
        for (int i = 0; i < 60 * 2; i++) burst.Update(Dt, alone, World, NoCrystals);
        Check("still gone at two seconds", burst.State == Puffer.States.Gone);
        for (int i = 0; i < 40; i++) burst.Update(Dt, alone, World, NoCrystals);
        Console.WriteLine($"      after two and a half: {burst.State}, back at"
            + $" {burst.Pos.X:F0},{burst.Pos.Y:F0}");
        Check("back by two and a half", burst.State == Puffer.States.Idle);
        Check("where it started", Math.Abs(burst.Pos.X) < 3.5f && Math.Abs(burst.Pos.Y + 10f) < 3.5f);
        Check("saying so", Heard(burst).Contains("puffer_reform"));

        Console.WriteLine();
        Console.WriteLine("  Its wander, and being put somewhere else");
        var drifting = new Puffer(new PointF(100f, -100f));
        var nobody = OnFloor(400f);
        float loX = float.MaxValue, hiX = float.MinValue, loY = float.MaxValue, hiY = float.MinValue;
        for (int i = 0; i < 60 * 10; i++)
        {
            drifting.Update(Dt, nobody, World, NoCrystals);
            loX = Math.Min(loX, drifting.Pos.X); hiX = Math.Max(hiX, drifting.Pos.X);
            loY = Math.Min(loY, drifting.Pos.Y); hiY = Math.Max(hiY, drifting.Pos.Y);
        }
        Console.WriteLine($"      it swam {hiX - loX:F0} across and {hiY - loY:F0} down,"
            + $" about {loX:F0}..{hiX:F0}");
        Check("about three pixels either way across", hiX - loX >= 4f && hiX - loX <= 7f);
        Check("and two down", hiY - loY >= 3f && hiY - loY <= 5f);

        drifting.BeginDrag();
        drifting.DragTo(new PointF(-50f, -50f));
        drifting.EndDrag();
        drifting.Update(Dt, nobody, World, NoCrystals);
        Check("dragging moves it", Math.Abs(drifting.Pos.X + 50f) < 4f);
        // Where it starts from moves with it, so a burst brings it back to where it was left
        // rather than to wherever it was spawned.
        var moved = Still(new PointF(0f, -10f));
        moved.BeginDrag();
        moved.DragTo(new PointF(-80f, -30f));
        moved.EndDrag();
        var trigger = OnFloor(-86f);
        trigger.Pos = new PointF(-86f, -20f);
        for (int i = 0; i < 3 && moved.State != Puffer.States.Gone; i++)
            moved.Update(Dt, trigger, World, NoCrystals);
        for (int i = 0; i < 60 * 3; i++) moved.Update(Dt, alone, World, NoCrystals);
        Console.WriteLine($"      dragged, set off, and back at {moved.Pos.X:F0},{moved.Pos.Y:F0}");
        Check("and it comes home to where it was put, not to where it was made",
            Math.Abs(moved.Pos.X + 80f) < 6f);

        Console.WriteLine();
        Console.WriteLine("  What it draws besides itself");
        // Puffer.Render is most of what a puffer looks like: the outline, the arc it watches
        // her along, and the one pixel of eye. The arc is the part with geometry to get wrong.
        var watcher = Still(new PointF(0f, -40f));
        var her = OnFloor(0f);
        var at = new PointF[28];
        var inward = new PointF[28];
        var alpha = new float[28];

        // Far away: it is not watching anything.
        var distant = OnFloor(300f);
        for (int i = 0; i < 90; i++) watcher.Update(Dt, distant, World, NoCrystals);
        Console.WriteLine($"      with her three hundred away, fade {watcher.AggroFade:F2}");
        Check("nothing is drawn for somebody far off", watcher.AggroArc(at, inward, alpha) == 0);

        // Near: it lights up, and every mark of it sits on the circle it is drawn on.
        for (int i = 0; i < 90; i++) watcher.Update(Dt, her, World, NoCrystals);
        int marks = watcher.AggroArc(at, inward, alpha);
        float nearest = float.MaxValue, furthest = 0f;
        float best = 0f, bestAngle = 0f;
        for (int i = 0; i < marks; i++)
        {
            float dx = at[i].X - watcher.Pos.X, dy = at[i].Y - watcher.Pos.Y;
            float outFrom = (float)Math.Sqrt(dx * dx + dy * dy);
            nearest = Math.Min(nearest, outFrom);
            furthest = Math.Max(furthest, outFrom);
            if (alpha[i] > best) { best = alpha[i]; bestAngle = (float)Math.Atan2(dy, dx); }
        }
        Console.WriteLine($"      with her below it: {marks} mark(s), {nearest:F0}..{furthest:F0}"
            + $" from it, brightest at {bestAngle * 180f / (float)Math.PI:F0} degrees");
        Check("it is watching her", marks > 0);
        // Thirty-two, give or take the pixel of shimmer each mark carries.
        Check("every mark is on the circle it draws", nearest >= 30f && furthest <= 34f);
        // She is straight below it, and down is ninety degrees with y pointing down.
        Check("and the brightest of them is the way she is",
            Math.Abs(bestAngle * 180f / (float)Math.PI - 90f) < 25f);
        Check("none of them is brighter than the arc ever gets", best <= .8f + .001f);

        Console.WriteLine();
        Console.WriteLine("  The outline, and the eye");
        Check("it is outlined while it is there", watcher.Outlined);
        Check("and has an eye once it is puffed up", watcher.HasEye);
        PointF eye = watcher.Eye;
        Console.WriteLine($"      its eye is at {eye.X - watcher.Pos.X:F0},{eye.Y - watcher.Pos.Y:F0}"
            + " from the middle of it");
        Check("which sits on the fish rather than out in the air",
            Math.Abs(eye.X - watcher.Pos.X) <= 6f && Math.Abs(eye.Y - watcher.Pos.Y) <= 8f);

        // Gone, and there is nothing of it to draw at all.
        var vanished = Still(new PointF(0f, -10f));
        var setoff = OnFloor(0f);
        setoff.Pos = new PointF(-6f, 0f);
        vanished.Update(Dt, setoff, World, NoCrystals);
        Check("a puffer that has gone off draws no arc",
            vanished.AggroArc(at, inward, alpha) == 0);

        Console.WriteLine();
        Console.WriteLine("  A window moving into one (Solid.MoveHExact, Puffer.OnSquish)");
        // Every Actor in the scene is walked by a moving solid, and a puffer is one -- it was
        // the only thing loose on the desktop that a window used to pass straight through.
        var shoved = Still(new PointF(0f, -40f));
        var wall = new Solid { Id = new IntPtr(7), L = -60f, T = -80f, R = -10f, B = 0f };
        var world = new List<Solid> { Floor(), wall };
        // Eight pixels to the right, arriving over it: the fish is six wide either way, so a
        // wall whose right edge reaches -10 + 8 has it by four.
        var sweeping = new Solid
        { Id = wall.Id, L = wall.L + 8f, T = wall.T, R = wall.R + 8f, B = wall.B };
        world[1] = sweeping;
        var pushedTo = shoved.Pos;
        bool cleared = ActorSweep.Push(world, ref pushedTo, Puffer.HalfWidth,
            -Puffer.HalfHeight, Puffer.HalfHeight, sweeping, 8f, 0f);
        shoved.Pos = pushedTo;
        Console.WriteLine($"      a window sweeping into it left it at {shoved.Pos.X:F0}"
            + $" (it was at 0, the window's edge is now at {sweeping.R:F0})");
        Check("a window sweeping into a puffer shoves it along", cleared && shoved.Pos.X > 0f);
        Check("just far enough to be clear of it, and no further than the window went",
            Math.Abs(shoved.Pos.X - (sweeping.R + Puffer.HalfWidth)) < 0.01f &&
            shoved.Pos.X <= 8f);

        // Against something solid behind it there is nowhere to go, and vanilla's answer to
        // that is not to leave it inside: Puffer.OnSquish sets it off and it is gone.
        var trapped = Still(new PointF(0f, -40f));
        var behind = new Solid { Id = new IntPtr(8), L = 6f, T = -80f, R = 60f, B = 0f };
        var vice = new List<Solid> { Floor(), behind,
            new Solid { Id = new IntPtr(9), L = -60f, T = -80f, R = -6f, B = 0f } };
        var closing = new Solid { Id = new IntPtr(9), L = -56f, T = -80f, R = -2f, B = 0f };
        vice[2] = closing;
        var nowhere = trapped.Pos;
        bool clearedTrap = ActorSweep.Push(vice, ref nowhere, Puffer.HalfWidth,
            -Puffer.HalfHeight, Puffer.HalfHeight, closing, 4f, 0f);
        if (!clearedTrap) trapped.Squish(faraway, vice, NoCrystals);
        Check("one with a wall behind it cannot be pushed clear", !clearedTrap);
        Check("so it goes off where it stands, as Puffer.OnSquish does",
            trapped.State == Puffer.States.Gone && trapped.Explosions == 1);
        // Two and a half seconds and it swims home, which is why it needs no sparing.
        for (int i = 0; i < 60 * 3; i++) trapped.Update(Dt, faraway, vice, NoCrystals);
        Check($"and comes back on its own, so a nudge of a window costs nothing"
            + $" ({trapped.State})", trapped.State == Puffer.States.Idle);

        // It floats: Puffer.IsRiding is false for a solid, so a window under one carries it
        // nowhere however far it goes.
        var floating = Still(new PointF(0f, -12f));
        var under = new Solid { Id = new IntPtr(10), L = -60f, T = 0f, R = 60f, B = 40f };
        Check("a window it is floating over never carries it",
            !ActorSweep.RidingOn(under, floating.Pos, Puffer.HalfWidth, Puffer.HalfHeight));

        return failed;
    }

    static string Heard(Puffer puffer)
    {
        var heard = new List<string>();
        while (puffer.SoundEvents.Count > 0) heard.Add(puffer.SoundEvents.Dequeue().Path);
        return string.Join(", ", heard);
    }
}
