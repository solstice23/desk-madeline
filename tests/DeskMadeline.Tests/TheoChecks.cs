using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

/// <summary>How the Theo crystal breaks.</summary>
/// <remarks>
/// TheoCrystal.Die (celeste_reference/Celeste/TheoCrystal.cs:457) takes the player with him,
/// plays the death sound at the crystal, hides the sprite and leaves a DeathEffect in forest
/// green to play out where he broke. The port used to delete him outright, which skipped both
/// the sound and the burst.
/// </remarks>
static class TheoChecks
{
    const float Dt = 1f / 60f;
    const float DeathEffectDuration = 0.834f;   // DeathEffect.Duration

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("THEO CRYSTAL");
        Console.WriteLine(new string('=', 74));

        var world = new RectangleF(0f, 0f, 600f, 400f);
        var solids = new List<Solid>();
        var player = new Player { Solids = solids, MinX = -100000f, MaxX = 100000f };
        player.ResetTo(new PointF(100f, 100f));

        // Dropped below the world, which is the way he breaks here.
        var theo = new TheoCrystal(new PointF(300f, world.Bottom + 40f));
        var sounds = new List<string>();
        int frames = 0;
        float lastPercent = -1f;
        bool everDying = false, monotonic = true;

        while (frames < 120 && !theo.Removed)
        {
            theo.Update(Dt, player, solids, world);
            while (theo.SoundEvents.Count > 0)
                sounds.Add(theo.SoundEvents.Dequeue().Path.Replace("event:/char/madeline/", ""));
            if (theo.IsDying)
            {
                everDying = true;
                if (theo.DeathPercent < lastPercent) monotonic = false;
                lastPercent = theo.DeathPercent;
            }
            frames++;
        }

        Check("he breaks rather than vanishing", everDying);
        Check("the death sound plays at the crystal", sounds.Contains("death"));
        // Vanilla's TheoCrystal.Die calls Die on the player first, because in Celeste losing
        // the crystal means restarting the room and killing her is how a room restarts. A
        // desktop has no rooms, and him dropping off the bottom of the screen is no reason for
        // her to die wherever she happens to be standing, so here he goes alone. Deliberate,
        // and pinned so that it stays a decision rather than becoming a regression.
        Check("and he goes alone, unlike the game, there being no room to restart",
            !player.IsDead && !player.IsRespawning);
        Check("the burst runs forward, not backwards", monotonic);
        Check("he is gone once it finishes", theo.Removed && lastPercent >= 1f);
        // One DeathEffect.Duration at 60Hz, plus the frame that started it.
        int expected = (int)Math.Ceiling(DeathEffectDuration / Dt) + 1;
        Check($"the burst lasts DeathEffect.Duration ({frames} frames, wanted about {expected})",
            Math.Abs(frames - expected) <= 2);

        Console.WriteLine($"      sounds: {string.Join(", ", sounds)}");
        return failed;
    }
}
