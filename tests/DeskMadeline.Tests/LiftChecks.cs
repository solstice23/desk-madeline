using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// Lift speed: what a moving floor hands to whoever is standing on it, and what she is allowed
// to do with it. Actor.LiftSpeed, Player.LiftBoost, and the rule at the top of NormalUpdate.
//
// The numbers are vanilla's. A jump is -105 and the lift is added whole; sideways it is capped
// at 250 either way and upwards at 130; downwards it is thrown away entirely, so a floor
// dropping out from under her never presses her jump down. A floor that rises out from under
// her launches her without a jump at all, which is the moon boost, and the last lift she had
// is still worth having for a sixth of a second after the floor stops -- long enough to cover
// the frames a window spends between whole pixels.
//
// The rig is the desktop loop in miniature, in the order PetWindow runs it: the platform moves,
// she is carried by the ride-along and settled by the push, and only then does she update.
static class LiftChecks
{
    const float Dt = 1f / 60f;
    const float Gravity = 900f * Dt;   // one frame of it, for the frames she is not held up

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    sealed class Rig
    {
        public Player Player;
        public Solid Platform;

        public Rig(float liftX = 0f, float liftY = 0f)
        {
            Platform = new Solid
            { Id = new IntPtr(1), L = -400f, T = 0f, R = 400f, B = 200f };
            Player = new Player
            {
                Solids = new List<Solid> { Platform },
                Waters = new List<Solid>(),
                MinX = -100000f,
                MaxX = 100000f,
                FreezeFramesEnabled = false,
                Dashes = 1,
                Pos = new PointF(0f, 0f),
            };
            for (int i = 0; i < 5; i++) Step(0f, 0f, new PetInput());
        }

        /// <summary>One frame: the platform moves that far, hands over that speed, she updates.</summary>
        public void Step(float dx, float dy, PetInput input)
        {
            Platform = new Solid
            {
                Id = Platform.Id,
                L = Platform.L + dx, T = Platform.T + dy,
                R = Platform.R + dx, B = Platform.B + dy,
                LiftX = dx / Dt, LiftY = dy / Dt,
            };
            Player.Solids = new List<Solid> { Platform };
            if (Player.GroundId == Platform.Id) Player.RideAlong(dx, dy);
            else Player.EndRide();
            Player.SweptInto(Platform, dx, dy);
            Player.Update(Dt, input);
        }

        /// <summary>Ride it for a while, then jump on the next frame.</summary>
        public float RideAndJump(float dx, float dy, int frames = 20, int coast = 0)
        {
            var carried = new PetInput();
            for (int i = 0; i < frames; i++) Step(dx, dy, carried);
            for (int i = 0; i < coast; i++) Step(0f, 0f, carried);
            Player.BufferJump();
            var jump = new PetInput { JumpPressed = true, JumpHeld = true };
            Step(0f, 0f, jump);
            return Player.Speed.Y;
        }
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("LIFT: a floor on the move, and the boost she takes off it");
        Console.WriteLine(new string('=', 74));

        Console.WriteLine();
        Console.WriteLine("  Jumping off it (Player.Jump: Speed.Y = -105, then += LiftBoost)");
        // A whole game pixel a frame is sixty a second, which is what a window bobbing in
        // whole pixels moves at while it moves at all.
        Check($"a floor rising at 60 makes a -165 jump ({new Rig().RideAndJump(0f, -1f):F1})",
            Math.Abs(new Rig().RideAndJump(0f, -1f) - -165f) < 0.01f);
        Check("one rising at 400 is capped at 130 of boost, so -235",
            Math.Abs(new Rig().RideAndJump(0f, -400f * Dt) - -235f) < 0.01f);
        Check("and one sinking gives the plain -105: downward lift is thrown away",
            Math.Abs(new Rig().RideAndJump(0f, 1f) - -105f) < 0.01f);

        var sideways = new Rig();
        sideways.RideAndJump(300f * Dt, 0f);
        Check($"sideways it is carried across at 250 at the most ({sideways.Player.Speed.X:F1})",
            Math.Abs(sideways.Player.Speed.X - 250f) < 0.01f);

        Console.WriteLine();
        Console.WriteLine("  The moon boost proper: a floor that rises out from under her");
        // NormalUpdate's first lines. No jump and no button: she was on the ground, she is not
        // now, and what carried her is what she leaves with.
        var stepped = new Rig();
        var walking = new PetInput { MoveX = 1, AimX = 1, FeatherX = 1 };
        for (int i = 0; i < 10; i++) stepped.Step(0f, -1f, walking);
        // Off the end of it, still rising: the platform is narrowed to leave her in the air.
        stepped.Platform = new Solid
        { Id = stepped.Platform.Id, L = -400f, T = stepped.Platform.T, R = -8f, B = 200f };
        stepped.Step(0f, -1f, walking);
        Check($"she leaves the ground already going up ({stepped.Player.Speed.Y:F1})",
            Math.Abs(stepped.Player.Speed.Y - (-60f + Gravity)) < 0.01f);

        Console.WriteLine();
        Console.WriteLine("  How long it is still worth having (Actor.LiftSpeedGraceTime 0.16)");
        // A sixth of a second is nine and a half frames, and the frame the lift arrived on
        // spends one of them, so eight frames of standing still on it still carry the boost
        // and the ninth does not. This is what covers the frames a window bobbing in whole
        // pixels spends between them, and the frame or two a slow poll costs.
        Check($"eight frames after the floor stopped, the jump still carries it" +
            $" ({new Rig().RideAndJump(0f, -1f, 20, 8):F1})",
            Math.Abs(new Rig().RideAndJump(0f, -1f, 20, 8) - -165f) < 0.01f);
        Check($"the ninth, it is gone and the jump is the plain one" +
            $" ({new Rig().RideAndJump(0f, -1f, 20, 9):F1})",
            Math.Abs(new Rig().RideAndJump(0f, -1f, 20, 9) - -105f) < 0.01f);

        Console.WriteLine();
        Console.WriteLine("  What else takes it");
        // NormalUpdate hands the boost to a dash as well as to a jump -- Speed += LiftBoost
        // and then StartDash -- and the dash keeps the faster of its own 240 and what she
        // already had that way. So a floor sweeping her along at more than a dash is worth
        // dashing off, and the 250 cap is what she leaves with.
        var dashing = new Rig();
        var idle = new PetInput();
        for (int i = 0; i < 20; i++) dashing.Step(300f * Dt, 0f, idle);
        dashing.Player.BufferDash();
        var dashInput = new PetInput { MoveX = 1, AimX = 1, FeatherX = 1, DashPressed = true };
        for (int i = 0; i < 3; i++)
        {
            dashInput.DashPressed = dashing.Player.HasDashBuffer;
            dashing.Step(0f, 0f, dashInput);
        }
        Check($"a dash off a floor on the move leaves at the boost rather than the dash's own" +
            $" 240 ({dashing.Player.Speed.X:F1})",
            dashing.Player.State == Player.StDash &&
            Math.Abs(dashing.Player.Speed.X - 250f) < 0.01f);

        // WallJump asks the wall behind her for its lift when she has none of her own.
        var climbing = new Player
        {
            Waters = new List<Solid>(),
            MinX = -100000f, MaxX = 100000f, FreezeFramesEnabled = false,
            Dashes = 1, Pos = new PointF(0f, -40f), Facing = 1,
        };
        var side = new Solid
        { Id = new IntPtr(2), L = 4f, T = -200f, R = 200f, B = 200f, LiftX = 0f, LiftY = -300f };
        climbing.Solids = new List<Solid> { side };
        climbing.Speed = new PointF(0f, 0f);
        var grab = new PetInput { GrabHeld = true, MoveX = 1, AimX = 1, FeatherX = 1 };
        for (int i = 0; i < 5; i++) climbing.Update(Dt, grab);
        bool onWall = climbing.State == Player.StClimb;
        climbing.BufferJump();
        var wallJump = new PetInput
        { GrabHeld = true, MoveX = -1, AimX = -1, FeatherX = -1, JumpPressed = true, JumpHeld = true };
        climbing.Update(Dt, wallJump);
        Check($"a wall jump takes the wall's own lift ({climbing.Speed.Y:F1}), climbing={onWall}",
            onWall && Math.Abs(climbing.Speed.Y - (-105f - 130f)) < 0.01f);

        return failed;
    }
}
