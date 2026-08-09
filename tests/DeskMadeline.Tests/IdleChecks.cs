using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// Idle autonomy: the director that plays her when nobody else is.
//
// Its only output is the same PetInput the keyboard fills, so the whole thing runs headless:
// build a world of solids, let the director drive the real Player, and watch where she ends
// up. The structural promise checked throughout is that she never dashes on her own -- in
// kevin mode an autonomous dash would fling the user's windows about.
static class IdleChecks
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

    static Player OnFloor(float x, params Solid[] extra)
    {
        var solids = new List<Solid> { Floor() };
        solids.AddRange(extra);
        var player = new Player
        {
            Solids = solids,
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(x, 0f)
        };
        for (int i = 0; i < 5; i++) player.Update(Dt, new PetInput());
        return player;
    }

    static readonly List<RectangleF> OneMonitor = new List<RectangleF>
    { new RectangleF(-400f, -300f, 800f, 340f) };

    static IdleContext Context(Player player, bool fullscreen = false,
        List<Glider> gliders = null, List<Seeker> seekers = null)
        => new IdleContext
        {
            Player = player,
            Solids = player.Solids,
            Monitors = OneMonitor,
            Cursor = new PointF(1000f, 1000f),
            ForegroundFullscreen = fullscreen,
            SeekersDormant = false,
            Gliders = gliders ?? new List<Glider>(),
            Seekers = seekers ?? new List<Seeker>(),
            Puffers = new List<Puffer>(),
            Windows = new List<KeyValuePair<IntPtr, RectangleF>>(),
        };

    static bool dashedEver;

    static PetInput Step(IdleDirector director, Player player, IdleContext ctx)
    {
        PetInput input = director.Drive(Dt, ctx);
        if (input.DashPressed) dashedEver = true;
        player.Update(Dt, input);
        if (player.State == Player.StDash) dashedEver = true;
        return input;
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("IDLE AUTONOMY: what she does when nobody is playing");
        Console.WriteLine(new string('=', 74));
        dashedEver = false;

        Console.WriteLine("  The arbiter");
        var director = new IdleDirector(new Random(7));
        var player = OnFloor(0f);
        var ctx = Context(player);
        for (int i = 0; i < (int)(4.5f / Dt); i++) director.Drive(Dt, ctx);
        Check("four and a half quiet seconds are not yet hers", !director.Engaged);
        for (int i = 0; i < (int)(1f / Dt); i++) director.Drive(Dt, ctx);
        Check("five and a half are", director.Engaged);
        director.NoteRealInput();
        Check("one real key and she is the player's again, that frame", !director.Engaged);

        Console.WriteLine();
        Console.WriteLine("  Walking somewhere on purpose");
        var walker = OnFloor(0f);
        var walkDirector = new IdleDirector(new Random(7));
        walkDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(90f, 0f));
        var walkCtx = Context(walker);
        int frames = 0;
        for (; frames < (int)(12f / Dt) && Math.Abs(walker.Pos.X - 90f) > 3f; frames++)
            Step(walkDirector, walker, walkCtx);
        Console.WriteLine($"      she reached x={walker.Pos.X:F0} in {frames * Dt:F1}s");
        Check("she walks to where she decided to go", Math.Abs(walker.Pos.X - 90f) <= 3f);

        Console.WriteLine();
        Console.WriteLine("  Climbing a window to sit on it");
        var box = new Solid { Id = new IntPtr(9), L = 60f, T = -50f, R = 160f, B = 0f };
        var climber = OnFloor(0f, box);
        var climbDirector = new IdleDirector(new Random(7));
        climbDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(60f, -50f, 100f, 50f));
        var climbCtx = Context(climber);
        for (frames = 0; frames < (int)(20f / Dt); frames++)
        {
            Step(climbDirector, climber, climbCtx);
            if (climber.onGround && Math.Abs(climber.Pos.Y + 50f) <= 2f &&
                climber.Pos.X > 62f && climber.Pos.X < 158f) break;
        }
        Console.WriteLine($"      she is at {climber.Pos.X:F0},{climber.Pos.Y:F0}"
            + $" after {frames * Dt:F1}s (the top is at -50)");
        Check("she grabs the side, climbs, and stands on top",
            climber.onGround && Math.Abs(climber.Pos.Y + 50f) <= 2f);

        Console.WriteLine();
        Console.WriteLine("  Giving up gracefully");
        var tall = new Solid { Id = new IntPtr(9), L = 60f, T = -400f, R = 160f, B = 0f };
        var stuck = OnFloor(0f, tall);
        var stuckDirector = new IdleDirector(new Random(7));
        stuckDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(60f, -400f, 100f, 400f));
        var stuckCtx = Context(stuck);
        int abandoned = -1;
        for (frames = 0; frames < (int)(15f / Dt); frames++)
        {
            Step(stuckDirector, stuck, stuckCtx);
            if (stuckDirector.Current != IdleDirector.Activity.ClimbWindow) { abandoned = frames; break; }
        }
        Console.WriteLine($"      a four-hundred-pixel window, more than her stamina:"
            + $" gave up after {(abandoned < 0 ? -1f : abandoned * Dt):F1}s");
        Check("the watchdog abandons what is not working", abandoned >= 0 && abandoned * Dt < 10f);
        Console.WriteLine();
        Console.WriteLine("  Jumping onto what a jump can reach");
        var low = new Solid { Id = new IntPtr(9), L = 80f, T = -22f, R = 200f, B = 0f };
        var hopper = OnFloor(0f, low);
        var hopDirector = new IdleDirector(new Random(7));
        hopDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(160f, -22f));
        var hopCtx = Context(hopper);
        for (frames = 0; frames < (int)(12f / Dt); frames++)
        {
            Step(hopDirector, hopper, hopCtx);
            if (hopper.onGround && hopper.Pos.Y <= -20f && hopper.Pos.X > 82f) break;
        }
        Console.WriteLine($"      a twenty-two-pixel window in the path: she is at"
            + $" {hopper.Pos.X:F0},{hopper.Pos.Y:F0} after {frames * Dt:F1}s");
        Check("she jumps onto it rather than stalling against it",
            hopper.onGround && hopper.Pos.Y <= -20f);

        Console.WriteLine();
        Console.WriteLine("  Sleeping through a film");
        var sleeper = OnFloor(0f);
        var sleepDirector = new IdleDirector(new Random(7));
        sleepDirector.ForceEngageForCheck();
        var filmCtx = Context(sleeper, fullscreen: true);
        for (int i = 0; i < (int)(3f / Dt); i++) Step(sleepDirector, sleeper, filmCtx);
        Check("something fullscreen in front means she naps",
            sleepDirector.Current == IdleDirector.Activity.Nap && sleepDirector.Napping);
        sleepDirector.NoteRealInput();
        Check("woken by a key, she asks for the wake-up animation",
            sleepDirector.ConsumeWakeRequest());
        Check("and only once", !sleepDirector.ConsumeWakeRequest());

        Console.WriteLine();
        Console.WriteLine("  The jellyfish errand");
        var carrier = OnFloor(0f);
        var jelly = new Glider(new PointF(60f, 0f));
        carrier.Holdables = new List<IPetHoldable> { jelly };
        var carryDirector = new IdleDirector(new Random(7));
        carryDirector.ForceActivityForCheck(IdleDirector.Activity.CarryJelly,
            jelly.Pos, jelly: jelly);
        var carryCtx = Context(carrier, gliders: new List<Glider> { jelly });
        bool held = false;
        for (frames = 0; frames < (int)(30f / Dt); frames++)
        {
            PetInput input = Step(carryDirector, carrier, carryCtx);
            // The shell places what she holds each frame; this loop is the shell here.
            carrier.UpdateCarryPosition(0f);
            jelly.Update(Dt, input, carrier.Solids, -100000f, 100000f);
            held |= carrier.IsHoldingGlider;
            if (held && !carrier.IsHoldingGlider) break;
        }
        Console.WriteLine($"      picked up: {held}; put down at {jelly.Pos.X:F0}"
            + $" (it began at 60); holding now: {carrier.IsHoldingGlider}");
        Check("she picks the jellyfish up", held);
        Check("carries it somewhere else and sets it down",
            !carrier.IsHoldingGlider && Math.Abs(jelly.Pos.X - 60f) > 15f);

        Console.WriteLine();
        Console.WriteLine("  What she never does on her own");
        Check("not one dash was pressed in any of it", !dashedEver);

        return failed;
    }
}
