using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

/// <summary>
/// What a super and a hyper sound like, at the input timings people actually use.
/// </summary>
/// <remarks>
/// Celeste's SuperJump (celeste_reference/Celeste/Player.cs:1815) plays
/// event:/char/madeline/jump and then jump_superslide when ducking, jump_super when not.
/// The dash's own sound comes from CallDashEvents, which DashCoroutine reaches only on the
/// dash's second frame -- so pressing jump with dash, or a frame after it, leaves through
/// SuperJump before that ever arrives. DashEnd calls it too, and that is what keeps the
/// dash audible.
/// </remarks>
static class SoundChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    static Player OnFloor() => new Player
    {
        Solids = new List<Solid>
        {
            new Solid { Id = new IntPtr(1), L = 0f, T = 200f, R = 600f, B = 260f },
        },
        MinX = -100000f,
        MaxX = 100000f,
        FreezeFramesEnabled = false,
        Dashes = 1,
        Pos = new PointF(100f, 200f),
        Facing = 1,
    };

    static void Case(string title, bool crouchDash, bool holdDown, int jumpDelayFrames, string expectedLaunch)
    {
        var p = OnFloor();
        var input = new PetInput { MoveX = 1, AimX = 1 };
        var heard = new List<string>();

        void Pump()
        {
            input.MoveY = holdDown ? 1 : 0;
            input.AimY = holdDown ? 1 : 0;
            input.JumpPressed = p.HasJumpBuffer;
            input.DashPressed = p.HasDashBuffer;
            p.Update(Dt, input);
            while (p.SoundEvents.Count > 0)
                heard.Add(p.SoundEvents.Dequeue().Path.Replace("event:/char/madeline/", ""));
        }

        for (int i = 0; i < 20; i++) Pump();       // settle on the floor, running
        heard.Clear();

        p.BufferDash(crouchDash);
        if (jumpDelayFrames == 0) p.BufferJump();  // both buttons on the same frame
        for (int i = 0; i < jumpDelayFrames; i++) Pump();
        if (jumpDelayFrames > 0) p.BufferJump();
        for (int i = 0; i < 14; i++) Pump();       // the super or hyper itself

        int dashes = heard.FindAll(s => s.StartsWith("dash_")).Count;
        bool jumped = heard.Contains("jump");
        bool launched = heard.Contains(expectedLaunch);
        bool ok = dashes == 1 && jumped && launched;
        if (!ok) failed++;

        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {title}");
        Console.WriteLine($"            heard: {string.Join(", ", heard)}");
        if (!ok)
            Console.WriteLine($"            wanted one dash_*, a jump and {expectedLaunch}; got " +
                              $"{dashes} dash sound(s), jump={jumped}, {expectedLaunch}={launched}");
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("SUPER AND HYPER SOUNDS");
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("  Each of these is a dash cut short by a jump, so each owes exactly one");
        Console.WriteLine("  dash sound, the jump, and its super or superslide layer.");
        Console.WriteLine();

        const string Super = "jump_super", Hyper = "jump_superslide";

        // Room to spare: the dash has run its course and sounded itself already.
        Case("super: dash, jump 6 frames later", false, false, 6, Super);
        Case("hyper: crouch dash, jump 6 frames later", true, false, 6, Hyper);
        Case("hyper: dash holding down, jump 6 frames later", false, true, 6, Hyper);

        // How it is really played, and where the dash used to go unheard: the jump lands
        // before DashCoroutine's second frame ever arrives.
        Case("super: dash + jump on the SAME frame", false, false, 0, Super);
        Case("super: jump 1 frame after dash", false, false, 1, Super);
        Case("super: jump 2 frames after dash", false, false, 2, Super);
        Case("hyper: down held, dash + jump on the SAME frame", false, true, 0, Hyper);
        Case("hyper: down held, jump 1 frame after dash", false, true, 1, Hyper);
        Case("hyper: crouch dash + jump on the SAME frame", true, false, 0, Hyper);

        return failed;
    }
}
