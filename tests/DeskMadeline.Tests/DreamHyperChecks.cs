using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

/// <summary>The dream hyper, and the crouch that decides whether it is one.</summary>
/// <remarks>
/// Dash down-diagonally into a dream block, leave through one of its sides, and spend the
/// dash the exit refills plus the coyote time it grants on a dash and a jump together. It
/// lands as a hyper, or as a super, and nothing in the port picks between them: SuperJump
/// reads Ducking, and Ducking is decided one frame earlier by DashBegin --
///
///     if (!onGround &amp;&amp; Ducking &amp;&amp; CanUnDuck) Ducking = false;
///     else if (!Ducking &amp;&amp; (crouchDash || MoveY == 1)) Ducking = true;
///
/// an else-if, so a dash taken in mid-air while already crouched stands her up and cannot
/// crouch her again. Arriving at the block crouched therefore costs the hyper, which is what
/// Celeste players mean by "do not demo-dash into the block". Both halves are vanilla
/// (celeste_reference/Celeste/Player.cs:3559), and the point of pinning them here is that
/// the super looks like a bug from the outside -- it is easy to "fix" by forcing the crouch,
/// which would break Celeste's rule rather than follow it.
/// </remarks>
static class DreamHyperChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    /// <summary>
    /// Down-diagonal dash into the block from above, out through its right face, then dash
    /// and jump on the exit frame. Returns what she launched with.
    /// </summary>
    static (PointF Launch, bool Exited, bool SideExit, bool DuckedIntoDash) Attempt(bool crouchedOnExit)
    {
        var solids = new List<Solid>
        {
            new Solid { Id = new IntPtr(1), L = 100f, T = 200f, R = 300f, B = 400f, Dream = true },
            new Solid { Id = new IntPtr(2), L = 0f, T = 600f, R = 800f, B = 660f },
        };
        var p = new Player
        {
            Solids = solids,
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Facing = 1,
            Pos = new PointF(140f, 170f),   // above the block, dashing down into it
        };
        var input = new PetInput { MoveX = 1, MoveY = 1, AimX = 1, AimY = 1 };
        p.BufferDash();

        int exitFrame = -1;
        PointF exitPos = PointF.Empty, launch = PointF.Empty;
        bool duckedIntoDash = false, sawDash = false;
        int launches = 0;

        for (int i = 0; i < 70; i++)
        {
            input.JumpPressed = p.HasJumpBuffer;
            input.DashPressed = p.HasDashBuffer;

            int stateBefore = p.State, launchesBefore = p.LaunchCount;
            p.Update(Dt, input);

            if (stateBefore == Player.StDreamDash && p.State != Player.StDreamDash)
            {
                exitFrame = i;
                exitPos = p.Pos;
                // Standing in for having arrived at the block already crouched, which is what
                // a demo dash into it would leave her as. Set here so the geometry above can
                // stay one shape for both cases.
                if (crouchedOnExit) p.Ducking = true;
                p.BufferDash();   // the hyper's dash, on the exit frame
            }
            if (!sawDash && stateBefore != Player.StDash && p.State == Player.StDash && exitFrame >= 0)
            {
                sawDash = true;
                duckedIntoDash = p.Ducking;   // what DashBegin settled on
            }
            if (exitFrame >= 0 && i == exitFrame + 1) p.BufferJump();
            if (p.LaunchCount != launchesBefore) { launches++; launch = p.Speed; }
        }

        bool sideExit = exitFrame >= 0 && exitPos.X > 300f;
        return (launches > 0 ? launch : PointF.Empty, exitFrame >= 0, sideExit, duckedIntoDash);
    }

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("DREAM HYPER (hyper is 325 / -52.5; super is 260 / -105)");
        Console.WriteLine(new string('=', 74));

        var upright = Attempt(crouchedOnExit: false);
        Console.WriteLine($"    uncrouched out of the block: launch ({upright.Launch.X:0.0}, {upright.Launch.Y:0.0}), " +
                          $"crouched into the dash={upright.DuckedIntoDash}");
        Check("she dream dashes and leaves through a side face", upright.Exited && upright.SideExit);
        Check("uncrouched, the dash crouches her", upright.DuckedIntoDash);
        Check("uncrouched, it lands as a hyper (325 / -52.5)",
            Math.Abs(upright.Launch.X - 325f) < 1f && Math.Abs(upright.Launch.Y - -52.5f) < 1f);

        var crouched = Attempt(crouchedOnExit: true);
        Console.WriteLine($"    crouched out of the block:   launch ({crouched.Launch.X:0.0}, {crouched.Launch.Y:0.0}), " +
                          $"crouched into the dash={crouched.DuckedIntoDash}");
        Check("crouched, the mid-air dash stands her up instead", !crouched.DuckedIntoDash);
        Check("crouched, it lands as a super (260 / -105), the hyper being impossible",
            Math.Abs(crouched.Launch.X - 260f) < 1f && Math.Abs(crouched.Launch.Y - -105f) < 1f);

        return failed;
    }
}
