using System;
using System.Collections.Generic;
using System.Drawing;
using DeskMadeline;

// Swimming, against the numbers in Celeste's Player.SwimUpdate.
//
// Water is not solid -- vanilla's Water is a plain Entity -- so the check that matters most
// is that she does not stand on it. The rest are the constants: 60 up, 80 across and down,
// 60 across while fully under, approached at 600 a second.
//
// The pool below runs from y=-400 (its surface) down to y=0, so "deep" is a larger y than
// "near the surface". Which side is which decides every condition in the state: she is only
// pushed upwards while there is no water 18 pixels above her, and can only jump out while
// there is none 14 above -- both of them true near the top and false down in the water.
static class WaterChecks
{
    const float Dt = 1f / 60f;
    const float Surface = -400f;

    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static Player InPool(float y, params Solid[] solids)
        => new Player
        {
            Solids = new List<Solid>(solids),
            Waters = new List<Solid>
            {
                new Solid { Id = new IntPtr(1), L = -500f, T = Surface, R = 500f, B = 0f }
            },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Dashes = 1,
            Pos = new PointF(0f, y)
        };

    static PetInput Neutral => new PetInput();

    static PetInput Aim(int x, int y) =>
        new PetInput { MoveX = x, MoveY = y, FeatherX = x, FeatherY = y, AimX = x, AimY = y };

    static void Run(Player player, PetInput input, int frames)
    {
        for (int i = 0; i < frames; i++) player.Update(Dt, input);
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("WATER: swimming, against Player.SwimUpdate");
        Console.WriteLine(new string('=', 74));

        Console.WriteLine("  Water is not a floor");
        var falling = InPool(Surface + 40f);
        Run(falling, Neutral, 6);
        Check("she moves through it rather than landing on the surface", !falling.onGround);
        Check("and she is swimming, not falling", falling.State == 3);

        Console.WriteLine();
        Console.WriteLine("  Deep water (SwimVDeccel 600, no rise while 18px of it is overhead)");
        var deep = InPool(Surface + 200f);
        deep.Speed = new PointF(0f, 120f);
        Run(deep, Neutral, 30);
        Check($"a sinking speed decays to nothing rather than to a rise ({deep.Speed.Y:F0})",
            Math.Abs(deep.Speed.Y) < 0.5f);
        var sinking = InPool(Surface + 200f);
        Run(sinking, Aim(0, 1), 30);
        Check($"held down she sinks at 80/s ({sinking.Speed.Y:F0})",
            Math.Abs(sinking.Speed.Y - 80f) < 0.5f);
        var acrossDeep = InPool(Surface + 200f);
        Run(acrossDeep, Aim(1, 0), 30);
        Check($"and crosses at 60/s, the underwater maximum ({acrossDeep.Speed.X:F0})",
            Math.Abs(acrossDeep.Speed.X - 60f) < 0.5f);

        Console.WriteLine();
        Console.WriteLine("  The surface (SwimMaxRise -60)");
        var hovering = InPool(Surface + 200f);
        Run(hovering, Neutral, 120);
        Check($"deeper than that she hangs where she is ({hovering.Pos.Y:F0}, from {Surface + 200f:F0})",
            Math.Abs(hovering.Pos.Y - (Surface + 200f)) < 2f);
        // The first frame is the one she enters on; by the third she is rising in earnest,
        // and once she reaches the top the clamp below zeroes it again -- so this is measured
        // on the way up rather than after.
        // She enters on the first frame with a frame of gravity in her, halved by SwimBegin,
        // so the rise starts from +7.5 and gains 10 a frame: -12.5, -22.5, -32.5, -42.5.
        var rising = InPool(Surface + 17f);
        Run(rising, Neutral, 6);
        Check($"within 18px of it she is pushed up ({rising.Speed.Y:F1}/s, towards 60)",
            Math.Abs(rising.Speed.Y + 42.5f) < 0.5f);
        var floating = InPool(Surface + 10f);
        Run(floating, Neutral, 120);
        Check($"and she comes to rest at the top of the water ({floating.Pos.Y:F0})",
            floating.Pos.Y > Surface - 12f && floating.Pos.Y < Surface + 20f);
        Check("still swimming, not fallen out of it", floating.State == 3);

        Console.WriteLine();
        Console.WriteLine("  Entering (SwimBegin halves a fall)");
        var diving = InPool(Surface - 40f);
        diving.Speed = new PointF(0f, 200f);
        for (int i = 0; i < 120 && diving.State != 3; i++) diving.Update(Dt, Neutral);
        Check("a fall into it becomes a swim", diving.State == 3);
        Check($"halved on the way in ({diving.Speed.Y:F0} of about 200)",
            diving.Speed.Y > 0f && diving.Speed.Y < 160f);

        Console.WriteLine();
        Console.WriteLine("  Leaving");
        var jumping = InPool(Surface + 10f);
        Run(jumping, Neutral, 120);                       // float up to the top first
        jumping.Update(Dt, new PetInput { JumpPressed = true });
        Check("a jump at the surface leaves the water", jumping.State == 0);
        Check($"with a jump's speed, not a swim's ({jumping.Speed.Y:F0})",
            jumping.Speed.Y < -100f);
        var submerged = InPool(Surface + 200f);
        submerged.Update(Dt, new PetInput { JumpPressed = true });
        Check("a jump from deep water does nothing, being 14px under", submerged.State == 3);

        var above = InPool(Surface - 200f);
        Run(above, Neutral, 4);
        Check("above the water she is in the normal state", above.State == 0);

        Console.WriteLine();
        Console.WriteLine("  Water gives the dash back (orig_Update refills while State == 3)");
        var spent = InPool(Surface + 200f);
        spent.Dashes = 0;
        Run(spent, Neutral, 3);
        Check("swimming refills it", spent.Dashes == 1);
        var airborne = InPool(Surface - 200f);
        airborne.Dashes = 0;
        Run(airborne, Neutral, 3);
        Check("falling above the water does not", airborne.Dashes == 0);

        Console.WriteLine();
        Console.WriteLine("  What else is loose on the desktop");
        // Only Player and CrushBlock carry a WaterInteraction in Celeste; the jellyfish, the
        // crystal and the seeker have no water code at all and fall straight through it. That
        // is a decision of the game's rather than an omission here, so it is pinned: giving
        // them buoyancy would be inventing a mechanic, which AGENTS.md says not to do.
        var jelly = new Glider(new PointF(0f, Surface - 20f));
        var noSolids = new List<Solid>();
        float before = jelly.Pos.Y;
        for (int i = 0; i < 60; i++) jelly.Update(Dt, new PetInput(), noSolids, -100000f, 100000f);
        // It drifts rather than falls -- that is what a jellyfish does -- so what is being
        // asked is only that the water neither held it up nor slowed it.
        Check($"a jellyfish drifts through water as if it were not there ({before:F0} to {jelly.Pos.Y:F0})",
            jelly.Pos.Y > before + 20f);

        Console.WriteLine();
        Console.WriteLine("  Carrying something into it");
        // Picked up above the water, then carried into it, which is how it happens: pickup
        // lives in the normal state, as it does in vanilla, and swimming never offers it.
        var carrying = InPool(Surface - 40f);
        var carried = new Glider(new PointF(0f, Surface - 40f));
        carrying.Holdables = new List<IPetHoldable> { carried };
        carrying.Update(Dt, new PetInput { GrabHeld = true });
        Check("she picks a jellyfish up out of the water", carrying.Holding != null);
        for (int i = 0; i < 120 && carrying.State != 3; i++)
        {
            carrying.Update(Dt, new PetInput { GrabHeld = true });
            carried.Update(Dt, new PetInput(), carrying.Solids, -100000f, 100000f);
        }
        Check("and swims with it in hand", carrying.State == 3 && carrying.Holding != null);
        Run(carrying, new PetInput { MoveY = 1 }, 2);      // let go of grab, holding down
        Check("and can let go of it while swimming", carrying.Holding == null);

        Console.WriteLine();
        Console.WriteLine("  Grab, at the edge of a pool");
        var wall = new Solid { Id = new IntPtr(9), L = 4f, T = Surface - 100f, R = 100f, B = 0f };
        var states = new List<int>();
        var grabbing = InPool(Surface + 10f, wall);
        grabbing.Facing = 1;
        for (int i = 0; i < 40; i++)
        {
            grabbing.Update(Dt, new PetInput { GrabHeld = true });
            states.Add(grabbing.State);
        }
        int swaps = 0;
        for (int i = 1; i < states.Count; i++) if (states[i] != states[i - 1]) swaps++;
        Console.WriteLine($"      states: {string.Join("", states)}  ({swaps} changes)");
        Check($"holding grab beside a wall settles instead of flickering ({swaps} changes)",
            swaps <= 2);

        Console.WriteLine();
        Console.WriteLine("  Sounds");
        var splashing = InPool(Surface - 40f);
        splashing.Speed = new PointF(0f, 200f);
        var heard = new List<string>();
        for (int i = 0; i < 120; i++)
        {
            splashing.Update(Dt, Neutral);
            while (splashing.SoundEvents.Count > 0)
                heard.Add(splashing.SoundEvents.Dequeue().Path);
        }
        Check("going in sounds water_in exactly once",
            heard.FindAll(p => p == "event:/char/madeline/water_in").Count == 1);
        Check("and nothing sounds water_out while she stays in",
            !heard.Contains("event:/char/madeline/water_out"));

        return failed;
    }
}
