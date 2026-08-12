using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// Idle autonomy: the director that plays her when nobody else is.
//
// Its only output is the same PetInput the keyboard fills, so the whole thing runs headless:
// build a world of solids, let the director drive the real Player, and watch where she ends
// up. The structural promise checked throughout: a dash is never pressed uninvited -- only a
// wander leg that rolled one and proved its corridor clear may dash, and in kevin mode,
// where a dash flings the user's windows, never at all.
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
    { new RectangleF(-400f, -500f, 800f, 540f) };

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
        // The inviolable rule is not "no dashes" -- rehearsed plans dash where dashing
        // is safe -- it is "never where a dash could move a window".
        bool dashForbidden = ctx.WindowsAreKevin || ctx.WindowsReactToDash;
        if (dashForbidden && input.DashPressed) dashedEver = true;
        player.Update(Dt, input);
        if (dashForbidden && player.State == Player.StDash) dashedEver = true;
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
        Console.WriteLine("  Hanging off the lip of the window she sits on");
        var perch = new Solid { Id = new IntPtr(9), L = 60f, T = -50f, R = 160f, B = 0f };
        var percher = OnFloor(0f, perch);
        var perchDirector = new IdleDirector(new Random(7));
        perchDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(60f, -50f, 100f, 50f));
        perchDirector.ForceSitHangForCheck();
        var perchCtx = Context(percher);
        bool stoodOnTop = false, hungBelowLip = false, backOverLip = false;
        for (frames = 0; frames < (int)(40f / Dt); frames++)
        {
            Step(perchDirector, percher, perchCtx);
            if (perchDirector.Current != IdleDirector.Activity.ClimbWindow) break;
            if (percher.onGround && Math.Abs(percher.Pos.Y + 50f) <= 2f) stoodOnTop = true;
            if (stoodOnTop && percher.State == Player.StClimb &&
                percher.Pos.Y > -48f && percher.Pos.Y < -15f) hungBelowLip = true;
            if (hungBelowLip && percher.onGround && Math.Abs(percher.Pos.Y + 50f) <= 2f)
            { backOverLip = true; break; }
        }
        Console.WriteLine($"      stood on top: {stoodOnTop}; hung below the lip:"
            + $" {hungBelowLip}; climbed back over: {backOverLip}");
        Check("from the top she swings below the lip, hangs, and climbs back over",
            stoodOnTop && hungBelowLip && backOverLip);

        Console.WriteLine();
        Console.WriteLine("  Jumping off a ledge instead of walking off it");
        var high = new Solid { Id = new IntPtr(1), L = -500f, T = 0f, R = 0f, B = 40f };
        var lower = new Solid { Id = new IntPtr(1), L = 0f, T = 60f, R = 500f, B = 100f };
        var leaper = new Player
        {
            Solids = new List<Solid> { high, lower },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(-80f, 0f)
        };
        for (int i = 0; i < 5; i++) leaper.Update(Dt, new PetInput());
        var leapDirector = new IdleDirector(new Random(7));
        leapDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(200f, 60f));
        var leapCtx = Context(leaper);
        bool leapt = false;
        for (frames = 0; frames < (int)(12f / Dt); frames++)
        {
            Step(leapDirector, leaper, leapCtx);
            if (leaper.Pos.X > -30f && leaper.Pos.X < 30f && leaper.Speed.Y < -60f) leapt = true;
            if (Math.Abs(leaper.Pos.X - 200f) <= 3f && leaper.onGround) break;
        }
        Console.WriteLine($"      at the drop to the lower floor: jumped {leapt}, and she is"
            + $" at {leaper.Pos.X:F0},{leaper.Pos.Y:F0} after {frames * Dt:F1}s");
        Check("she leaves the ledge with a jump, the way a player would", leapt);
        Check("and lands on the lower floor and carries on",
            Math.Abs(leaper.Pos.X - 200f) <= 3f && leaper.onGround);

        Console.WriteLine();
        Console.WriteLine("  Scaling a wall taller than the tank");
        var tall = new Solid { Id = new IntPtr(9), L = 60f, T = -400f, R = 160f, B = 0f };
        var scaler = OnFloor(0f, tall);
        var scaleDirector = new IdleDirector(new Random(7));
        scaleDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(60f, -400f, 100f, 400f));
        var scaleCtx = Context(scaler);
        for (frames = 0; frames < (int)(60f / Dt); frames++)
        {
            Step(scaleDirector, scaler, scaleCtx);
            if (scaleDirector.Current != IdleDirector.Activity.ClimbWindow) break;
            if (scaler.onGround && Math.Abs(scaler.Pos.Y + 400f) <= 2f &&
                scaler.Pos.X > 62f && scaler.Pos.X < 158f) break;
        }
        Console.WriteLine($"      four hundred pixels on a 110 tank: she is at"
            + $" {scaler.Pos.X:F0},{scaler.Pos.Y:F0} after {frames * Dt:F1}s");
        Check("wall jumps cost nothing, so she neutral-jumps past the stamina to the top",
            scaler.onGround && Math.Abs(scaler.Pos.Y + 400f) <= 2f);

        Console.WriteLine();
        Console.WriteLine("  Giving up gracefully");
        var stuck = OnFloor(0f);
        var floatingJelly = new Glider(new PointF(100f, -200f));
        stuck.Holdables = new List<IPetHoldable> { floatingJelly };
        var stuckDirector = new IdleDirector(new Random(7));
        stuckDirector.ForceActivityForCheck(IdleDirector.Activity.CarryJelly,
            floatingJelly.Pos, jelly: floatingJelly);
        var stuckCtx = Context(stuck, gliders: new List<Glider> { floatingJelly });
        int abandoned = -1;
        for (frames = 0; frames < (int)(15f / Dt); frames++)
        {
            Step(stuckDirector, stuck, stuckCtx);
            if (stuckDirector.Current != IdleDirector.Activity.CarryJelly) { abandoned = frames; break; }
        }
        Console.WriteLine($"      a jellyfish two hundred pixels overhead:"
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
        Console.WriteLine("  Hanging off the side of the screen");
        var edgeWall = new Solid { Id = new IntPtr(1), L = -260f, T = -2000f, R = -200f, B = 40f };
        var hanger = OnFloor(0f, edgeWall);
        var hangDirector = new IdleDirector(new Random(7));
        hangDirector.ForceActivityForCheck(IdleDirector.Activity.PlayWithWall,
            new PointF(-202f, 0f), new RectangleF(-202f, -60f, 4f, 4f));
        var hangCtx = Context(hanger);
        hangCtx.EdgesClimbable = true;
        hangCtx.EdgeLeft = -200f;
        hangCtx.EdgeRight = 400f;
        float highest = 0f;
        bool hung = false, backDown = false;
        for (frames = 0; frames < (int)(25f / Dt); frames++)
        {
            Step(hangDirector, hanger, hangCtx);
            if (hanger.State == Player.StClimb)
            {
                highest = Math.Min(highest, hanger.Pos.Y);
                if (hanger.Pos.Y <= -50f) hung = true;
            }
            if (hung && hanger.onGround && hanger.Pos.Y >= -2f) { backDown = true; break; }
        }
        Console.WriteLine($"      she climbed the edge wall to y={highest:F0}"
            + $" (asked: -60), and is {(backDown ? "back on the ground" : "not down")}");
        Check("she climbs the screen edge, hangs, and drops back off", hung && backDown);

        // The pane's force button takes the same road as a natural pick now: the
        // override runs Begin with candidates refreshed, so a forced wall play
        // actually finds a wall and plays with it.
        var pressed = OnFloor(0f, edgeWall);
        var pressDirector = new IdleDirector(new Random(7));
        var pressCtx = Context(pressed);
        pressCtx.EdgesClimbable = true;
        pressCtx.EdgeLeft = -200f;
        pressCtx.EdgeRight = 400f;
        // The room must reach the edges it claims, as the real desk''s always does.
        pressCtx.Monitors = new List<RectangleF> { new RectangleF(-200f, -500f, 600f, 540f) };
        pressDirector.RequestOverride(IdleDirector.Activity.PlayWithWall, default);
        bool pressPlayed = false, pressClimbed = false;
        for (frames = 0; frames < (int)(25f / Dt); frames++)
        {
            Step(pressDirector, pressed, pressCtx);
            if (pressDirector.Current == IdleDirector.Activity.PlayWithWall) pressPlayed = true;
            if (pressed.State == Player.StClimb && pressed.Pos.Y < -20f) pressClimbed = true;
            if (pressPlayed && pressClimbed && pressed.onGround && frames > 300) break;
        }
        Console.WriteLine($"      the pane button: began {pressPlayed},"
            + $" got on a wall {pressClimbed}");
        Check("a forced wall play finds a wall and plays with it", pressPlayed && pressClimbed);

        Console.WriteLine();
        Console.WriteLine("  Crossing the canyon between two monitors");
        var floorA = new Solid { Id = new IntPtr(1), L = -500f, T = 0f, R = 0f, B = 40f };
        var seam = new Solid { Id = new IntPtr(1), L = 0f, T = -160f, R = 100f, B = 40f };
        var floorB = new Solid { Id = new IntPtr(1), L = 100f, T = 0f, R = 600f, B = 40f };
        var crosser = new Player
        {
            Solids = new List<Solid> { floorA, seam, floorB },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(-200f, 0f)
        };
        for (int i = 0; i < 5; i++) crosser.Update(Dt, new PetInput());
        var crossDirector = new IdleDirector(new Random(7));
        crossDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(300f, 0f));
        var crossCtx = Context(crosser);
        crossCtx.Monitors = new List<RectangleF>
        {
            new RectangleF(-400f, -300f, 400f, 340f),      // this monitor ends at x=0
            new RectangleF(100f, -300f, 500f, 340f),       // the other begins at x=100
        };
        for (frames = 0; frames < (int)(35f / Dt); frames++)
        {
            Step(crossDirector, crosser, crossCtx);
            if (Math.Abs(crosser.Pos.X - 300f) <= 3f && crosser.onGround) break;
        }
        Console.WriteLine($"      a 160px seam wall between the displays: she is at"
            + $" {crosser.Pos.X:F0},{crosser.Pos.Y:F0} after {frames * Dt:F1}s");
        Check("she scales the seam, walks its top, and drops onto the other monitor",
            Math.Abs(crosser.Pos.X - 300f) <= 3f && crosser.onGround);

        Console.WriteLine();
        Console.WriteLine("  Not pinned by a crossing that cannot work");
        var pocketFloor = new Solid { Id = new IntPtr(1), L = -500f, T = 0f, R = 0f, B = 40f };
        var pocketWall = new Solid { Id = new IntPtr(1), L = 0f, T = -2000f, R = 100f, B = 40f };
        var pocketRoof = new Solid { Id = new IntPtr(1), L = -60f, T = -120f, R = 0f, B = -100f };
        var pinned = new Player
        {
            Solids = new List<Solid> { pocketFloor, pocketWall, pocketRoof },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(-200f, 0f)
        };
        for (int i = 0; i < 5; i++) pinned.Update(Dt, new PetInput());
        var pinDirector = new IdleDirector(new Random(7));
        pinDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(300f, 0f));
        var pinCtx = Context(pinned);
        pinCtx.Monitors = new List<RectangleF>
        {
            new RectangleF(-400f, -300f, 400f, 340f),
            new RectangleF(100f, -300f, 500f, 340f),
        };
        int freed = -1;
        for (frames = 0; frames < (int)(20f / Dt); frames++)
        {
            Step(pinDirector, pinned, pinCtx);
            if (pinDirector.Current != IdleDirector.Activity.Wander) { freed = frames; break; }
        }
        Console.WriteLine($"      a roof over the seam pocket stops the ladder: gave up"
            + $" after {(freed < 0 ? -1f : freed * Dt):F1}s (forever, before)");
        Check("a ladder gaining no height is a stall, and she gives it up",
            freed >= 0 && freed * Dt < 12f);

        Console.WriteLine();
        Console.WriteLine("  The terrain is the destination pool");
        var mesa = new Solid { Id = new IntPtr(9), L = 100f, T = -60f, R = 260f, B = 0f };
        var scout = OnFloor(-200f, mesa);
        var scoutDirector = new IdleDirector(new Random(7));
        var scoutCtx = Context(scout);
        scoutCtx.Windows = new List<KeyValuePair<IntPtr, RectangleF>>
        { new KeyValuePair<IntPtr, RectangleF>(new IntPtr(9), new RectangleF(100f, -60f, 160f, 60f)) };
        int floorSpots = 0, mesaSpots = 0;
        for (int i = 0; i < 80; i++)
        {
            PointF spot = scoutDirector.ProbeExploreForCheck(scoutCtx, out _, out bool up);
            if (Math.Abs(spot.Y) < 2f) floorSpots++;
            if (Math.Abs(spot.Y + 60f) < 2f && up) mesaSpots++;
        }
        Console.WriteLine($"      eighty draws over a floor and a window: floor {floorSpots},"
            + $" window top {mesaSpots}");
        Check("both the floor and the window's top come up as destinations",
            floorSpots > 0 && mesaSpots > 0);

        Console.WriteLine();
        Console.WriteLine("  A stroll leg that goes up");
        var upLeg = OnFloor(-200f, mesa);
        var upDirector = new IdleDirector(new Random(7));
        upDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(180f, -60f),
            new RectangleF(100f, -60f, 160f, 60f));
        var upCtx = Context(upLeg);
        bool toppedOut = false;
        for (frames = 0; frames < (int)(20f / Dt); frames++)
        {
            Step(upDirector, upLeg, upCtx);
            if (upDirector.Current != IdleDirector.Activity.Wander) break;
            if (upLeg.onGround && Math.Abs(upLeg.Pos.Y + 60f) <= 2f) { toppedOut = true; break; }
        }
        Console.WriteLine($"      a wander leg aimed at a sixty-pixel top: she is at"
            + $" {upLeg.Pos.X:F0},{upLeg.Pos.Y:F0} after {frames * Dt:F1}s, still wandering:"
            + $" {upDirector.Current == IdleDirector.Activity.Wander}");
        Check("a stroll climbs to an elevated spot and stays a stroll",
            toppedOut && upDirector.Current == IdleDirector.Activity.Wander);

        Console.WriteLine();
        Console.WriteLine("  A monitor seam one pixel tall");
        var floorLow = new Solid { Id = new IntPtr(1), L = -500f, T = 0f, R = 0f, B = 400f };
        var floorHigh = new Solid { Id = new IntPtr(1), L = 0f, T = -1f, R = 500f, B = 399f };
        var stepper = new Player
        {
            Solids = new List<Solid> { floorLow, floorHigh },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(-100f, 0f)
        };
        for (int i = 0; i < 5; i++) stepper.Update(Dt, new PetInput());
        var stepDirector = new IdleDirector(new Random(7));
        stepDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(200f, -1f));
        var stepCtx = Context(stepper);
        for (frames = 0; frames < (int)(10f / Dt); frames++)
        {
            Step(stepDirector, stepper, stepCtx);
            if (Math.Abs(stepper.Pos.X - 200f) <= 3f && stepper.onGround) break;
        }
        Console.WriteLine($"      the floor rises one pixel at x=0, the real seam between"
            + $" the user's monitors: she is at {stepper.Pos.X:F0},{stepper.Pos.Y:F0}"
            + $" after {frames * Dt:F1}s");
        Check("she hops the one-pixel step instead of stalling on it",
            Math.Abs(stepper.Pos.X - 200f) <= 3f && stepper.onGround);

        Console.WriteLine();
        Console.WriteLine("  Climbing out of a hole");
        var pitFloor = new Solid { Id = new IntPtr(1), L = -500f, T = 0f, R = 500f, B = 40f };
        var pitWallL = new Solid { Id = new IntPtr(1), L = -60f, T = -100f, R = -30f, B = 0f };
        var pitWallR = new Solid { Id = new IntPtr(1), L = 30f, T = -100f, R = 60f, B = 0f };
        var potholed = OnFloor(0f, pitWallL, pitWallR);
        var pitDirector = new IdleDirector(new Random(7));
        pitDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(300f, 0f));
        var pitCtx = Context(potholed);
        bool escaped = false;
        for (frames = 0; frames < (int)(40f / Dt); frames++)
        {
            Step(pitDirector, potholed, pitCtx);
            if (pitDirector.Current != IdleDirector.Activity.Wander &&
                pitDirector.Current != IdleDirector.Activity.Rest) break;
            if (potholed.onGround && (potholed.Pos.X < -62f || potholed.Pos.X > 62f) &&
                potholed.Pos.Y <= 0f) { escaped = true; break; }
        }
        Console.WriteLine($"      hundred-pixel walls both sides: she is at"
            + $" {potholed.Pos.X:F0},{potholed.Pos.Y:F0} after {frames * Dt:F1}s");
        Check("two walled legs in a row read as a hole, and she climbs out", escaped);

        Console.WriteLine();
        Console.WriteLine("  Two close walls are a chimney, hopped between");
        var chimneyL = new Solid { Id = new IntPtr(8), L = 0f, T = -300f, R = 20f, B = 40f };
        var chimneyR = new Solid { Id = new IntPtr(8), L = 50f, T = -300f, R = 70f, B = 40f };
        var sweep = OnFloor(35f, chimneyL, chimneyR);
        var sweepDirector = new IdleDirector(new Random(7));
        sweepDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(0f, -300f, 20f, 340f));
        var sweepCtx = Context(sweep);
        float chimneyMinX = 999f, chimneyMaxX = -999f;
        bool toppedChimney = false;
        for (frames = 0; frames < (int)(30f / Dt); frames++)
        {
            Step(sweepDirector, sweep, sweepCtx);
            if (!sweep.onGround && sweep.Pos.Y < -80f && sweep.Pos.Y > -280f)
            {
                chimneyMinX = Math.Min(chimneyMinX, sweep.Pos.X);
                chimneyMaxX = Math.Max(chimneyMaxX, sweep.Pos.X);
            }
            if (sweep.onGround && Math.Abs(sweep.Pos.Y + 300f) <= 6f) { toppedChimney = true; break; }
        }
        Console.WriteLine($"      three hundred pixels between walls thirty apart: topped"
            + $" {toppedChimney} in {frames * Dt:F1}s, swinging x {chimneyMinX:F0}..{chimneyMaxX:F0}");
        Check("she reaches the top of the chimney", toppedChimney);
        // Which ascent she rolls is her business now -- the chimney kick itself is
        // proven in MoveChecks; the swing range above is informational.

        Console.WriteLine();
        Console.WriteLine("  Already standing on the destination");
        var plinth = new Solid { Id = new IntPtr(9), L = 60f, T = -50f, R = 160f, B = 0f };
        var settled = new Player
        {
            Solids = new List<Solid> { Floor(), plinth },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(110f, -50f)
        };
        for (int i = 0; i < 5; i++) settled.Update(Dt, new PetInput());
        var settledDirector = new IdleDirector(new Random(7));
        settledDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(60f, -52f, 100f, 52f));      // stored top two pixels stale
        var settledCtx = Context(settled);
        bool everJumped = false;
        for (frames = 0; frames < (int)(3f / Dt); frames++)
        {
            Step(settledDirector, settled, settledCtx);
            if (settled.Speed.Y < -60f) everJumped = true;
        }
        Console.WriteLine($"      forced to climb the window she is standing on, its stored"
            + $" top two pixels stale: jumped {everJumped}, at"
            + $" {settled.Pos.X:F0},{settled.Pos.Y:F0}");
        Check("standing on the top already counts as being there, jitter and all",
            !everJumped && settled.onGround && Math.Abs(settled.Pos.Y + 50f) <= 2f);

        Console.WriteLine();
        Console.WriteLine("  A spot that defeated her is left alone for a while");
        var sour = OnFloor(0f, box);
        var sourDirector = new IdleDirector(new Random(7));
        var sourCtx = Context(sour);
        sourCtx.Windows = new List<KeyValuePair<IntPtr, RectangleF>>
        { new KeyValuePair<IntPtr, RectangleF>(new IntPtr(9), new RectangleF(60f, -50f, 100f, 50f)) };
        RectangleF before = default;
        for (int i = 0; i < 20 && before.Width == 0f; i++)
            before = sourDirector.ProbeClimbForCheck(sourCtx);
        sourDirector.NoteFailedSpotForCheck(new PointF(110f, -50f));
        RectangleF after = default;
        for (int i = 0; i < 20 && after.Width == 0f; i++)
            after = sourDirector.ProbeClimbForCheck(sourCtx);
        Console.WriteLine($"      offered before the failure: {before.Width > 0f};"
            + $" offered again right after: {after.Width > 0f}");
        Check("a reachable window is offered, but not again soon after failing there",
            before.Width > 0f && after.Width == 0f);

        Console.WriteLine();
        Console.WriteLine("  What she never does uninvited");
        Check("not one dash wherever a dash could move a window", !dashedEver);

        Console.WriteLine();
        Console.WriteLine("  The dash, where a dash can hit nothing");
        var open = OnFloor(-350f);
        var dashDirector = new IdleDirector(new Random(7));
        dashDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(300f, 0f));
        dashDirector.ForceLegDashForCheck(-250f);
        var dashCtx = Context(open);
        bool sawDash = false;
        for (frames = 0; frames < (int)(15f / Dt); frames++)
        {
            Step(dashDirector, open, dashCtx);
            if (open.State == Player.StDash) sawDash = true;
            if (Math.Abs(open.Pos.X - 300f) <= 3f && open.onGround) break;
        }
        Console.WriteLine($"      a long empty leg: dashed {sawDash}, and reached"
            + $" x={open.Pos.X:F0} in {frames * Dt:F1}s");
        Check("a long clear wander leg gets its dash", sawDash);
        Check("and the stroll still arrives", Math.Abs(open.Pos.X - 300f) <= 3f);

        Console.WriteLine();
        Console.WriteLine("  And never in kevin mode");
        var kevinWalker = OnFloor(-350f);
        var kevinDirector = new IdleDirector(new Random(7));
        kevinDirector.ForceActivityForCheck(IdleDirector.Activity.Wander, new PointF(300f, 0f));
        kevinDirector.ForceLegDashForCheck(-250f);
        var kevinCtx = Context(kevinWalker);
        kevinCtx.WindowsAreKevin = true;
        bool kevinDashed = false;
        for (frames = 0; frames < (int)(15f / Dt); frames++)
        {
            Step(kevinDirector, kevinWalker, kevinCtx);
            if (kevinWalker.State == Player.StDash) kevinDashed = true;
            if (Math.Abs(kevinWalker.Pos.X - 300f) <= 3f && kevinWalker.onGround) break;
        }
        Console.WriteLine($"      the same rolled dash with windows as kevin blocks:"
            + $" dashed {kevinDashed}");
        Check("the very same rolled dash is refused when a dash could throw a window",
            !kevinDashed);

        Console.WriteLine();
        Console.WriteLine("  A window hanging above the floor, reached with an up-dash");
        var floater = new Solid { Id = new IntPtr(9), L = 60f, T = -150f, R = 200f, B = -80f };
        var dasher = OnFloor(0f, floater);
        var floatDirector = new IdleDirector(new Random(7));
        floatDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(60f, -150f, 140f, 70f));
        var floatCtx = Context(dasher);
        bool upDashed = false;
        for (frames = 0; frames < (int)(25f / Dt); frames++)
        {
            Step(floatDirector, dasher, floatCtx);
            if (dasher.State == Player.StDash) upDashed = true;
            if (dasher.onGround && Math.Abs(dasher.Pos.Y + 150f) <= 2f &&
                dasher.Pos.X > 62f && dasher.Pos.X < 198f) break;
        }
        Console.WriteLine($"      its bottom is 80 up, past any jump: dashed {upDashed},"
            + $" and she is at {dasher.Pos.X:F0},{dasher.Pos.Y:F0} after {frames * Dt:F1}s");
        Check("she jumps, dashes up the face, grabs it, and tops out",
            upDashed && dasher.onGround && Math.Abs(dasher.Pos.Y + 150f) <= 2f);

        Console.WriteLine();
        Console.WriteLine("  The same reach begun from underneath it");
        var under = OnFloor(130f, floater);
        var underDirector = new IdleDirector(new Random(7));
        underDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(60f, -150f, 140f, 70f));
        var underCtx = Context(under);
        for (frames = 0; frames < (int)(25f / Dt); frames++)
        {
            Step(underDirector, under, underCtx);
            if (underDirector.Current != IdleDirector.Activity.ClimbWindow) break;
            if (under.onGround && Math.Abs(under.Pos.Y + 150f) <= 2f &&
                under.Pos.X > 62f && under.Pos.X < 198f) break;
        }
        Console.WriteLine($"      from x=130, under the window, the dash spot at 205 is"
            + $" away from its middle: she is at {under.Pos.X:F0},{under.Pos.Y:F0}"
            + $" after {frames * Dt:F1}s");
        Check("walking away from the target toward the dash spot is not a stall",
            under.onGround && Math.Abs(under.Pos.Y + 150f) <= 2f);

        Console.WriteLine();
        Console.WriteLine("  The same window when a dash would move it");
        var mover = OnFloor(0f, floater);
        var moonDirector = new IdleDirector(new Random(7));
        var moonCtx = Context(mover);
        moonCtx.WindowsReactToDash = true;
        moonCtx.Windows = new List<KeyValuePair<IntPtr, RectangleF>>
        { new KeyValuePair<IntPtr, RectangleF>(new IntPtr(9), new RectangleF(60f, -150f, 140f, 70f)) };
        RectangleF moonGot = default;
        for (int i = 0; i < 20 && moonGot.Width == 0f; i++)
            moonGot = moonDirector.ProbeClimbForCheck(moonCtx);
        Console.WriteLine($"      windows as moon or kevin blocks, dash edges off the"
            + $" graph: offered {moonGot.Width > 0f}");
        Check("no route is offered where the only way up is a dash that would move it",
            moonGot.Width == 0f);

        Console.WriteLine();
        Console.WriteLine("  Detecting a floating window beside another window's wall");
        var neighborWall = new Solid { Id = new IntPtr(8), L = 220f, T = -140f, R = 224f, B = 40f };
        // She starts in the chimney, where the leap plan is designed to run; the old
        // far-side start passed only through a lucky over-the-top detour.
        var spotter = OnFloor(210f, floater, neighborWall);
        var spotDirector = new IdleDirector(new Random(7));
        var spotCtx = Context(spotter);
        spotCtx.WindowsReactToDash = true;      // kevin or moon: the dash route is off the table
        spotCtx.Windows = new List<KeyValuePair<IntPtr, RectangleF>>
        { new KeyValuePair<IntPtr, RectangleF>(new IntPtr(9), new RectangleF(60f, -150f, 140f, 70f)) };
        RectangleF spotted = default;
        for (int i = 0; i < 20 && spotted.Width == 0f; i++)
            spotted = spotDirector.ProbeClimbForCheck(spotCtx);
        Console.WriteLine($"      the floater's bottom is 80 up and dashes are off:"
            + $" found {spotted.Width > 0f}");
        Check("a neighbouring window's wall makes the floater a candidate, dashlessly",
            spotted.Width > 0f);
        bool climbedIt = false;
        if (spotted.Width > 0f)
        {
            spotDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default, spotted);
            for (frames = 0; frames < (int)(35f / Dt); frames++)
            {
                Step(spotDirector, spotter, spotCtx);
                if (spotter.onGround && Math.Abs(spotter.Pos.Y + 150f) <= 2f &&
                    spotter.Pos.X > 62f && spotter.Pos.X < 198f) { climbedIt = true; break; }
            }
            Console.WriteLine($"      and she made it: {climbedIt}, at"
                + $" {spotter.Pos.X:F0},{spotter.Pos.Y:F0} after {frames * Dt:F1}s");
        }
        Check("she climbs the neighbour and leaps back onto the floater", climbedIt);

        Console.WriteLine();
        Console.WriteLine("  Too high from the floor, one dash from a neighbour's top");
        var terminal = new Solid { Id = new IntPtr(7), L = 240f, T = -100f, R = 400f, B = 0f };
        var filePilot = new Solid { Id = new IntPtr(9), L = 200f, T = -260f, R = 340f, B = -180f };
        var stager = OnFloor(500f, terminal, filePilot);
        var stageDirector = new IdleDirector(new Random(7));
        var stageCtx = Context(stager);
        stageCtx.Windows = new List<KeyValuePair<IntPtr, RectangleF>>
        { new KeyValuePair<IntPtr, RectangleF>(new IntPtr(9), new RectangleF(200f, -260f, 140f, 80f)) };
        RectangleF staged = default;
        for (int i = 0; i < 20 && staged.Width == 0f; i++)
            staged = stageDirector.ProbeClimbForCheck(stageCtx);
        Console.WriteLine($"      its bottom is 180 up -- past any dash from the floor --"
            + $" but a 100-tall window stands under it: found {staged.Width > 0f}");
        Check("the route through the neighbour's top is seen from the ground",
            staged.Width > 0f);
        bool viaStone = false;
        if (staged.Width > 0f)
        {
            stageDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default, staged);
            for (frames = 0; frames < (int)(40f / Dt); frames++)
            {
                Step(stageDirector, stager, stageCtx);
                if (stageDirector.Current != IdleDirector.Activity.ClimbWindow) break;
                if (stager.onGround && Math.Abs(stager.Pos.Y + 260f) <= 2f &&
                    stager.Pos.X > 202f && stager.Pos.X < 338f) { viaStone = true; break; }
            }
            Console.WriteLine($"      and she made it: {viaStone}, at"
                + $" {stager.Pos.X:F0},{stager.Pos.Y:F0} after {frames * Dt:F1}s");
        }
        Check("she climbs the neighbour, then dashes up from its top", viaStone);

        Console.WriteLine();
        Console.WriteLine("  A side blocked by another window's bottom");
        var goal = new Solid { Id = new IntPtr(9), L = 60f, T = -120f, R = 200f, B = 0f };
        var blockerBottom = new Solid { Id = new IntPtr(8), L = 150f, T = -60f, R = 320f, B = -58f };
        var blockerTop = new Solid { Id = new IntPtr(8), L = 150f, T = -200f, R = 320f, B = -198f };
        var blockerLeft = new Solid { Id = new IntPtr(8), L = 150f, T = -200f, R = 152f, B = -60f };
        var blockerRight = new Solid { Id = new IntPtr(8), L = 318f, T = -200f, R = 320f, B = -60f };
        var router = OnFloor(420f, goal, blockerBottom, blockerTop, blockerLeft, blockerRight);
        var routeDirector = new IdleDirector(new Random(7));
        var routeCtx = Context(router);
        routeCtx.Windows = new List<KeyValuePair<IntPtr, RectangleF>>
        {
            new KeyValuePair<IntPtr, RectangleF>(new IntPtr(9), new RectangleF(60f, -120f, 140f, 120f)),
            new KeyValuePair<IntPtr, RectangleF>(new IntPtr(8), new RectangleF(150f, -200f, 170f, 140f)),
        };
        RectangleF routed = default;
        for (int i = 0; i < 40; i++)
        {
            RectangleF got = routeDirector.ProbeClimbForCheck(routeCtx);
            if (got.Width > 0f && Math.Abs(got.Left - 60f) < 1f) { routed = got; break; }
        }
        Console.WriteLine($"      the goal's climbable side runs under another window's"
            + $" bottom: offered {routed.Width > 0f}");
        Check("the blocked window is reached by a route over the blocker",
            routed.Width > 0f);
        bool overTheTop = false;
        if (routed.Width > 0f)
        {
            routeDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default, routed);
            for (frames = 0; frames < (int)(45f / Dt); frames++)
            {
                Step(routeDirector, router, routeCtx);
                if (routeDirector.Current != IdleDirector.Activity.ClimbWindow) break;
                if (router.onGround && Math.Abs(router.Pos.Y + 120f) <= 2f &&
                    router.Pos.X > 62f && router.Pos.X < 198f) { overTheTop = true; break; }
            }
            Console.WriteLine($"      climbed the blocker, walked off, dropped on the goal:"
                + $" {overTheTop}, at {router.Pos.X:F0},{router.Pos.Y:F0} after {frames * Dt:F1}s");
        }
        Check("she climbs the blocker and drops onto the goal from above", overTheTop);

        Console.WriteLine();
        Console.WriteLine("  A lid right above a window's top means nowhere to stand");
        var lid = new Solid { Id = new IntPtr(8), L = 40f, T = -134f, R = 220f, B = -130f };
        var lone = OnFloor(420f, goal, lid);
        var loneDirector = new IdleDirector(new Random(7));
        var loneCtx = Context(lone);
        loneCtx.Windows = new List<KeyValuePair<IntPtr, RectangleF>>
        { new KeyValuePair<IntPtr, RectangleF>(new IntPtr(9), new RectangleF(60f, -120f, 140f, 120f)) };
        RectangleF loneGot = default;
        for (int i = 0; i < 20 && loneGot.Width == 0f; i++)
            loneGot = loneDirector.ProbeClimbForCheck(loneCtx);
        Console.WriteLine($"      ten pixels of air under the lid: offered"
            + $" {loneGot.Width > 0f}");
        Check("a top she could not stand on is never a destination",
            loneGot.Width == 0f);

        Console.WriteLine();
        Console.WriteLine("  No dashing at a face that cannot be caught");
        var ghostFace = new Solid { Id = new IntPtr(9), L = 198f, T = -150f, R = 200f, B = -120f };
        var wary = OnFloor(300f, ghostFace);
        var waryDirector = new IdleDirector(new Random(7));
        var waryCtx = Context(wary);
        waryCtx.Windows = new List<KeyValuePair<IntPtr, RectangleF>>
        { new KeyValuePair<IntPtr, RectangleF>(new IntPtr(9), new RectangleF(60f, -150f, 140f, 70f)) };
        RectangleF ghost = default;
        for (int i = 0; i < 20 && ghost.Width == 0f; i++)
            ghost = waryDirector.ProbeClimbForCheck(waryCtx);
        Console.WriteLine($"      only the upper half of its border is solid --"
            + $" the catch zone is occluded away: offered {ghost.Width > 0f}");
        Check("a window whose catch zone is missing is not offered as a dash target",
            ghost.Width == 0f);

        Console.WriteLine();
        Console.WriteLine("  A high window beside the screen edge, reached by leaping across");
        var assistWall = new Solid { Id = new IntPtr(1), L = -260f, T = -2000f, R = -200f, B = 40f };
        var highWin = new Solid { Id = new IntPtr(9), L = -160f, T = -200f, R = -40f, B = -120f };
        var leaperUp = OnFloor(0f, assistWall, highWin);
        var viaDirector = new IdleDirector(new Random(7));
        viaDirector.ForceActivityForCheck(IdleDirector.Activity.ClimbWindow, default,
            new RectangleF(-160f, -200f, 120f, 80f));
        var viaCtx = Context(leaperUp);
        viaCtx.EdgesClimbable = true;
        viaCtx.EdgeLeft = -200f;
        viaCtx.EdgeRight = 400f;
        bool viaDashed = false;
        for (frames = 0; frames < (int)(35f / Dt); frames++)
        {
            Step(viaDirector, leaperUp, viaCtx);
            if (leaperUp.State == Player.StDash) viaDashed = true;
            if (leaperUp.onGround && Math.Abs(leaperUp.Pos.Y + 200f) <= 2f &&
                leaperUp.Pos.X > -158f && leaperUp.Pos.X < -42f) break;
        }
        Console.WriteLine($"      its bottom is 120 up, the edge wall 40 away: she is at"
            + $" {leaperUp.Pos.X:F0},{leaperUp.Pos.Y:F0} after {frames * Dt:F1}s"
            + $" (dashed: {viaDashed})");
        Check("she rides the edge up and leaps across onto the window",
            leaperUp.onGround && Math.Abs(leaperUp.Pos.Y + 200f) <= 2f);
        Check("without a single dash, so it works in every window mode", !viaDashed);

        return failed;
    }
}
