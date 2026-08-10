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

        return failed;
    }
}
