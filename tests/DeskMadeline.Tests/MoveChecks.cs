using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// The move library: each primitive is one named Celeste move, rehearsed here on a ghost
// against the real physics. If a recipe drifts from what the port actually does, this is
// where it shows, not on the user's desk. The ghost itself is checked first: a rehearsal
// must not make a sound, move a window, or disturb the live player in any way.
static class MoveChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static Player OnFloor(float x, params Solid[] extra)
    {
        var solids = new List<Solid>
        { new Solid { Id = new IntPtr(1), L = -600f, T = 0f, R = 600f, B = 40f } };
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

    static List<Move> Plan(params Move[] moves) => new List<Move>(moves);

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("THE MOVE LIBRARY: rehearsed on a ghost, judged by the real physics");
        Console.WriteLine(new string('=', 74));

        Console.WriteLine("  The ghost cannot touch anything");
        var haunted = OnFloor(0f);
        bool callbackFired = false;
        haunted.OnDashCollide = (_, __) => { callbackFired = true; return DashCollisionResults.NormalCollision; };
        PointF before = haunted.Pos;
        int soundsBefore = haunted.SoundEvents.Count;
        IdleMoves.Rehearse(haunted,
            Plan(IdleMoves.Of(MoveKind.Super, dir: 1, at: 4)),
            p => p.Pos.X > 100f, 400, out PointF ghostEnd, out _, out _);
        Check("the live player did not move", haunted.Pos == before);
        Check("the rehearsal made no sound", haunted.SoundEvents.Count == soundsBefore);
        Check("a ghost dash never reaches a dash-collide callback", !callbackFired);
        Check("and yet the ghost itself went somewhere", ghostEnd.X > 60f);

        Console.WriteLine();
        Console.WriteLine("  Super and hyper");
        var runner = OnFloor(0f);
        IdleMoves.Rehearse(runner, Plan(IdleMoves.Of(MoveKind.Super, dir: 1, at: 6)),
            p => p.onGround, 400, out PointF superEnd, out float superPeak, out int superFrames);
        Console.WriteLine($"      super: landed at x={superEnd.X:F0} (peak {superPeak:F0})"
            + $" in {superFrames} frames");
        Check("a super carries her a long flat way", superEnd.X > 110f && superEnd.Y >= -2f);
        var slider = OnFloor(0f);
        IdleMoves.Rehearse(slider, Plan(IdleMoves.Of(MoveKind.Hyper, dir: 1, at: 6)),
            p => p.onGround, 400, out PointF hyperEnd, out float hyperPeak, out _);
        Console.WriteLine($"      hyper: landed at x={hyperEnd.X:F0} (peak {hyperPeak:F0})");
        Check("a hyper flies lower than a super", hyperPeak > superPeak + 4f);
        Check("and still travels far", hyperEnd.X > 110f);

        Console.WriteLine();
        Console.WriteLine("  Wavedash");
        var waver = OnFloor(0f);
        IdleMoves.Rehearse(waver, Plan(IdleMoves.Of(MoveKind.Wavedash, dir: 1)),
            p => p.onGround && p.Pos.X > 120f, 400, out PointF waveEnd, out _, out int waveFrames);
        Console.WriteLine($"      wavedash: at x={waveEnd.X:F0} after {waveFrames} frames");
        Check("the landing jump keeps the dash speed", waveEnd.X > 120f && waveFrames < 130);

        Console.WriteLine();
        Console.WriteLine("  Up-dash grab onto a hanging face");
        var floatBox = new Solid { Id = new IntPtr(9), L = 60f, T = -150f, R = 200f, B = -80f };
        var dasher = OnFloor(52f, floatBox);
        bool caught = IdleMoves.Rehearse(dasher,
            Plan(IdleMoves.Of(MoveKind.UpDashGrab, dir: 1, at: 14)),
            p => p.State == Player.StClimb, 200, out PointF dashEnd, out _, out _);
        Console.WriteLine($"      from under a bottom 80 up: caught {caught} at"
            + $" {dashEnd.X:F0},{dashEnd.Y:F0}");
        Check("she jumps, dashes up, and catches the face", caught);

        Console.WriteLine();
        Console.WriteLine("  Ultra");
        var sprinter = OnFloor(-500f);
        bool ultraRan = IdleMoves.Rehearse(sprinter,
            Plan(IdleMoves.Of(MoveKind.Ultra, dir: 1)),
            p => p.Pos.X > -260f, 400,
            out PointF ultraEnd, out _, out int ultraFrames);
        Console.WriteLine($"      the chain carried her to x={ultraEnd.X:F0}"
            + $" ({ultraEnd.X + 500f:F0}px) in {ultraFrames} frames");
        Check("boosted landings chain into real distance",
            ultraRan && ultraEnd.X + 500f > 240f);

        Console.WriteLine();
        Console.WriteLine("  Diagonal dash grab");
        var cornerBox = new Solid { Id = new IntPtr(9), L = 120f, T = -140f, R = 260f, B = -52f };
        var cutter = OnFloor(76f, cornerBox);
        bool cornered = IdleMoves.Rehearse(cutter,
            Plan(IdleMoves.Of(MoveKind.DiagDashGrab, dir: 1, at: 12)),
            p => p.State == Player.StClimb, 200, out PointF cutEnd, out _, out _);
        Console.WriteLine($"      a face both up and across: caught {cornered} at"
            + $" {cutEnd.X:F0},{cutEnd.Y:F0}");
        Check("the up-diagonal cuts the corner onto the face", cornered);

        Console.WriteLine();
        Console.WriteLine("  Wallbounce");
        var tallWall = new Solid { Id = new IntPtr(9), L = 100f, T = -400f, R = 140f, B = 40f };
        var bouncer = OnFloor(93f, tallWall);
        IdleMoves.Rehearse(bouncer,
            Plan(IdleMoves.Of(MoveKind.Wallbounce, dir: 1, at: 15)),
            p => true, 200, out _, out float bouncePeak, out _);
        Console.WriteLine($"      beside a tall wall: peak {bouncePeak:F0}"
            + $" (a plain jump peaks near -28)");
        Check("the super wall jump flies far higher than any jump", bouncePeak < -90f);

        Console.WriteLine();
        Console.WriteLine("  Climb, neutral hop, chimney kick");
        var pillar = new Solid { Id = new IntPtr(9), L = 60f, T = -400f, R = 100f, B = 40f };
        var grabber = OnFloor(20f, pillar);
        bool onWall = IdleMoves.Rehearse(grabber,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 50f),
                 IdleMoves.Of(MoveKind.RunningJump, dir: 1, hold: 10, grab: true),
                 IdleMoves.Of(MoveKind.ClimbUp, dir: 1, hold: 40)),
            p => p.State == Player.StClimb && p.Pos.Y < -40f, 400,
            out PointF climbEnd, out _, out _);
        Console.WriteLine($"      walk, jump-grab, climb: on the wall at"
            + $" {climbEnd.X:F0},{climbEnd.Y:F0}");
        Check("the jump-grab-climb chain puts her on the wall", onWall);
        bool hopped = IdleMoves.Rehearse(grabber,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 50f),
                 IdleMoves.Of(MoveKind.RunningJump, dir: 1, hold: 10, grab: true),
                 IdleMoves.Of(MoveKind.ClimbUp, dir: 1, hold: 40),
                 IdleMoves.Of(MoveKind.NeutralHop, dir: 1)),
            p => p.State == Player.StClimb && p.Pos.Y < climbEnd.Y - 6f, 500,
            out PointF hopEnd, out _, out _);
        Console.WriteLine($"      one neutral hop later she holds the wall at y={hopEnd.Y:F0}");
        Check("a neutral hop regains the wall higher for free", hopped);
        var flueL = new Solid { Id = new IntPtr(9), L = 0f, T = -400f, R = 20f, B = 40f };
        var flueR = new Solid { Id = new IntPtr(9), L = 50f, T = -400f, R = 70f, B = 40f };
        var kicker = OnFloor(35f, flueL, flueR);
        bool crossed = IdleMoves.Rehearse(kicker,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 44f),
                 IdleMoves.Of(MoveKind.RunningJump, dir: 1, hold: 10, grab: true),
                 IdleMoves.Of(MoveKind.ClimbUp, dir: 1, hold: 40),
                 IdleMoves.Of(MoveKind.ChimneyKick, dir: -1)),
            p => p.State == Player.StClimb && p.Pos.X < 33f, 500,
            out PointF kickEnd, out _, out _);
        Console.WriteLine($"      a kick across a thirty-pixel chimney lands her at"
            + $" {kickEnd.X:F0},{kickEnd.Y:F0}");
        Check("the chimney kick takes the far wall", crossed);

        Console.WriteLine();
        Console.WriteLine("  The wall ladder");
        var spire = new Solid { Id = new IntPtr(9), L = 60f, T = -400f, R = 100f, B = 40f };
        var rung = OnFloor(20f, spire);
        bool laddered = IdleMoves.Rehearse(rung,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 48f),
                 IdleMoves.Of(MoveKind.RunningJump, dir: 1, hold: 10, grab: true),
                 IdleMoves.Of(MoveKind.WallLadder, dir: 1, x: -394f)),
            p => p.onGround && Math.Abs(p.Pos.Y + 400f) <= 6f, 2600,
            out PointF rungEnd, out _, out int rungFrames);
        Console.WriteLine($"      four hundred pixels on one move: ended"
            + $" {rungEnd.X:F0},{rungEnd.Y:F0} in {rungFrames} frames");
        Check("climb while the tank lasts, wall-jump the rest, land over the lip", laddered);

        var hangTop = new Solid { Id = new IntPtr(9), L = 60f, T = -274f, R = 147f, B = -273f };
        var hangWall = new Solid { Id = new IntPtr(9), L = 146f, T = -272f, R = 147f, B = -27f };
        var hanger = OnFloor(203f, hangTop, hangWall);
        bool hung = IdleMoves.Rehearse(hanger,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 152f),
                 IdleMoves.Of(MoveKind.WallLadder, dir: -1, x: -280f)),
            p => p.onGround && Math.Abs(p.Pos.Y + 274f) <= 6f, 2600,
            out PointF hangEnd, out _, out int hangFrames);
        Console.WriteLine($"      a face hanging 27px overhead: ended"
            + $" {hangEnd.X:F0},{hangEnd.Y:F0} in {hangFrames} frames");
        Check("the vertical hop catches a hanging face the running grab flies under",
            hung);

        Console.WriteLine();
        Console.WriteLine("  Chimney kicks in the ladder");
        var chimA = new Solid { Id = new IntPtr(9), L = 40f, T = -300f, R = 62f, B = 40f };
        var chimB = new Solid { Id = new IntPtr(9), L = 102f, T = -300f, R = 124f, B = 40f };
        var crosser = OnFloor(82f, chimA, chimB);
        bool roomy = IdleMoves.Rehearse(crosser,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 94f),
                 IdleMoves.Of(MoveKind.RunningJump, dir: 1, hold: 10, grab: true),
                 IdleMoves.Of(MoveKind.WallLadder, dir: 1, x: -294f, grab: true)),
            p => p.onGround && Math.Abs(p.Pos.Y + 300f) <= 6f, 2600,
            out PointF roomyEnd, out _, out int roomyFrames);
        Console.WriteLine($"      a forty-pixel chimney, kick style: ended"
            + $" {roomyEnd.X:F0},{roomyEnd.Y:F0} in {roomyFrames} frames");
        Check("the kick-styled ladder crosses a roomy chimney to the top", roomy);

        var chimC = new Solid { Id = new IntPtr(9), L = 40f, T = -300f, R = 62f, B = 40f };
        var chimD = new Solid { Id = new IntPtr(9), L = 76f, T = -300f, R = 98f, B = 40f };
        var wedged = OnFloor(69f, chimC, chimD);
        bool cramped = IdleMoves.Rehearse(wedged,
            Plan(IdleMoves.Of(MoveKind.WallLadder, dir: 1, x: -294f)),
            p => p.onGround && Math.Abs(p.Pos.Y + 300f) <= 6f, 2600,
            out PointF crampEnd, out _, out int crampFrames);
        Console.WriteLine($"      a fourteen-pixel chimney, plain ladder: ended"
            + $" {crampEnd.X:F0},{crampEnd.Y:F0} in {crampFrames} frames");
        Check("a chimney too tight for the neutral forces kicks even unstyled", cramped);

        Console.WriteLine();
        Console.WriteLine("  Both doors");
        var doorBox = new Solid { Id = new IntPtr(9), L = -60f, T = -140f, R = 60f, B = -20f };
        var chooser = OnFloor(150f, doorBox);
        var doorCtx = new IdleContext
        {
            Player = chooser,
            Solids = chooser.Solids,
            Monitors = new List<RectangleF> { new RectangleF(-400f, -500f, 800f, 540f) },
            Cursor = new PointF(2000f, 2000f),
            Gliders = new List<Glider>(),
            Seekers = new List<Seeker>(),
            Puffers = new List<Puffer>(),
            Windows = new List<KeyValuePair<IntPtr, RectangleF>>(),
        };
        var doorSegs = new List<NavSeg>();
        IdleNav.BuildSegs(doorCtx, doorSegs);
        int doorFrom = -1, doorTo = -1;
        for (int i = 0; i < doorSegs.Count; i++)
        {
            if (doorSegs[i].Y == 0f && doorSegs[i].L <= 150f && doorSegs[i].R >= 150f)
                doorFrom = i;
            if (doorSegs[i].Y == -140f) doorTo = i;
        }
        bool sawLeft = false, sawRight = false;
        for (int seed = 0; seed < 12; seed++)
        {
            var doorRoute = IdleNav.FindRoute(doorCtx, doorSegs, doorFrom, doorTo,
                new Random(seed));
            if (doorRoute == null || doorRoute.Count == 0) continue;
            if (doorRoute[0].Dir > 0) sawLeft = true;
            if (doorRoute[0].Dir < 0) sawRight = true;
        }
        Console.WriteLine($"      twelve rolls over a floater: left face {sawLeft},"
            + $" right face {sawRight}");
        Check("a window climbable from both sides gets climbed from both", sawLeft && sawRight);

        Console.WriteLine();
        Console.WriteLine("  A step across and a step down");
        var highTop = new Solid { Id = new IntPtr(9), L = -80f, T = -132f, R = 6f, B = -131f };
        var highWallL = new Solid { Id = new IntPtr(9), L = -80f, T = -131f, R = -79f, B = -20f };
        var lowTop = new Solid { Id = new IntPtr(9), L = 19f, T = -100f, R = 300f, B = -98f };
        var ledgeWalker = OnFloor(-30f, highTop, highWallL, lowTop);
        ledgeWalker.Pos = new PointF(-30f, -132f);
        for (int i = 0; i < 5; i++) ledgeWalker.Update(Dt, new PetInput());
        var stepCtx = new IdleContext
        {
            Player = ledgeWalker,
            Solids = ledgeWalker.Solids,
            Monitors = new List<RectangleF> { new RectangleF(-400f, -500f, 800f, 540f) },
            Cursor = new PointF(2000f, 2000f),
            Gliders = new List<Glider>(),
            Seekers = new List<Seeker>(),
            Puffers = new List<Puffer>(),
            Windows = new List<KeyValuePair<IntPtr, RectangleF>>(),
        };
        var stepSegs = new List<NavSeg>();
        IdleNav.BuildSegs(stepCtx, stepSegs);
        int stepFrom = -1, stepTo = -1;
        for (int i = 0; i < stepSegs.Count; i++)
        {
            if (stepSegs[i].Y == -132f) stepFrom = i;
            if (stepSegs[i].Y == -100f) stepTo = i;
        }
        var stepRoute = IdleNav.FindRoute(stepCtx, stepSegs, stepFrom, stepTo, new Random(3));
        Console.WriteLine($"      13 across, 32 down: route"
            + $" {(stepRoute == null ? "MISSING" : stepRoute.Count + " step(s)")}");
        Check("a top one step across and one step down is a neighbour, not an island",
            stepRoute != null);

        Console.WriteLine();
        Console.WriteLine("  The hanging assist wall");
        var hangAssist = new Solid { Id = new IntPtr(9), L = 60f, T = -315f, R = 62f, B = -69f };
        var besideTop = new Solid { Id = new IntPtr(9), L = 75f, T = -284f, R = 300f, B = -282f };
        // The neighbour is a hollow window, so it has a border of its own -- which is
        // what the dry-tank zigzag climbs once the boarding dash's stamina runs out.
        var besideWall = new Solid { Id = new IntPtr(9), L = 75f, T = -282f, R = 77f, B = -100f };
        var boarder = OnFloor(120f, hangAssist, besideTop, besideWall);
        bool boarded = IdleMoves.Rehearse(boarder,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 70f),
                 IdleMoves.Of(MoveKind.Settle),
                 IdleMoves.Of(MoveKind.UpDashGrab, dir: -1, at: 14),
                 IdleMoves.Of(MoveKind.WallLadder, dir: -1, x: -290f),
                 IdleMoves.Of(MoveKind.ChimneyKick, dir: 1),
                 IdleMoves.Of(MoveKind.WallLadder, dir: 1, x: -290f)),
            p => p.onGround && Math.Abs(p.Pos.Y + 284f) <= 6f, 2600,
            out PointF boardEnd, out _, out int boardFrames);
        Console.WriteLine($"      dash-board the hanging border, kick across: ended"
            + $" {boardEnd.X:F0},{boardEnd.Y:F0} in {boardFrames} frames");
        Check("a hanging border is boarded by dash and leapt from", boarded);

        Console.WriteLine();
        Console.WriteLine("  The dash across");
        var bigWall = new Solid { Id = new IntPtr(9), L = 200f, T = -350f, R = 600f, B = 0f };
        var farTop = new Solid { Id = new IntPtr(9), L = -140f, T = -262f, R = 86f, B = -260f };
        var farWall = new Solid { Id = new IntPtr(9), L = 84f, T = -260f, R = 86f, B = -95f };
        var crosser2 = OnFloor(150f, bigWall, farTop, farWall);
        bool spanned = IdleMoves.Rehearse(crosser2,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 190f),
                 IdleMoves.Of(MoveKind.RunningJump, dir: 1, hold: 10, grab: true),
                 IdleMoves.Of(MoveKind.WallLadder, dir: 1, x: -235f),
                 IdleMoves.Of(MoveKind.DashAcross, dir: -1, at: 8),
                 IdleMoves.Of(MoveKind.WallLadder, dir: -1, x: -268f)),
            p => p.onGround && Math.Abs(p.Pos.Y + 262f) <= 6f, 2600,
            out PointF crossEnd, out _, out int crossFrames);
        Console.WriteLine($"      climb the big wall, dash the 114px gap: ended"
            + $" {crossEnd.X:F0},{crossEnd.Y:F0} in {crossFrames} frames");
        Check("the wall-jump dash carries a gap far past the kick", spanned);

        var wideCtx = new IdleContext
        {
            Player = crosser2,
            Solids = crosser2.Solids,
            Monitors = new List<RectangleF> { new RectangleF(-400f, -500f, 1100f, 540f) },
            Cursor = new PointF(2000f, 2000f),
            Gliders = new List<Glider>(),
            Seekers = new List<Seeker>(),
            Puffers = new List<Puffer>(),
            Windows = new List<KeyValuePair<IntPtr, RectangleF>>(),
        };
        var wideSegs = new List<NavSeg>();
        IdleNav.BuildSegs(wideCtx, wideSegs);
        int wideFrom = -1, wideTo = -1;
        for (int i = 0; i < wideSegs.Count; i++)
        {
            if (wideSegs[i].Y == 0f && wideSegs[i].L <= 150f && wideSegs[i].R >= 150f)
                wideFrom = i;
            if (wideSegs[i].Y == -262f) wideTo = i;
        }
        var wideRoute = IdleNav.FindRoute(wideCtx, wideSegs, wideFrom, wideTo, new Random(2));
        Console.WriteLine($"      the graph offers it: route"
            + $" {(wideRoute == null ? "MISSING" : "found, kind " + wideRoute[0].Move)}");
        Check("the graph sees the wall a dash-length away as a way up", wideRoute != null);

        Console.WriteLine();
        Console.WriteLine("  Over the lip");
        var block = new Solid { Id = new IntPtr(9), L = 60f, T = -50f, R = 160f, B = 0f };
        var popper = OnFloor(20f, block);
        bool popped = IdleMoves.Rehearse(popper,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 50f),
                 IdleMoves.Of(MoveKind.RunningJump, dir: 1, hold: 10, grab: true),
                 IdleMoves.Of(MoveKind.ClimbOverLip, dir: 1)),
            p => p.onGround && Math.Abs(p.Pos.Y + 50f) <= 2f, 400,
            out PointF popEnd, out _, out _);
        Console.WriteLine($"      she stands on the top at {popEnd.X:F0},{popEnd.Y:F0}");
        Check("the climb pops over the lip onto the top", popped);

        Console.WriteLine();
        Console.WriteLine("  Drop off");
        var shelfTop = new Solid { Id = new IntPtr(9), L = -100f, T = -80f, R = 60f, B = -40f };
        var stepper = new Player
        {
            Solids = new List<Solid>
            {
                new Solid { Id = new IntPtr(1), L = -600f, T = 0f, R = 600f, B = 40f },
                shelfTop,
            },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(0f, -80f)
        };
        for (int i = 0; i < 5; i++) stepper.Update(Dt, new PetInput());
        bool dropped = IdleMoves.Rehearse(stepper,
            Plan(IdleMoves.Of(MoveKind.DropOff, dir: 1)),
            p => p.onGround && p.Pos.Y >= -2f, 300, out PointF dropEnd, out _, out _);
        Console.WriteLine($"      off the shelf to {dropEnd.X:F0},{dropEnd.Y:F0}");
        Check("she walks off the end and lands below", dropped);

        Console.WriteLine();
        Console.WriteLine("  The desk with two ways up (from a real probe)");
        var dFpTop = new Solid { Id = new IntPtr(2), L = 775f, T = 48f, R = 1059f, B = 50f };
        var dFpBot = new Solid { Id = new IntPtr(2), L = 775f, T = 227f, R = 1059f, B = 229f };
        var dFpL = new Solid { Id = new IntPtr(2), L = 775f, T = 50f, R = 776f, B = 227f };
        var dFpR = new Solid { Id = new IntPtr(2), L = 1057f, T = 50f, R = 1059f, B = 227f };
        var dStTop = new Solid { Id = new IntPtr(3), L = 676f, T = 16f, R = 762f, B = 17f };
        var dStBot = new Solid { Id = new IntPtr(3), L = 676f, T = 263f, R = 762f, B = 264f };
        var dStL = new Solid { Id = new IntPtr(3), L = 676f, T = 17f, R = 677f, B = 263f };
        var dStR = new Solid { Id = new IntPtr(3), L = 761f, T = 17f, R = 762f, B = 263f };
        var dFloor = new Solid { Id = new IntPtr(1), L = 533f, T = 332f, R = 1173f, B = 732f };
        var dEdgeR = new Solid { Id = new IntPtr(1), L = 1173f, T = -28f, R = 1573f, B = 332f };
        var dEdgeT = new Solid { Id = new IntPtr(1), L = 533f, T = -428f, R = 1173f, B = -28f };
        var desk = new Player
        {
            Solids = new List<Solid> { dFpTop, dFpBot, dFpL, dFpR, dStTop, dStBot, dStL,
                dStR, dFloor, dEdgeR, dEdgeT },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(900f, 332f)
        };
        for (int i = 0; i < 5; i++) desk.Update(Dt, new PetInput());
        var deskCtx = new IdleContext
        {
            Player = desk,
            Solids = desk.Solids,
            Monitors = new List<RectangleF> { new RectangleF(533f, -28f, 640f, 360f) },
            Cursor = new PointF(3000f, 3000f),
            Gliders = new List<Glider>(),
            Seekers = new List<Seeker>(),
            Puffers = new List<Puffer>(),
            Windows = new List<KeyValuePair<IntPtr, RectangleF>>(),
        };
        var deskSegs = new List<NavSeg>();
        IdleNav.BuildSegs(deskCtx, deskSegs);
        int dFrom = -1, dSteam = -1, dFp = -1;
        for (int i = 0; i < deskSegs.Count; i++)
        {
            if (deskSegs[i].Y == 332f && deskSegs[i].L <= 900f && deskSegs[i].R >= 900f)
                dFrom = i;
            if (deskSegs[i].Y == 16f) dSteam = i;
            if (deskSegs[i].Y == 48f) dFp = i;
        }
        Console.WriteLine($"      the hanging window, from both sides:");
        bool stLeft = false, stRight = false;
        for (int seed = 0; seed < 16; seed++)
        {
            var r1 = IdleNav.FindRoute(deskCtx, deskSegs, dFrom, dSteam, new Random(seed));
            if (r1 == null || r1.Count == 0) continue;
            var last = r1[r1.Count - 1];
            if (last.Dir > 0) stLeft = true;
            if (last.Dir < 0) stRight = true;
        }
        Console.WriteLine($"        16 rolls: left-side door {stLeft}, right-side door {stRight}");
        Check("a hanging window is climbed from both its sides", stLeft && stRight);
        // The taller neighbour has two ways up: the leap that boards the hanging
        // window's border, and the screen edge a dash-length away. Both must come
        // up in the rolls -- a shadowed route is variety lost and resilience lost.
        bool viaSteam = false, viaScreenEdge = false;
        for (int seed = 0; seed < 24; seed++)
        {
            var rr = IdleNav.FindRoute(deskCtx, deskSegs, dFrom, dFp, new Random(seed));
            if (rr == null || rr.Count == 0) continue;
            if (rr[0].Dir > 0 && rr[0].X < 900f) viaSteam = true;
            if (rr[0].Dir < 0 && rr[0].X > 1100f) viaScreenEdge = true;
        }
        Console.WriteLine($"      the taller neighbour: via the hanging window {viaSteam},"
            + $" via the screen edge {viaScreenEdge}");
        Check("both ways up the taller neighbour come up in the rolls",
            viaSteam && viaScreenEdge);
        // And the screen-edge plan itself, performed.
        bool viaEdge = IdleMoves.Rehearse(desk,
            Plan(IdleMoves.Of(MoveKind.WalkTo, x: 1163f),
                 IdleMoves.Of(MoveKind.RunningJump, dir: 1, hold: 10, grab: true),
                 IdleMoves.Of(MoveKind.WallLadder, dir: 1, x: 75f),
                 IdleMoves.Of(MoveKind.DashAcross, dir: -1, at: 8),
                 IdleMoves.Of(MoveKind.WallLadder, dir: -1, x: 42f)),
            pp => pp.onGround && pp.Pos.Y <= 54f && pp.Pos.Y >= 38f, 2600,
            out PointF edgeEnd, out _, out int edgeFrames);
        Console.WriteLine($"        via the screen edge, performed: {viaEdge}"
            + $" end {edgeEnd.X:F0},{edgeEnd.Y:F0} ({edgeFrames}f)");
        Check("the screen-edge route is performable end to end", viaEdge);

        // The hop from the hanging window down onto the taller neighbour is ultra
        // terrain: down the diagonal, landing mid-dash for the boost. The physics
        // first, demanding a REAL boost -- then the planner, which must offer it.
        var diver = new Player
        {
            Solids = desk.Solids,
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(700f, 16f)
        };
        for (int i = 0; i < 5; i++) diver.Update(Dt, new PetInput());
        bool dove = IdleMoves.Rehearse(diver,
            Plan(IdleMoves.Of(MoveKind.Ultra, dir: 1, grab: true)),
            pp => pp.onGround && pp.Pos.Y <= 54f && pp.Pos.Y >= 38f &&
                pp.WavedashCount >= 1, 400,
            out PointF diveEnd, out _, out int diveFrames);
        Console.WriteLine($"      the ultra off the ledge: {dove} end"
            + $" {diveEnd.X:F0},{diveEnd.Y:F0} ({diveFrames}f)");
        Check("the drop to the neighbour lands boosted -- a real ultra", dove);
        int steamTopSeg = -1, fpSeg2 = -1;
        for (int i = 0; i < deskSegs.Count; i++)
        {
            if (deskSegs[i].Y == 16f) steamTopSeg = i;
            if (deskSegs[i].Y == 48f) fpSeg2 = i;
        }
        var dropRoute = IdleNav.FindRoute(deskCtx, deskSegs, steamTopSeg, fpSeg2, new Random(4));
        int ultras = 0, drops = 0;
        if (dropRoute != null && dropRoute.Count > 0)
        {
            var diverCtx = deskCtx;
            diverCtx.Player = diver;
            for (int seed = 0; seed < 24; seed++)
            {
                var dPlan = IdlePlanner.PlanStep(diverCtx, deskSegs, dropRoute[0], new Random(seed));
                if (dPlan == null) continue;
                if (dPlan[0].Kind == MoveKind.Ultra) ultras++;
                else drops++;
            }
        }
        Console.WriteLine($"      24 planner rolls on that drop: {ultras} ultras,"
            + $" {drops} plain walk-offs");
        Check("the planner rolls the ultra on the drop, sometimes", ultras > 0 && drops > 0);

        Console.WriteLine();
        Console.WriteLine("  Dream country");
        var dreamBlock = new Solid
        { Id = new IntPtr(9), L = 60f, T = -60f, R = 200f, B = 40f, Dream = true };
        var dreamer = OnFloor(20f, dreamBlock);
        dreamer.BufferDash();
        bool through = false;
        for (int i = 0; i < 150 && !through; i++)
        {
            var di = new PetInput { MoveX = 1, AimX = 1, DashPressed = dreamer.HasDashBuffer };
            dreamer.Update(Dt, di);
            if (dreamer.IsDead) break;
            if (dreamer.Pos.X > 205f) through = true;
        }
        Console.WriteLine($"      dashed into the block at 60, now at"
            + $" {dreamer.Pos.X:F0},{dreamer.Pos.Y:F0}, dead {dreamer.IsDead}");
        Check("a dream block is a doorway: in one side, out the other, alive",
            through && !dreamer.IsDead);

        Console.WriteLine();
        Console.WriteLine("  The encore cools");
        var westBox = new Solid { Id = new IntPtr(10), L = -200f, T = -140f, R = -80f, B = -20f };
        var eastBox = new Solid { Id = new IntPtr(11), L = 80f, T = -140f, R = 200f, B = -20f };
        var chooser2 = OnFloor(0f, westBox, eastBox);
        var repDirector = new IdleDirector(new Random(9));
        var repCtx = new IdleContext
        {
            Player = chooser2,
            Solids = chooser2.Solids,
            Monitors = new List<RectangleF> { new RectangleF(-400f, -500f, 800f, 540f) },
            Cursor = new PointF(2000f, 2000f),
            Gliders = new List<Glider>(),
            Seekers = new List<Seeker>(),
            Puffers = new List<Puffer>(),
            Windows = new List<KeyValuePair<IntPtr, RectangleF>>
            {
                new KeyValuePair<IntPtr, RectangleF>(new IntPtr(10), new RectangleF(-200f, -140f, 120f, 120f)),
                new KeyValuePair<IntPtr, RectangleF>(new IntPtr(11), new RectangleF(80f, -140f, 120f, 120f)),
            },
        };
        int west1 = 0, east1 = 0;
        for (int i = 0; i < 30; i++)
        {
            var got = repDirector.ProbeClimbForCheck(repCtx);
            if (got.Left == -200f) west1++;
            else if (got.Left == 80f) east1++;
        }
        repDirector.NoteClimbedForCheck(new IntPtr(10));
        int west2 = 0, east2 = 0;
        for (int i = 0; i < 30; i++)
        {
            var got = repDirector.ProbeClimbForCheck(repCtx);
            if (got.Left == -200f) west2++;
            else if (got.Left == 80f) east2++;
        }
        Console.WriteLine($"      before the note: west {west1} east {east1};"
            + $" after climbing west: west {west2} east {east2}");
        Check("a just-climbed window mostly yields to the rest of the desk",
            west2 <= west1 / 2 && east2 > east1);

        return failed;
    }
}
