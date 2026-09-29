using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// Grab modes: Input.GrabCheck under Hold, Invert and Toggle, and the GrabbyIcon glove.
//
// The frame order is the part worth pinning. Celeste.Update runs Input.UpdateGrab after the
// scene, so the player reads the Toggle latch before a press flips it and answers one frame
// late. Each frame here does what PetWindow.SampleInput does: Check, then Update.
static class GrabModeChecks
{
    static int failed;
    const float Dt = 1f / 60f;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static bool Frame(GrabInput grab, bool held, bool pressed)
    {
        bool check = grab.Check(held);
        grab.Update(pressed);
        return check;
    }

    // Falling beside a wall to her right, facing it and pushing into it.
    static Player OnWallSide()
    {
        var p = new Player
        {
            Waters = new List<Solid>(),
            MinX = -100000f, MaxX = 100000f, FreezeFramesEnabled = false,
            Pos = new PointF(0f, -40f), Facing = 1,
        };
        p.Solids = new List<Solid> { new Solid { Id = new IntPtr(1), L = 4f, T = -200f, R = 200f, B = 200f } };
        return p;
    }

    static void Step(Player p, GrabInput grab, bool held, bool pressed)
        => p.Update(Dt, new PetInput { MoveX = 1, AimX = 1, FeatherX = 1,
            GrabHeld = Frame(grab, held, pressed) });

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("GRAB MODES: hold, invert, toggle, and the glove");
        Console.WriteLine(new string('=', 74));

        var grab = new GrabInput();
        Check("the default is Hold", grab.Mode == GrabModes.Hold);
        Check("Hold: held is a grab", Frame(grab, true, true) && Frame(grab, true, false));
        Check("Hold: a press latches nothing", !Frame(grab, false, false) && !grab.Toggled);

        grab.SetMode(GrabModes.Invert);
        Check("Invert: nothing held is a grab", Frame(grab, false, false));
        Check("Invert: held is letting go", !Frame(grab, true, true));

        grab.SetMode(GrabModes.Toggle);
        Check("Toggle: nothing held is nothing", !Frame(grab, false, false));
        Check("Toggle: the press frame still reads the old latch", !Frame(grab, true, true));
        Check("Toggle: the frame after, held or not, is a grab",
            Frame(grab, true, false) && Frame(grab, false, false));
        bool stillOn = true;
        for (int i = 0; i < 120; i++) stillOn &= Frame(grab, false, false);
        Check("Toggle: it stays on with the key up", stillOn);
        Check("Toggle: the next press reads on, and the frame after it off",
            Frame(grab, true, true) && !Frame(grab, false, false));

        Frame(grab, true, true);
        grab.SetMode(GrabModes.Toggle);
        Check("changing the mode resets the latch, as the options menu does", !grab.Toggled);
        Frame(grab, true, true);
        grab.Reset();
        Check("ResetGrab clears it (a new Player, on respawn)", !Frame(grab, false, false));
        grab.SetMode(GrabModes.Invert);
        grab.Update(true);
        Check("only Toggle ever latches", !grab.Toggled);

        Console.WriteLine("  On a wall");
        var inverted = new GrabInput();
        inverted.SetMode(GrabModes.Invert);
        var p = OnWallSide();
        for (int i = 0; i < 5; i++) Step(p, inverted, false, false);
        bool climbed = p.State == Player.StClimb;
        Step(p, inverted, true, true);
        Check($"Invert: she takes the wall with no key down, and lets go when it is pressed" +
            $" (climbed={climbed}, state={p.State})", climbed && p.State != Player.StClimb);

        var toggled = new GrabInput();
        toggled.SetMode(GrabModes.Toggle);
        p = OnWallSide();
        Step(p, toggled, true, true);
        bool notYet = p.State != Player.StClimb;
        Step(p, toggled, false, false);
        bool took = p.State == Player.StClimb;
        bool held = true;
        for (int i = 0; i < 60; i++)
        {
            Step(p, toggled, false, false);
            held &= p.State == Player.StClimb;
        }
        Check($"Toggle: one tap, and she takes the wall a frame later and keeps it with the key up" +
            $" (notYet={notYet}, took={took}, held={held})", notYet && took && held);
        Step(p, toggled, true, true);
        bool stillClimbing = p.State == Player.StClimb;
        Step(p, toggled, false, false);
        Check($"Toggle: the second tap lets go, a frame later (then={stillClimbing}, now={p.State})",
            stillClimbing && p.State != Player.StClimb);

        Console.WriteLine("  The glove");
        var icon = new GrabbyIcon();
        icon.Update(false, Dt);
        Check("hidden while the grab is off", !icon.Enabled);
        icon.Update(true, Dt);
        Check($"shown the frame it comes on, at Wiggler.Start's full 1.2 ({icon.Scale:F3})",
            icon.Enabled && Math.Abs(icon.Scale - 1.2f) < 1e-6f);
        icon.Update(true, Dt);
        float expected = 1f + (float)Math.Cos(Math.PI * 2 * 0.3 * Dt) * (1f - Dt / 0.1f) * 0.2f;
        Check($"then Cos(sine)·counter of Wiggler(0.1, 0.3) ({icon.Scale:F4} vs {expected:F4})",
            Math.Abs(icon.Scale - expected) < 1e-4f);
        int frames = 1;
        while (icon.Scale != 1f && frames < 30) { icon.Update(true, Dt); frames++; }
        Check($"and settles in a tenth of a second ({frames} frames)", frames >= 6 && frames <= 7);
        icon.Update(false, Dt);
        Check("and goes when the grab does", !icon.Enabled);

        return failed;
    }
}
