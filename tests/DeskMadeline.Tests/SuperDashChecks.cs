using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using DeskMadeline;

/// <summary>
/// Celeste's "Super Dashing" variant, which is five lines spread through
/// celeste_reference/Celeste/Player.cs and nothing else: DashBegin adds 0.15s to the
/// dash-attack window, DashCoroutine waits 0.3s instead of 0.15s and lays a longer trail,
/// and DashUpdate gains two powers -- steering the dash toward the aim while canCurveDash
/// holds, and spending another dash without leaving the state.
/// </summary>
static class SuperDashChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    /// <summary>Airborne, well above a wide floor, with the dashes she is given.</summary>
    static Player Air(bool superDashing, float height = 600f, int dashes = 1)
        => new Player
        {
            Solids = new List<Solid>
            { new Solid { Id = new IntPtr(1), L = -8000f, T = 0f, R = 8000f, B = 200f } },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            SuperDashing = superDashing,
            Dashes = dashes,
            DashMode = dashes,
            Facing = 1,
            Pos = new PointF(0f, -height),
        };

    static object Private(Player p, string name) => typeof(Player)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(p);

    static float Length(PointF p) => (float)Math.Sqrt(p.X * p.X + p.Y * p.Y);
    static float Degrees(PointF p) => (float)(Math.Atan2(p.Y, p.X) * 180.0 / Math.PI);
    static bool Near(float a, float b, float tolerance) => Math.Abs(a - b) <= tolerance;

    /// <summary>Warms up, dashes, and hands back the player on the frame DashBegin ran.</summary>
    static Player Dashing(bool superDashing, PetInput input, float height = 600f, int dashes = 1)
    {
        var p = Air(superDashing, height, dashes);
        for (int i = 0; i < 5; i++) p.Update(Dt, input);
        p.BufferDash();
        p.Update(Dt, input);
        return p;
    }

    /// <summary>How many frames the dash state lasts, start to finish.</summary>
    static int DashStateFrames(bool superDashing)
    {
        var input = new PetInput { AimX = 1 };
        var p = Dashing(superDashing, input);
        int frames = 1;
        for (int i = 0; i < 200 && p.State == Player.StDash; i++)
        {
            p.Update(Dt, input);
            if (p.State == Player.StDash) frames++;
        }
        return frames;
    }

    /// <summary>The dash-attack window as DashBegin leaves it.</summary>
    static float DashAttackWindow(bool superDashing)
        => (float)Private(Dashing(superDashing, new PetInput { AimX = 1 }), "dashAttackTimer");

    /// <summary>DashCoroutine's wait, as DashBegin leaves it.</summary>
    static float DashWait(bool superDashing)
        => (float)Private(Dashing(superDashing, new PetInput { AimX = 1 }), "dashTime");

    /// <summary>
    /// Dashes right, then asks for a different aim from the second frame on -- the first
    /// belongs to the aim the dash is still being pointed with. Returns the heading of every
    /// frame after that, in degrees.
    /// </summary>
    static List<float> Curve(bool superDashing, int aimX, int aimY, int frames)
    {
        var input = new PetInput { AimX = 1 };
        var p = Dashing(superDashing, input);
        p.Update(Dt, input);                  // the aim lands: 240 to the right
        input.AimX = aimX;
        input.AimY = aimY;
        var headings = new List<float>();
        for (int i = 0; i < frames; i++)
        {
            p.Update(Dt, input);
            headings.Add(Degrees(p.DashDir));
        }
        return headings;
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("SUPER DASHING: the variant, and the ordinary dash it must leave alone");
        Console.WriteLine(new string('=', 74));

        Console.WriteLine();
        Console.WriteLine("  How long the dash lasts");
        float plainWait = DashWait(false), superWait = DashWait(true);
        int plainFrames = DashStateFrames(false);
        int superFrames = DashStateFrames(true);
        Console.WriteLine($"      DashCoroutine waits {plainWait:F3}s plain, {superWait:F3}s super");
        Console.WriteLine($"      dash state: {plainFrames} frames plain, {superFrames} super");
        // The two waits DashCoroutine yields, and nothing between them.
        Check("DashCoroutine still waits 0.15s", plainWait == 0.15f);
        Check("the variant waits 0.3s instead", superWait == 0.3f);
        // Both spans are two frames -- DashBegin's own, then the coroutine's yield return
        // null -- plus however many DashUpdate spends draining the wait. 0.3f takes exactly
        // eighteen 60Hz frames to reach zero; 0.15f takes nine and a residue of 9e-9, which
        // buys a tenth, and that residue is why the Super/Hyper window is the documented 12
        // frames rather than 11.
        Check("the plain dash is still the 12-frame Super/Hyper window", plainFrames == 12);
        Check("and the variant runs 2 + 18 frames of a 0.3s wait", superFrames == 20);
        Check("which is the 0.15s longer dash, to the frame",
            Near((superFrames - plainFrames) * Dt, 0.15f, Dt));

        Console.WriteLine();
        Console.WriteLine("  The dash-attack window");
        float plainAttack = DashAttackWindow(false);
        float superAttack = DashAttackWindow(true);
        Console.WriteLine($"      dashAttackTimer at DashBegin: {plainAttack:F3}s plain," +
                          $" {superAttack:F3}s super");
        Check("DashBegin still opens 0.3s of dash attack", Near(plainAttack, 0.3f, 0.0005f));
        Check("the variant adds its 0.15s", Near(superAttack, 0.45f, 0.0005f));

        Console.WriteLine();
        Console.WriteLine("  Steering the dash");
        // 240 deg/s is 4 deg a frame, and the turn stops once the aim is within
        // acos(0.99) = 8.11 deg -- so a dash right, aimed up-right, settles 5 deg short.
        var upRight = Curve(true, 1, -1, 16);
        Console.WriteLine("      heading: " + string.Join(", ",
            upRight.ConvertAll(d => d.ToString("F1"))));
        bool rate = true;
        for (int i = 0; i < 10; i++) rate &= Near(upRight[i], -4f * (i + 1), 0.05f);
        Check("she turns 4 degrees a frame (240 deg/s)", rate);
        bool settled = true;
        for (int i = 10; i < upRight.Count; i++) settled &= Near(upRight[i], -40f, 0.05f);
        Check("and stops 5 degrees short, inside the 8.11 the dot product allows", settled);

        var speedy = new PetInput { AimX = 1 };
        var keeper = Dashing(true, speedy);
        keeper.Update(Dt, speedy);
        speedy.AimY = -1;
        bool sameSpeed = true;
        for (int i = 0; i < 12; i++)
        {
            keeper.Update(Dt, speedy);
            sameSpeed &= Near(Length(keeper.Speed), 240f, 0.05f);
        }
        Check("turning never costs or adds speed", sameSpeed);

        var back = Curve(true, -1, 0, 8);
        Check("an aim behind her (dot -1, under the -0.1 floor) turns nothing",
            back.TrueForAll(d => Near(d, 0f, 0.0001f)));

        var straight = Curve(false, 1, -1, 8);
        Check("and with the variant off the dash flies straight",
            straight.TrueForAll(d => Near(d, 0f, 0.0001f)));

        Console.WriteLine();
        Console.WriteLine("  Hitting something ends the steering");
        // A down-diagonal dash onto the floor: OnCollideV clears canCurveDash before it does
        // anything else, so the aim she is still holding no longer moves the dash.
        var landing = new PetInput { AimX = 1, AimY = 1 };
        var lander = Dashing(true, landing, height: 30f);
        lander.Update(Dt, landing);                     // the aim lands: 170 down-right
        landing.AimX = -1;                              // and now she steers it down-left
        bool curvedBeforeLanding = false, curvedAfterLanding = false;
        bool clearedOnContact = false;
        float headingAtContact = 0f;
        for (int i = 0; i < 30 && lander.State == Player.StDash; i++)
        {
            float before = Degrees(lander.DashDir);
            lander.Update(Dt, landing);
            float after = Degrees(lander.DashDir);
            if (!(bool)Private(lander, "canCurveDash"))
            {
                if (!clearedOnContact) { clearedOnContact = true; headingAtContact = after; }
                else if (!Near(after, headingAtContact, 0.0001f)) curvedAfterLanding = true;
            }
            else if (!Near(before, after, 0.0001f)) curvedBeforeLanding = true;
        }
        Console.WriteLine($"      canCurveDash cleared on contact: {clearedOnContact}," +
                          $" heading held at {headingAtContact:F1} deg");
        Check("she was steering on the way down", curvedBeforeLanding);
        Check("the landing clears canCurveDash", clearedOnContact);
        Check("and the dash holds its heading from there", !curvedAfterLanding);

        Console.WriteLine();
        Console.WriteLine("  Dashing again without leaving the dash");
        var again = new PetInput { AimX = 1 };
        var redash = Dashing(true, again, dashes: 2);
        int redashFrame = -1, stateAtRedash = -1;
        PointF speedOnRedashFrame = PointF.Empty, speedAfter = PointF.Empty;
        for (int i = 1; i <= 40 && redash.State == Player.StDash; i++)
        {
            if (i >= 10 && redash.DashSequenceCount == 1) redash.BufferDash();
            if (i >= 10) { again.AimX = 0; again.AimY = -1; }   // straight up, this time
            redash.Update(Dt, again);
            if (redashFrame < 0 && redash.DashSequenceCount == 2)
            {
                redashFrame = i;
                stateAtRedash = redash.State;
                speedOnRedashFrame = redash.Speed;
                redash.Update(Dt, again);
                speedAfter = redash.Speed;
            }
        }
        int heardDashes = 0;
        while (redash.SoundEvents.Count > 0)
            if (redash.SoundEvents.Dequeue().Path.Contains("/dash_")) heardDashes++;
        Console.WriteLine($"      re-dashed on dash frame {redashFrame}, dashes left" +
                          $" {redash.Dashes}, speed {speedOnRedashFrame.X:F0}," +
                          $"{speedOnRedashFrame.Y:F0} then {speedAfter.X:F0},{speedAfter.Y:F0}");
        Check("the dash restarts without passing through Normal",
            redashFrame > 0 && stateAtRedash == Player.StDash);
        // dashCooldownTimer is 0.2s, so the earliest re-dash is the 13th frame of the dash.
        Check("and not before the 0.2s dash cooldown is up", redashFrame >= 13);
        Check("it spends the second dash", redash.Dashes == 0);
        Check("the restarted coroutine gives up a frame first",
            speedOnRedashFrame == PointF.Empty);
        Check("and reads the aim on the next one",
            Near(speedAfter.X, 0f, 0.05f) && Near(speedAfter.Y, -240f, 0.05f));
        Check("two dashes, two dash sounds", heardDashes == 2);

        var patient = new PetInput { AimX = 1 };
        var plain = Dashing(false, patient, dashes: 2);
        bool restartedWithoutTheVariant = false;
        for (int i = 1; i <= 40 && plain.State == Player.StDash; i++)
        {
            if (i >= 10) plain.BufferDash();
            plain.Update(Dt, patient);
            if (plain.State == Player.StDash && plain.DashSequenceCount > 1)
                restartedWithoutTheVariant = true;
        }
        Check("with the variant off a mid-dash press waits for Normal",
            !restartedWithoutTheVariant);

        return failed;
    }
}
