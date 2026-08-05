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
        PointF got = PetWindow.ClampIntoDisplays(from, Height, Displays, edgeWrapMode);
        bool ok = Math.Abs(got.X - expected.X) < 0.01f && Math.Abs(got.Y - expected.Y) < 0.01f;
        if (!ok) failed++;
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what,-46} " +
                          $"({from.X,6:0.#},{from.Y,6:0.#}) -> ({got.X,6:0.#},{got.Y,6:0.#})" +
                          (ok ? "" : $"   expected ({expected.X:0.#},{expected.Y:0.#})"));
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

        // A wrapping axis is left alone; the other still snaps.
        Check("horizontal wrap: x free, y still clamped",
            new PointF(900f, 400f), new PointF(900f, 200f), edgeWrapMode: 1);
        Check("vertical wrap: y free, x still clamped",
            new PointF(900f, 400f), new PointF(596f, 400f), edgeWrapMode: 2);
        Check("both wrap: left alone",
            new PointF(900f, 400f), new PointF(900f, 400f), edgeWrapMode: 3);

        return failed;
    }
}
