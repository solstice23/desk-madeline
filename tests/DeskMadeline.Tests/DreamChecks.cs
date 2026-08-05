using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// Where does the death effect appear when a dream dash ends in a solid?
//
// Vanilla (Celeste Player.DreamDashUpdate) calls Die(Vector2.Zero) with the player left
// exactly where NaiveMove put her, so the death effect is centred on the spot she died.
// The port adds SnapDreamDeathToExitFace() first. This measures the gap between the two.
static class DreamChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    /// <summary>Report a verdict and remember any failure.</summary>
    static string Verdict(bool ok, string good, string bad)
    {
        if (!ok) failed++;
        return ok ? good : bad;
    }

    static Solid Dream(float l, float t, float r, float b) =>
        new Solid { Id = new IntPtr(1), L = l, T = t, R = r, B = b, Dream = true };

    static Solid Wall(float l, float t, float r, float b) =>
        new Solid { Id = new IntPtr(2), L = l, T = t, R = r, B = b };

    /// <summary>Dash from `start` in `aim` and report where she dies vs where the effect lands.</summary>
    static void Case(string title, List<Solid> solids, PointF start, int aimX, int aimY)
    {
        var p = new Player
        {
            Solids = solids,
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,   // step frame by frame, no hit-stop
            Dashes = 1,
            Pos = start,
            Facing = aimX >= 0 ? 1 : -1,
        };

        var input = new PetInput { MoveX = aimX, MoveY = aimY, AimX = aimX, AimY = aimY };
        p.BufferDash();              // PetWindow.SampleInput buffers on the press edge

        PointF lastLive = p.Pos;     // her position on the last frame before she died
        bool everDreamed = false;
        for (int i = 0; i < 200 && !p.IsDead; i++)
        {
            // SampleInput re-derives these from the buffers every frame.
            input.JumpPressed = p.HasJumpBuffer;
            input.DashPressed = p.HasDashBuffer;
            p.Update(Dt, input);
            if (p.State == Player.StDreamDash) everDreamed = true;
            if (!p.IsDead) lastLive = p.Pos;
        }

        Console.WriteLine();
        Console.WriteLine("  " + title);
        if (!everDreamed) { Console.WriteLine("    never entered the dream block -- setup wrong"); failed++; return; }
        if (!p.IsDead) { Console.WriteLine("    never died -- setup wrong"); failed++; return; }

        // DieFromDreamDash sets DeathPosition = (Pos.X, Pos.Y - 5).
        // Vanilla dies where NaiveMove stopped, so the effect belongs within one dream-dash
        // frame (4 px) of her last live position -- nothing is repositioned.
        PointF diedAt = new PointF(lastLive.X, lastLive.Y - 5f);
        float dx = p.DeathPosition.X - diedAt.X, dy = p.DeathPosition.Y - diedAt.Y;
        Console.WriteLine($"    last live position ({diedAt.X,7:0.0}, {diedAt.Y,7:0.0})");
        Console.WriteLine($"    death effect drawn ({p.DeathPosition.X,7:0.0}, {p.DeathPosition.Y,7:0.0})");
        Console.WriteLine($"    offset             ({dx,7:0.0}, {dy,7:0.0})  " +
                          Verdict(Math.Abs(dx) <= 5f && Math.Abs(dy) <= 5f, "ok", "WRONG -- repositioned"));
    }

    public static int Run()
    {
        Console.WriteLine("Dream-dash death: where the effect lands relative to where she died.");
        Console.WriteLine("A dream dash covers 4 px/frame, so anything past a few px is a visible jump.");

        // A window (dream block) with a wall down its right side. The floor stops at the
        // block's left face so she can stand beside it and dash in.
        List<Solid> Scene() => new List<Solid>
        {
            Dream(100f, 0f, 200f, 200f),
            Wall(200f, 0f, 260f, 200f),      // wall she dies against, down the right side
            Wall(0f, 200f, 100f, 260f),      // floor, left of the block only
        };

        // Straight right: she leaves by the right face, which is the face the snap picks.
        Case("dash RIGHT, dies on the right face", Scene(), new PointF(90f, 150f), 1, 0);

        // Up-right: she enters low on the left face and leaves by the RIGHT face partway up,
        // so her death spot has nothing to do with the block's top edge.
        Case("dash UP-RIGHT, dies partway up the right face", Scene(), new PointF(90f, 200f), 1, -1);

        // Down-right: same, leaving by the right face partway down.
        Case("dash DOWN-RIGHT, dies partway down the right face", Scene(), new PointF(90f, 10f), 1, 1);

        OffScreenCases();
        StuckInsideCases();
        return failed;
    }

    static Player Inside(List<Solid> solids) => new Player
    {
        Solids = solids,
        MinX = -100000f,
        MaxX = 100000f,
        FreezeFramesEnabled = false,
        Dashes = 1,
        Pos = new PointF(100f, 100f),   // middle of a 0,0..200,200 block
        Facing = 1,
    };

    /// <summary>Inside a dream block she is held, as in Celeste; a window border still lets go.</summary>
    static void StuckInsideCases()
    {
        Console.WriteLine();
        Console.WriteLine("  Being inside a solid, starting at 100,100 in a 0,0..200,200 block");

        // Held by a dream block: gravity included, nothing moves, and she is silent -- she is
        // not landing on the block she is sitting in, so nothing should be reported at all.
        var held = Inside(new List<Solid> { Dream(0f, 0f, 200f, 200f) });
        PointF start = held.Pos;
        held.SoundEvents.Clear();
        int sounds = 0;
        float peakSpeed = 0f;
        for (int i = 0; i < 120; i++)
        {
            held.Update(Dt, new PetInput());
            sounds += held.SoundEvents.Count;
            held.SoundEvents.Clear();
            peakSpeed = Math.Max(peakSpeed, Math.Abs(held.Speed.X) + Math.Abs(held.Speed.Y));
        }
        Console.WriteLine($"    dream block, no input:  moved to ({held.Pos.X:0.0}, {held.Pos.Y:0.0})  " +
                          Verdict(held.Pos == start, "ok -- stuck", "WRONG -- drifted out"));
        Console.WriteLine($"      sounds over 2 seconds: {sounds}  " + Verdict(sounds == 0, "ok -- silent", "WRONG -- noisy"));
        Console.WriteLine($"      peak speed while held: {peakSpeed:0.0}  " +
                          Verdict(peakSpeed == 0f, "ok -- no momentum", "WRONG -- builds momentum"));

        // A dash still gets her out, which is what keeps being stuck recoverable.
        var dashOut = Inside(new List<Solid> { Dream(0f, 0f, 200f, 200f) });
        var dashInput = new PetInput { MoveX = 1, AimX = 1 };
        dashOut.BufferDash();
        bool dreamed = false;
        for (int i = 0; i < 120; i++)
        {
            dashInput.JumpPressed = dashOut.HasJumpBuffer;
            dashInput.DashPressed = dashOut.HasDashBuffer;
            dashOut.Update(Dt, dashInput);
            if (dashOut.State == Player.StDreamDash) dreamed = true;
        }
        bool escaped = dashOut.Pos.X - 4f >= 200f || dashOut.Pos.X + 4f <= 0f;
        Console.WriteLine($"    dream block, dash out:  ended at ({dashOut.Pos.X:0.0}, {dashOut.Pos.Y:0.0})  " +
                          Verdict(dreamed && escaped, "ok -- dashed free", dreamed ? "WRONG -- dashed but did not clear" : "WRONG -- no dream dash"));

        // A window border must not swallow her: one opening around her has to let her leave,
        // since outside dream mode there is no dash through it.
        var border = Inside(new List<Solid> { Wall(0f, 0f, 200f, 200f) });
        for (int i = 0; i < 120; i++) border.Update(Dt, new PetInput());
        Console.WriteLine($"    window border, no input: moved to ({border.Pos.X:0.0}, {border.Pos.Y:0.0})  " +
                          Verdict(border.Pos.Y > 100f, "ok -- falls free", "WRONG -- swallowed"));
    }

    static Solid OffScreenBand(float l, float t, float r, float b) =>
        new Solid { Id = new IntPtr(3), L = l, T = t, R = r, B = b, OffScreen = true };

    /// <summary>Desktop rule: the display edge kills, even inside a window that hangs past it.</summary>
    static void OffScreenCases()
    {
        Console.WriteLine();
        Console.WriteLine("  Desktop rule: a window hanging off the display");
        Console.WriteLine("  (display ends at x = 300; the window runs from 100 to 500)");

        // The window (dream block) extends well past the right edge of the display, and the
        // area beyond the display is the off-screen band PollSolids builds.
        const float screenRight = 300f;
        // Tall enough that a 45-degree dash reaches the display edge while still inside it.
        List<Solid> Scene() => new List<Solid>
        {
            Dream(100f, -200f, 500f, 200f),
            OffScreenBand(screenRight, -400f, screenRight + 400f, 600f),
            Wall(0f, 200f, 100f, 260f),      // floor, left of the block only
        };

        // Same window, but now the display also ends at y = 0, so an upward dash meets the top
        // edge first. This is the axis a clamp against both faces of the band would ruin.
        const float screenTop = 0f;
        List<Solid> SceneWithTop()
        {
            var s = Scene();
            s.Add(OffScreenBand(-400f, screenTop - 400f, 700f, screenTop));
            return s;
        }

        foreach ((string name, PointF start, int aimX, int aimY, bool withTop) in new[]
        {
            ("dash RIGHT through the window, past the display edge", new PointF(90f, 150f), 1, 0, false),
            ("dash UP-RIGHT through the window, past the display edge", new PointF(90f, 200f), 1, -1, false),
            ("dash UP-RIGHT with a top edge too (dies at the top)", new PointF(90f, 200f), 1, -1, true),
        })
        {
            var p = new Player
            {
                Solids = withTop ? SceneWithTop() : Scene(),
                MinX = -100000f,
                MaxX = 100000f,
                FreezeFramesEnabled = false,
                Dashes = 1,
                Pos = start,
                Facing = 1,
            };
            var input = new PetInput { MoveX = aimX, MoveY = aimY, AimX = aimX, AimY = aimY };
            p.BufferDash();
            bool everDreamed = false;
            for (int i = 0; i < 200 && !p.IsDead; i++)
            {
                input.JumpPressed = p.HasJumpBuffer;
                input.DashPressed = p.HasDashBuffer;
                p.Update(Dt, input);
                if (p.State == Player.StDreamDash) everDreamed = true;
            }

            Console.WriteLine();
            Console.WriteLine("  " + name);
            if (!everDreamed) { Console.WriteLine("    never entered the dream block -- setup wrong"); failed++; continue; }
            if (!p.IsDead) { Console.WriteLine("    NEVER DIED -- sailed off the display"); failed++; continue; }
            // She dies where she was, so the effect sits within a frame's travel of the edge
            // she crossed rather than being pulled back to it.
            float past = withTop
                ? screenTop - (p.DeathPosition.Y + 5f - 11f)   // how far her head went past the top
                : (p.DeathPosition.X + 4f) - screenRight;      // how far her side went past the right
            Console.WriteLine($"    death effect drawn ({p.DeathPosition.X,7:0.0}, {p.DeathPosition.Y,7:0.0})");
            Console.WriteLine($"    past the {(withTop ? "top  " : "right")} edge by {past,5:0.0} px  " +
                              Verdict(past >= 0f && past <= 5f, "ok -- dies as she crosses it", "WRONG -- not at the edge"));
        }

        // Assist mode bounces off a solid rather than dying (Celeste's Invincible path). The
        // display edge has to behave the same way, not strand her at the boundary.
        {
            var p = new Player
            {
                Solids = Scene(),
                MinX = -100000f,
                MaxX = 100000f,
                FreezeFramesEnabled = false,
                Invincible = true,
                Dashes = 1,
                Pos = new PointF(90f, 150f),
                Facing = 1,
            };
            var input = new PetInput { MoveX = 1, AimX = 1 };
            p.BufferDash();
            float furthest = float.MinValue;
            bool bounced = false;
            for (int i = 0; i < 200 && !p.IsDead; i++)
            {
                input.JumpPressed = p.HasJumpBuffer;
                input.DashPressed = p.HasDashBuffer;
                p.Update(Dt, input);
                furthest = Math.Max(furthest, p.Pos.X + 4f);
                if (p.Speed.X < 0f) bounced = true;
            }
            Console.WriteLine();
            Console.WriteLine("  assist mode (Invincible): dash RIGHT past the display edge");
            Console.WriteLine($"    died: {Verdict(!p.IsDead, "no", "YES -- should have bounced")}");
            Console.WriteLine($"    bounced back: {Verdict(bounced, "yes", "NO -- stranded at the edge")}");
            Console.WriteLine($"    furthest her side reached: {furthest:0.0} (display ends at {screenRight})");
        }
    }
}
