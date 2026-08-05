using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// Harness for the climb/slip report: drives Player.Update at a fixed 60Hz over
// window-derived Solids built the way PetWindow.PollSolids builds them (hollow
// borders, physical pixels converted by GameScale), and measures vertical drift
// while grab is held.
//
// Two independent physical offsets are swept, because both feed the collision math:
//   * the window's own left coordinate (the wall she grabs)
//   * the cursor position a drag left her at (her own X)
//
// Vanilla numbers being asserted (Celeste Player.ClimbUpdate):
//   grab, no direction  ->   0 px/s  (holding still on a wall)
//   grab + up           -> -45 px/s  (ClimbUpSpeed)
//   slipping off a lip  -> +30 px/s  (ClimbSlipSpeed) -- only near a ledge
static class GridChecks
{
    const float Dt = 1f / 60f;
    const int BorderPx = 8;   // PetWindow.WindowBorderPx

    static int scale = 6;     // PetWindow.GameScale
    static bool gridSnapped;  // false = code before the fix, true = after

    // PetWindow.ToGamePixels
    static float ToGame(int physical) => gridSnapped
        ? (float)Math.Floor(physical / (double)scale + 0.5)
        : physical / (float)scale;

    /// <summary>PetWindow.WindowEdges + PollSolids' conversion, including TryToSolid's drop rule.</summary>
    static List<Solid> BuildWindow(int left, int top, int right, int bottom)
    {
        int b = gridSnapped ? Math.Max(BorderPx, scale) : BorderPx;
        var edges = new[]
        {
            (L: left, T: top, R: right, B: top + b),                        // top
            (L: left, T: bottom - b, R: right, B: bottom),                  // bottom
            (L: left, T: top + b, R: left + b, B: bottom - b),              // left
            (L: right - b, T: top + b, R: right, B: bottom - b),            // right
        };
        var solids = new List<Solid>();
        foreach (var e in edges)
        {
            float l = ToGame(e.L), t = ToGame(e.T), r = ToGame(e.R), bb = ToGame(e.B);
            if (gridSnapped && (r <= l || bb <= t)) continue;   // TryToSolid drops sub-pixel remnants
            solids.Add(new Solid { Id = new IntPtr(1), L = l, T = t, R = r, B = bb });
        }
        return solids;
    }

    struct Result
    {
        public float Drift;            // px moved during the 1s measure window
        public int StateFlips;         // climb <-> non-climb transitions
        public float ClimbFraction;    // share of measured frames in StClimb
        public bool Grabbed;
    }

    static Result RunClimb(int windowLeft, int cursorPhysicalX, int moveY, int facing)
    {
        // Tall window so neither climbing up nor slipping down reaches a lip within
        // the measured second (the border strip spans y = 21.3 .. 198.7 game px).
        const int top = 120, bottom = 1200;
        int windowRight = windowLeft + 900;
        var solids = BuildWindow(windowLeft, top, windowRight, bottom);

        var p = new Player
        {
            Solids = solids,
            InfiniteStamina = true,   // isolate slipping from the stamina timeout
            Facing = facing,
            MinX = -100000f,
            MaxX = 100000f,
        };
        // Dropped by a mouse drag a few pixels from the strip she should grab
        // (PetWindow's WM_MOUSEMOVE handler), then she walks into it.
        p.Pos = new PointF(ToGame(cursorPhysicalX), 100f);

        var input = new PetInput { MoveX = facing, GrabHeld = true };

        bool grabbed = false;
        for (int i = 0; i < 90; i++)   // setup: move into the wall and grab
        {
            p.Update(Dt, input);
            if (p.State == Player.StClimb) grabbed = true;
        }

        input.MoveY = moveY;
        float y0 = p.Pos.Y;
        int flips = 0, climbFrames = 0;
        bool wasClimb = p.State == Player.StClimb;
        for (int i = 0; i < 60; i++)   // measure: exactly one second
        {
            p.Update(Dt, input);
            bool isClimb = p.State == Player.StClimb;
            if (isClimb != wasClimb) flips++;
            wasClimb = isClimb;
            if (isClimb) climbFrames++;
        }

        return new Result
        {
            Drift = p.Pos.Y - y0,
            StateFlips = flips,
            ClimbFraction = climbFrames / 60f,
            Grabbed = grabbed,
        };
    }

    static bool ClimbOk(Result still, Result up) =>
        still.Grabbed && up.Grabbed
        && Math.Abs(still.Drift) < 0.5f            // holds still instead of slipping
        && Math.Abs(up.Drift - -45f) < 1.5f        // climbs at ClimbUpSpeed
        && up.StateFlips == 0 && up.ClimbFraction == 1f;

    static void Sweep(string title, int facing, ref int failed, ref int total)
    {
        Console.WriteLine();
        Console.WriteLine("  " + title);
        Console.WriteLine("            cursor x (physical, mod " + scale + ")");
        Console.Write("  window x  ");
        for (int c = 0; c < scale; c++) Console.Write($"{c,8}");
        Console.WriteLine();

        for (int windowLeft = 600; windowLeft < 600 + scale; windowLeft++)
        {
            Console.Write($"  {windowLeft,8}  ");
            for (int c = 0; c < scale; c++)
            {
                // Drop her ~8 game px from the wall, offset by c physical pixels.
                int cursorX = facing > 0 ? windowLeft - 8 * scale + c : windowLeft + 900 + 8 * scale + c;
                var still = RunClimb(windowLeft, cursorX, 0, facing);
                var up = RunClimb(windowLeft, cursorX, -1, facing);
                bool ok = ClimbOk(still, up);
                total++;
                if (!ok) failed++;
                Console.Write($"{(ok ? "ok" : (still.Grabbed ? "SLIP" : "NOGRAB")),8}");
            }
            Console.WriteLine();
        }
    }

    /// <summary>Every window keeps four usable borders at every offered GameScale.</summary>
    static void BorderIntegrity(ref int failed, ref int total)
    {
        Console.WriteLine();
        Console.WriteLine("  Window borders survive the snap (4 solids, each >= 1 game px thick)");
        gridSnapped = true;
        foreach (int s in new[] { 2, 3, 4, 5, 6, 8 })
        {
            scale = s;
            int worst = int.MaxValue, bad = 0;
            for (int off = 0; off < s; off++)
            {
                var solids = BuildWindow(600 + off, 120 + off, 600 + off + 900, 120 + off + 600);
                total++;
                if (solids.Count != 4) { bad++; failed++; continue; }
                foreach (var so in solids)
                    worst = Math.Min(worst, (int)Math.Min(so.R - so.L, so.B - so.T));
                if (worst < 1) { bad++; failed++; }
            }
            Console.WriteLine($"    scale {s}x: thinnest border {worst} game px, {(bad == 0 ? "ok" : bad + " BAD")}");
        }
        scale = 6;
    }

    /// <summary>She lands on a window's top border and stays there, at every offset.</summary>
    static void LandOnWindow(ref int failed, ref int total)
    {
        Console.WriteLine();
        Console.WriteLine("  Landing on a window top (lands, rests, stays put)");
        foreach (bool snapped in new[] { false, true })
        {
            gridSnapped = snapped;
            int bad = 0;
            for (int off = 0; off < scale; off++)
            {
                int top = 600 + off;
                var solids = BuildWindow(400, top, 1600, top + 600);
                var p = new Player { Solids = solids, MinX = -100000f, MaxX = 100000f };
                p.Pos = new PointF(ToGame(1000), ToGame(top) - 30f);   // above the top border
                var input = new PetInput();
                for (int i = 0; i < 60; i++) p.Update(Dt, input);      // fall and settle
                float restY = p.Pos.Y;
                for (int i = 0; i < 60; i++) p.Update(Dt, input);      // stay for a second
                total++;
                bool ok = p.onGround && p.Speed.Y == 0f && p.Pos.Y == restY;
                if (!ok) { bad++; failed++; }
            }
            Console.WriteLine($"    {(snapped ? "after " : "before")}: {scale - bad}/{scale} offsets ok");
        }
    }

    /// <summary>A Seeker death must put her back on whole pixels, or climbing breaks again.</summary>
    static void RespawnOnGrid(ref int failed, ref int total)
    {
        Console.WriteLine();
        Console.WriteLine("  Respawn after a Seeker death lands on the grid");
        gridSnapped = true;
        int bad = 0, cases = 0;
        // Vary the angle from the killing Seeker: the respawn search offsets by cos/sin.
        foreach (int dx in new[] { -30, -10, 0, 10, 30 })
        foreach (int dy in new[] { -20, 0, 20 })
        {
            var solids = BuildWindow(400, 600, 1600, 1200);
            var p = new Player { Solids = solids, MinX = -100000f, MaxX = 100000f };
            p.Pos = new PointF(ToGame(1000), ToGame(600) - 30f);
            var input = new PetInput();
            for (int i = 0; i < 60; i++) p.Update(Dt, input);      // land on the window top

            p.DieFromSeeker(new PointF(p.Pos.X + dx, p.Pos.Y + dy));
            for (int i = 0; i < 300; i++) p.Update(Dt, input);     // die, then respawn

            cases++;
            total++;
            bool onGrid = p.Pos.X == (float)Math.Floor(p.Pos.X) && p.Pos.Y == (float)Math.Floor(p.Pos.Y);
            if (p.IsDead || p.IsRespawning || !onGrid) { bad++; failed++; }
        }
        Console.WriteLine($"    {cases - bad}/{cases} respawns land on whole pixels");
    }

    public static int Run()
    {
        Console.WriteLine("Vanilla: grab-only dy 0.00 | grab+up dy -45.00 | climb% 100 | flips 0");
        Console.WriteLine("Each cell = one (window position, drag position) pair, both swept over a full");
        Console.WriteLine("game pixel of physical offsets. 'ok' means she climbs; 'SLIP' means she does not.");

        int totalFailed = 0;
        foreach (bool snapped in new[] { false, true })
        {
            gridSnapped = snapped;
            int failed = 0, total = 0;
            Console.WriteLine();
            Console.WriteLine(new string('=', 74));
            Console.WriteLine(snapped ? "AFTER: window rects and player position snapped to the game-pixel grid"
                                      : "BEFORE: physical pixels divided by GameScale");
            Console.WriteLine(new string('=', 74));
            Sweep("wall on her RIGHT (window's left border, facing +1)", +1, ref failed, ref total);
            Sweep("wall on her LEFT  (window's right border, facing -1)", -1, ref failed, ref total);
            Console.WriteLine();
            Console.WriteLine($"  => {total - failed}/{total} climb correctly");
            if (snapped) totalFailed += failed;
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("REGRESSION CHECKS on the snapped geometry");
        Console.WriteLine(new string('=', 74));
        int regFailed = 0, regTotal = 0;
        BorderIntegrity(ref regFailed, ref regTotal);
        LandOnWindow(ref regFailed, ref regTotal);
        RespawnOnGrid(ref regFailed, ref regTotal);
        totalFailed += regFailed;

        return totalFailed;
    }
}
