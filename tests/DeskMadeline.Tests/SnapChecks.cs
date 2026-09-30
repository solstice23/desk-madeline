using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// A drag sets her position outright, so it can leave her off the displays entirely, where
// nothing in the physics brings her back. PetWindow.ClampIntoDisplays is what returns her.
static class SnapChecks
{
    static int failed;

    // Two displays side by side, the second one taller and offset upward, so the seam
    // between them is only partly shared -- the arrangement that makes a naive
    // "inside one rectangle" test wrong.
    static readonly List<RectangleF> Displays = new List<RectangleF>
    {
        RectangleF.FromLTRB(0f, 0f, 300f, 200f),
        RectangleF.FromLTRB(300f, -50f, 600f, 200f),
    };

    const float Height = 11f;   // standing hitbox

    static void Check(string what, PointF from, PointF expected, int edgeWrapMode = 0)
    {
        PointF got = PetWindow.ClampIntoDisplays(from, 4f, Height, 0f, Displays, edgeWrapMode);
        bool ok = Math.Abs(got.X - expected.X) < 0.01f && Math.Abs(got.Y - expected.Y) < 0.01f;
        if (!ok) failed++;
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what,-46} " +
                          $"({from.X,6:0.#},{from.Y,6:0.#}) -> ({got.X,6:0.#},{got.Y,6:0.#})" +
                          (ok ? "" : $"   expected ({expected.X:0.#},{expected.Y:0.#})"));
    }

    static void CheckBox(string what, PointF from, PointF expected,
        float halfWidth, float above, float below)
    {
        PointF got = PetWindow.ClampIntoDisplays(from, halfWidth, above, below, Displays, 0);
        bool ok = Math.Abs(got.X - expected.X) < 0.01f && Math.Abs(got.Y - expected.Y) < 0.01f;
        if (!ok) failed++;
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what,-46} " +
                          $"({from.X,6:0.#},{from.Y,6:0.#}) -> ({got.X,6:0.#},{got.Y,6:0.#})" +
                          (ok ? "" : $"   expected ({expected.X:0.#},{expected.Y:0.#})"));
    }

    static void CheckWrap(string what, List<RectangleF> displays, bool horizontal, float line,
        float pos, float speed, float? expected)
    {
        bool wrapped = PetWindow.WrapAcross(displays, horizontal, line, pos, speed, 12f, out float got);
        bool ok = expected == null ? !wrapped : wrapped && Math.Abs(got - expected.Value) < 0.01f;
        if (!ok) failed++;
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what,-46} " +
                          $"{pos,6:0.#} -> {(wrapped ? got.ToString("0.#") : "stays")}" +
                          (ok ? "" : $"   expected {(expected == null ? "stays" : expected.Value.ToString("0.#"))}"));
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine("  Snapping back onto the displays after a drag");
        Console.WriteLine("  (displays: 0,0..300,200 and 300,-50..600,200)");

        // On a display already: left exactly as she is.
        Check("standing mid-display", new PointF(150f, 150f), new PointF(150f, 150f));
        Check("feet on the very bottom edge", new PointF(150f, 200f), new PointF(150f, 200f));
        Check("flush against the left edge", new PointF(4f, 100f), new PointF(4f, 100f));
        Check("head against the top edge", new PointF(150f, 11f), new PointF(150f, 11f));
        // Straddling the shared seam: whole across two displays, so not moved.
        Check("straddling the seam between displays", new PointF(300f, 150f), new PointF(300f, 150f));

        // Dropped off a display: brought back to the nearest edge.
        Check("dropped below the bottom", new PointF(150f, 400f), new PointF(150f, 200f));
        Check("dropped above the top", new PointF(150f, -80f), new PointF(150f, 11f));
        Check("dropped off the left", new PointF(-120f, 100f), new PointF(4f, 100f));
        Check("dropped off the right", new PointF(900f, 100f), new PointF(596f, 100f));
        // Nearest display wins: this is over the second one, whose top reaches higher.
        Check("above the taller display", new PointF(450f, -200f), new PointF(450f, -39f));
        // Just past the left edge, only partly off: still pulled fully on.
        Check("half off the left edge", new PointF(1f, 100f), new PointF(4f, 100f));
        // In the notch beside the taller display, off every display.
        Check("in the notch above the shorter display", new PointF(150f, -30f), new PointF(150f, 11f));

        // Everything else loose on the desktop is brought back the same way. The crystal and
        // the jelly hang below their position as she does; the seeker sits in the middle of
        // its own, so its clamp has to keep its lower half on the display too.
        CheckBox("crystal dropped below the bottom", new PointF(150f, 400f), new PointF(150f, 200f),
            TheoCrystal.HalfWidth, TheoCrystal.ColliderHeight, 0f);
        CheckBox("jelly dropped off the left", new PointF(-120f, 100f), new PointF(4f, 100f),
            Glider.HalfWidth, Glider.ColliderHeight, 0f);
        CheckBox("seeker dropped below the bottom", new PointF(150f, 400f), new PointF(150f, 197f),
            Seeker.HalfSize, Seeker.HalfSize, Seeker.HalfSize);
        CheckBox("seeker dropped above the top", new PointF(150f, -80f), new PointF(150f, 3f),
            Seeker.HalfSize, Seeker.HalfSize, Seeker.HalfSize);
        CheckBox("seeker already on a display is left alone", new PointF(150f, 150f), new PointF(150f, 150f),
            Seeker.HalfSize, Seeker.HalfSize, Seeker.HalfSize);

        // A wrapping axis is left alone; the other still snaps.
        Check("horizontal wrap: x free, y still clamped",
            new PointF(900f, 400f), new PointF(900f, 200f), edgeWrapMode: 1);
        Check("vertical wrap: y free, x still clamped",
            new PointF(900f, 400f), new PointF(596f, 400f), edgeWrapMode: 2);
        Check("both wrap: left alone",
            new PointF(900f, 400f), new PointF(900f, 400f), edgeWrapMode: 3);

        Console.WriteLine();
        Console.WriteLine("  Wrapping around the displays (margin 12, same two displays)");
        // Across both: out of the right of the second, back in at the left of the first.
        CheckWrap("off the right, row both share", Displays, true, 100f, 615f, 1f, -9f);
        CheckWrap("off the left, row both share", Displays, true, 100f, -15f, -1f, 609f);
        // Above the shorter display only the taller one is on the row: it wraps around itself,
        // and the notch beside it counts as off the screen.
        CheckWrap("off the right, row only the tall one has", Displays, true, -20f, 615f, 1f, 291f);
        CheckWrap("into the notch, row only the tall one has", Displays, true, -20f, 285f, -1f, 609f);
        // Up and down go by column, each display its own height.
        CheckWrap("off the top of the first", Displays, false, 150f, -15f, -1f, 209f);
        CheckWrap("off the bottom of the second", Displays, false, 450f, 215f, 1f, -59f);
        // Not yet: within the margin, or moving back in.
        CheckWrap("inside the margin", Displays, true, 100f, 605f, 1f, null);
        CheckWrap("past the edge but heading back", Displays, true, 100f, 615f, -1f, null);
        CheckWrap("seam between displays", Displays, true, 100f, 305f, 1f, null);
        // Far past an edge was a drop, not a run.
        CheckWrap("dropped far past the edge", Displays, true, 100f, 700f, 1f, null);
        // Kept to one monitor, the world is that monitor: the seam is an edge.
        var one = new List<RectangleF> { Displays[0] };
        CheckWrap("one monitor: off its right at the seam", one, true, 100f, 315f, 1f, -9f);
        // Two monitors with a gap between them, joined by a wide one below. On their row the
        // gap stops the ray back, so each wraps on its own; below, the wide one wraps alone.
        var gapped = new List<RectangleF>
        {
            RectangleF.FromLTRB(0f, 0f, 100f, 100f),
            RectangleF.FromLTRB(200f, 0f, 300f, 100f),
            RectangleF.FromLTRB(0f, 100f, 300f, 200f),
        };
        CheckWrap("gap: off the right of the second", gapped, true, 50f, 315f, 1f, 191f);
        CheckWrap("gap: into it from the first", gapped, true, 50f, 115f, 1f, -9f);
        CheckWrap("gap: into it from the second", gapped, true, 50f, 185f, -1f, 309f);
        CheckWrap("gap: off the right of the wide one", gapped, true, 150f, 315f, 1f, -9f);
        CheckWrap("gap: down through its column", gapped, false, 150f, 215f, 1f, 91f);

        return failed;
    }
}
