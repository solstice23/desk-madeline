using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// The pinball bumper of Chapter 6: what it does to her, and what it does to everything else,
// which is nothing. Vanilla's Bumper adds a PlayerCollider and no other, so the crystal, the
// jellyfish and the seeker go through it as though it were not there -- that is the port being
// right rather than the port being unfinished.
static class BumperChecks
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

    /// <summary>A bumper that is not wandering, so that where it is is where it was put.</summary>
    static Bumper Still(PointF at) => new Bumper(at, 0f);

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("BUMPERS: the thing that will not let her stand near it");
        Console.WriteLine(new string('=', 74));

        Console.WriteLine("  Walking into one");
        // Her centre is five and a half above her feet, so a bumper level with it and to the
        // right is one she meets side on.
        var player = OnFloor(0f);
        var bumper = Still(new PointF(player.Center.X + 14f, player.Center.Y));
        bumper.Update(Dt, player);
        Console.WriteLine($"      speed {player.Speed.X:F0},{player.Speed.Y:F0}"
            + $" state {player.State} after {bumper.Hits} hit(s)");
        Check("it throws her", bumper.Hits == 1);
        Check("away from itself, level, at 280", Math.Abs(player.Speed.X + 280f) < .001f);
        // ExplodeLaunch: anything not already flying upwards leaves at 150 up, and autojumps.
        Check("and up at 150, since a level launch would leave her on the floor",
            Math.Abs(player.Speed.Y + 150f) < .001f);
        Check("she is launched", player.State == Player.StLaunch);
        Check("with her dash back", player.Dashes >= 1);
        Check("and it sounds", Heard(bumper).Contains("pinballbumper_hit"));

        Console.WriteLine();
        Console.WriteLine("  And then sitting out six tenths of a second");
        var again = OnFloor(0f);
        // Near enough that the wander cannot carry it out of her reach: three pixels of drift
        // on top of seven from her side is still inside the circle's twelve.
        var resting = Still(new PointF(again.Center.X + 11f, again.Center.Y));
        resting.Update(Dt, again);
        Heard(resting);
        Check("the first one throws her", resting.Hits == 1);

        // Held against it for half a second: it is out, and does nothing.
        for (int i = 0; i < 35; i++) { again.Pos = new PointF(0f, 0f); resting.Update(Dt, again); }
        Console.WriteLine($"      after half a second against it: {resting.Hits} hit(s), ready={resting.Ready}");
        Check("it will not throw her again while it is out", resting.Hits == 1);
        Check("and it is still out at half a second", !resting.Ready);

        // Out of its reach for the rest of the wait, so that coming back is what fires it.
        for (int i = 0; i < 8; i++) { again.Pos = new PointF(-100f, 0f); resting.Update(Dt, again); }
        Console.WriteLine($"      and at six tenths, with her away: ready={resting.Ready}");
        Check("back by six tenths", resting.Ready);
        Check("saying so", Heard(resting).Contains("pinballbumper_reset"));
        again.Pos = new PointF(0f, 0f);
        resting.Update(Dt, again);
        Check("and throwing her again when she comes back", resting.Hits == 2);

        Console.WriteLine();
        Console.WriteLine("  Hit it and then hold the dash button down");
        // The report: two dashes out of one bumper. It hands her the dash back as it throws
        // her -- ExplodeLaunch refills -- so she has exactly one, and nothing should give her
        // another until she lands or finds something that refills.
        var spamming = OnFloor(0f);
        var once = Still(new PointF(spamming.Center.X + 11f, spamming.Center.Y));
        int dashes = 0, wasState = spamming.State, airborne = 0;
        bool launched = false;
        for (int frame = 0; frame < 120; frame++)
        {
            once.Update(Dt, spamming);
            if (once.Hits > 0) launched = true;
            var input = new PetInput { MoveX = -1, AimX = -1 };
            spamming.BufferDash();
            input.DashPressed = spamming.HasDashBuffer;
            spamming.Update(Dt, input);
            // Only what she gets out of the bumper: landing hands the dash back as it always
            // does, and dashes after that are the floor's doing rather than the bumper's.
            if (launched && spamming.onGround && airborne > 3) break;
            if (launched && !spamming.onGround) airborne++;
            if (launched && spamming.State == Player.StDash && wasState != Player.StDash) dashes++;
            wasState = spamming.State;
        }
        Console.WriteLine($"      one hit, dash held: {dashes} dash(es) before landing again"
            + $" ({airborne} frames in the air)");
        Check("one bumper, one dash", dashes == 1);

        Console.WriteLine();
        Console.WriteLine("  Coming at it from above and to one side");
        // The bumper asks ExplodeLaunch not to snap her upright, which is what a puffer asks
        // for. Arriving mostly from above at an angle, she keeps the angle.
        var falling = OnFloor(0f);
        falling.Pos = new PointF(0f, -30f);
        var below = Still(new PointF(falling.Center.X + 6f, falling.Center.Y + 12f));
        below.Update(Dt, falling);
        Console.WriteLine($"      speed {falling.Speed.X:F0},{falling.Speed.Y:F0}");
        Check("she is thrown up and out, not straight up",
            falling.Speed.Y < 0f && Math.Abs(falling.Speed.X) > 1f);

        Console.WriteLine();
        Console.WriteLine("  Standing clear of one");
        var clear = OnFloor(0f);
        var far = Still(new PointF(clear.Center.X + 30f, clear.Center.Y));
        for (int i = 0; i < 60; i++) clear.Update(Dt, new PetInput());
        for (int i = 0; i < 60; i++) far.Update(Dt, clear);
        Check("nothing happens", far.Hits == 0 && far.Ready);
        // Circle(12) against her eight-wide box: her right side is four from her middle, so it
        // reaches her at sixteen and not at seventeen.
        Check("its reach is the circle's twelve, from her hitbox",
            clear.OverlapsCircle(new PointF(clear.Center.X + 16f, clear.Center.Y), 12f) &&
            !clear.OverlapsCircle(new PointF(clear.Center.X + 17f, clear.Center.Y), 12f));

        Console.WriteLine();
        Console.WriteLine("  Everything else goes through it");
        // Vanilla adds a PlayerCollider and nothing else. There is nothing in Bumper for a
        // crystal or a jellyfish to collide with, so this is what the port has to do too.
        var ignored = Still(new PointF(0f, 0f));
        var alone = OnFloor(400f);                 // her, well out of the way
        var crystal = new TheoCrystal(new PointF(0f, 0f));
        var jelly = new Glider(new PointF(0f, 0f));
        var seeker = new Seeker(new PointF(0f, 0f));
        PointF crystalWas = crystal.Pos, jellyWas = jelly.Pos, seekerWas = seeker.Pos;
        var world = new List<Solid> { Floor() };
        for (int i = 0; i < 30; i++)
        {
            ignored.Update(Dt, alone);
            crystal.Update(Dt, alone, world, new RectangleF(-1000f, -1000f, 2000f, 2000f));
            jelly.Update(Dt, new PetInput(), world, -100000f, 100000f);
        }
        Console.WriteLine($"      after half a second on top of one: {ignored.Hits} hit(s),"
            + $" crystal moved {Math.Abs(crystal.Pos.X - crystalWas.X):F0} across,"
            + $" jelly {Math.Abs(jelly.Pos.X - jellyWas.X):F0}, seeker {Math.Abs(seeker.Pos.X - seekerWas.X):F0}");
        Check("it does not fire for them", ignored.Hits == 0);
        Check("nor throw them anywhere",
            Math.Abs(crystal.Pos.X - crystalWas.X) < .001f &&
            Math.Abs(jelly.Pos.X - jellyWas.X) < .001f &&
            Math.Abs(seeker.Pos.X - seekerWas.X) < .001f);
        Check("and it stays ready for her", ignored.Ready);

        Console.WriteLine();
        Console.WriteLine("  Its wander, and being put somewhere else");
        var drifting = new Bumper(new PointF(100f, 100f));
        float loX = float.MaxValue, hiX = float.MinValue, loY = float.MaxValue, hiY = float.MinValue;
        var nobody = OnFloor(400f);
        for (int i = 0; i < 60 * 12; i++)
        {
            drifting.Update(Dt, nobody);
            loX = Math.Min(loX, drifting.Pos.X); hiX = Math.Max(hiX, drifting.Pos.X);
            loY = Math.Min(loY, drifting.Pos.Y); hiY = Math.Max(hiY, drifting.Pos.Y);
        }
        Console.WriteLine($"      over twelve seconds it wandered {hiX - loX:F1} across"
            + $" and {hiY - loY:F1} down");
        Check("three pixels either way across", hiX - loX > 5.5f && hiX - loX <= 6.01f);
        Check("and two down, which makes it an ellipse rather than a line",
            hiY - loY > 3.5f && hiY - loY <= 4.01f);
        Check("about where it was put", loX >= 97f && hiX <= 103f && loY >= 98f && hiY <= 102f);

        drifting.BeginDrag();
        drifting.DragTo(new PointF(-50f, -50f));
        var underneath = OnFloor(-50f);
        underneath.Pos = new PointF(-50f, -44.5f + 5.5f);
        drifting.Update(Dt, underneath);
        Check("dragging moves it", Math.Abs(drifting.Anchor.X + 50f) < .001f);
        Check("and it throws nobody while it is being carried", drifting.Hits == 0);
        drifting.EndDrag();
        drifting.Update(Dt, underneath);
        Check("put down on top of her, it throws her again", drifting.Hits == 1);

        return failed;
    }

    static string Heard(Bumper bumper)
    {
        var heard = new List<string>();
        while (bumper.SoundEvents.Count > 0) heard.Add(bumper.SoundEvents.Dequeue().Path);
        return string.Join(", ", heard);
    }
}
