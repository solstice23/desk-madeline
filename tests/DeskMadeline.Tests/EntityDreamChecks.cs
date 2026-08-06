using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

/// <summary>What a dream block does to a crystal, a jelly or a seeker dropped inside it.</summary>
/// <remarks>
/// DreamBlock is `public class DreamBlock : Solid` with no exemption for anything but a
/// dashing player: DreamBlock.BlockedCheck treats a TheoCrystal as an actor it is blocked by,
/// and Actor.MoveHExact tests only the destination, so an actor already inside one cannot
/// move at all. Dropped into a window in dream mode they are held exactly as Madeline is,
/// and a drag is the way out, there being no dash for them.
///
/// A window border is different and deliberately so: it collides only from the outside, so a
/// window opening around one of them does not swallow it.
/// </remarks>
static class EntityDreamChecks
{
    const float Dt = 1f / 60f;

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static List<Solid> Block(bool dream) => new List<Solid>
    {
        new Solid { Id = new IntPtr(1), L = 0f, T = 0f, R = 200f, B = 200f, Dream = dream },
    };

    static readonly RectangleF World = new RectangleF(-400f, -400f, 1200f, 1200f);

    /// <summary>Drop it in the middle of the block and see whether it can leave, and how loudly.</summary>
    static (bool Moved, PointF To, int Sounds) TheoIn(bool dream)
    {
        var solids = Block(dream);
        var player = new Player { Solids = solids, MinX = -100000f, MaxX = 100000f };
        player.ResetTo(new PointF(500f, 500f));
        var theo = new TheoCrystal(new PointF(100f, 100f));
        PointF from = theo.Pos;
        int sounds = 0;
        for (int i = 0; i < 120; i++)
        {
            theo.Update(Dt, player, solids, World);
            sounds += theo.SoundEvents.Count;
            theo.SoundEvents.Clear();
        }
        return (theo.Pos != from, theo.Pos, sounds);
    }

    static (bool Moved, PointF To, int Sounds) GliderIn(bool dream)
    {
        var solids = Block(dream);
        var glider = new Glider(new PointF(100f, 100f));
        PointF from = glider.Pos;
        int sounds = 0;
        for (int i = 0; i < 120; i++)
        {
            glider.Update(Dt, new PetInput(), solids, -100000f, 100000f);
            sounds += glider.SoundEvents.Count;
            glider.SoundEvents.Clear();
        }
        return (glider.Pos != from, glider.Pos, sounds);
    }

    static (bool Moved, PointF To, int Sounds) SeekerIn(bool dream)
    {
        var solids = Block(dream);
        var player = new Player { Solids = solids, MinX = -100000f, MaxX = 100000f };
        player.ResetTo(new PointF(500f, 500f));
        var seeker = new Seeker(new PointF(100f, 100f));
        PointF from = seeker.Pos;
        int sounds = 0;
        for (int i = 0; i < 120; i++)
        {
            seeker.Update(Dt, player, solids, World, World, new List<TheoCrystal>());
            sounds += seeker.SoundEvents.Count;
            seeker.SoundEvents.Clear();
        }
        return (seeker.Pos != from, seeker.Pos, sounds);
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("DROPPED INSIDE A BLOCK (dream blocks hold, window borders let go)");
        Console.WriteLine(new string('=', 74));

        var theoDream = TheoIn(dream: true);
        var theoBorder = TheoIn(dream: false);
        Console.WriteLine($"    theo:   dream ({theoDream.To.X:0},{theoDream.To.Y:0}) {theoDream.Sounds} sounds; " +
                          $"border ({theoBorder.To.X:0},{theoBorder.To.Y:0})");
        Check("a dream block holds the crystal", !theoDream.Moved);
        Check("held, the crystal is silent", theoDream.Sounds == 0);
        Check("a window border still lets the crystal go", theoBorder.Moved);

        var gliderDream = GliderIn(dream: true);
        var gliderBorder = GliderIn(dream: false);
        Console.WriteLine($"    jelly:  dream ({gliderDream.To.X:0},{gliderDream.To.Y:0}) {gliderDream.Sounds} sounds; " +
                          $"border ({gliderBorder.To.X:0},{gliderBorder.To.Y:0})");
        Check("a dream block holds the jelly", !gliderDream.Moved);
        Check("held, the jelly is silent", gliderDream.Sounds == 0);
        Check("a window border still lets the jelly go", gliderBorder.Moved);

        // The seeker already tested only its destination, so it was held either way, in a
        // border as much as in a dream block -- and sounded itself against both.
        var seekerDream = SeekerIn(dream: true);
        var seekerBorder = SeekerIn(dream: false);
        Console.WriteLine($"    seeker: dream ({seekerDream.To.X:0},{seekerDream.To.Y:0}) {seekerDream.Sounds} sounds; " +
                          $"border {seekerBorder.Sounds} sounds");
        Check("a dream block holds the seeker", !seekerDream.Moved);
        Check("held, the seeker is silent", seekerDream.Sounds == 0);
        Check("held in a border, the seeker is silent too", seekerBorder.Sounds == 0);

        return failed;
    }
}
