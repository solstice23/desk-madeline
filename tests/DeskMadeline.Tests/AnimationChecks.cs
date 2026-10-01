using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DeskMadeline;

/// <summary>
/// Her sprite, played the way the game plays it: Player.UpdateSprite and the constructor's
/// callbacks over the game's own sprite bank, and Player.UpdateHair's colour.
/// </summary>
/// <remarks>
/// Driven in PetWindow's order -- the sprite's component update, then the player's update,
/// which chooses the next animation. Needs an installed Celeste for the bank.
/// </remarks>
static class AnimationChecks
{
    const float Dt = 1f / 60f;
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static Player OnFloor(SpriteBank bank, float x = 100f, float y = 200f)
    {
        var p = new Player
        {
            Solids = new List<Solid> { new Solid { Id = new IntPtr(1), L = -2000f, T = 200f, R = 2000f, B = 260f } },
            MinX = -100000f,
            MaxX = 100000f,
            FreezeFramesEnabled = false,
            Pos = new PointF(x, y),
            Facing = 1,
        };
        p.ResetSprite(bank, "player", Player.ModeMadeline, null);
        return p;
    }

    static void Step(Player p, PetInput input, List<string> heard = null)
    {
        input.JumpPressed = p.HasJumpBuffer;
        input.DashPressed = p.HasDashBuffer;
        p.Sprite.Update(Dt);
        p.Update(Dt, input);
        while (p.SoundEvents.Count > 0)
        {
            PlayerSoundEvent e = p.SoundEvents.Dequeue();
            heard?.Add(e.IsStop ? "stop " + e.Key : e.Path.Replace("event:/char/madeline/", ""));
        }
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("ANIMATION: Player.UpdateSprite and its callbacks, Player.UpdateHair");
        Console.WriteLine(new string('=', 74));

        string xml = CelesteInstall.GraphicsFile("Sprites.xml");
        if (xml == null || CelesteInstall.AtlasesDirectory == null)
        {
            Console.WriteLine("  no Celeste install found -- skipped");
            return 0;
        }
        Sprites.LoadAll(Path.Combine(Path.GetTempPath(), "deskmadeline-no-assets"));
        var bank = SpriteBank.Load(Sprites.Atlas, XDocument.Load(xml), Array.Empty<XDocument>());

        CheckFidgets(bank);
        CheckHairFlash(bank);
        CheckStumble(bank);
        return failed;
    }

    /// <summary>Player's OnLastFrame: after three seconds still, idle sometimes becomes a fidget.</summary>
    static void CheckFidgets(SpriteBank bank)
    {
        var p = OnFloor(bank);
        var input = new PetInput();
        var heard = new List<string>();
        var played = new List<(int Frame, string Id)>();
        string last = null;
        for (int frame = 0; frame < 60 * 120; frame++)
        {
            Step(p, input, heard);
            if (p.AnimId != last && p.AnimId.StartsWith("idle") && p.AnimId != "idle") played.Add((frame, p.AnimId));
            last = p.AnimId;
        }
        Check("she stands in idle, on the game's idle frames",
            p.Sprite.Animations["idle"].Frames[0] == "characters/player/idle00");
        Check($"fidgets come, none before three seconds still ({played.Count} in two minutes)",
            played.Count > 0 && played.All(f => f.Frame > 180));
        Check("and only the three the cold set holds",
            played.All(f => f.Id == "idleA" || f.Id == "idleB" || f.Id == "idleC"));
        int scratches = played.Count(f => f.Id == "idleB"), sneezes = played.Count(f => f.Id == "idleC");
        Check($"each scratch and sneeze is heard, and crack-knuckles never ({scratches} idleB, {sneezes} idleC)",
            heard.Count(h => h == "idle_scratch") == scratches && heard.Count(h => h == "idle_sneeze") == sneezes &&
            !heard.Contains("idle_crackknuckles"));
    }

    /// <summary>Player.UpdateHair: white for 0.12s when the count changes, unless it went to none.</summary>
    static void CheckHairFlash(SpriteBank bank)
    {
        var p = OnFloor(bank);
        p.SetDashMode(2);
        var input = new PetInput();
        for (int i = 0; i < 30; i++) Step(p, input);
        Color pink = p.HairColor;
        Check("two dashes are pink", pink.ToArgb() == Player.TwoDashesHairColor.ToArgb());

        // Spend one in the air, so the floor cannot refill it.
        p.Pos = new PointF(p.Pos.X, 100f);
        p.Speed = PointF.Empty;
        input.AimX = 1; input.MoveX = 1;
        p.BufferDash(false);
        int white = 0;
        for (int i = 0; i < 20; i++)
        {
            Step(p, input);
            if (p.Dashes == 1 && p.HairColor.ToArgb() == Color.White.ToArgb()) white++;
        }
        // The frame the count changes, then every frame the 0.12s timer is still above zero
        // when tested -- counted the way UpdateHair counts it down, in floats.
        int expected = 1;
        for (float timer = 0.12f; timer > 0f; timer -= Dt) expected++;
        Check($"two to one flashes white for 0.12s ({white} frames, {expected} by the timer)", white == expected);

        p.BufferDash(false);
        bool everWhite = false;
        for (int i = 0; i < 20; i++)
        {
            Step(p, input);
            if (p.Dashes == 0 && p.HairColor.ToArgb() == Color.White.ToArgb()) everWhite = true;
        }
        Check("one to none does not flash, and fades towards blue",
            p.Dashes == 0 && !everWhite && p.HairColor.B > p.HairColor.R);
    }

    /// <summary>
    /// OnCollideV: a hard landing, running, plays runStumble -- and the game's own UpdateSprite,
    /// later the same frame, takes it straight back.
    /// </summary>
    /// <remarks>
    /// orig_Update decides onGround before it moves, and UpdateSprite after. A landing that
    /// collides partway through the move is still airborne by the frame's reckoning, so the
    /// fast-fall branch plays fallFast over the stumble (its LastAnimationID guard sees
    /// runStumble, not fallFast). A landing that only touches the floor collides on the next
    /// frame instead, by which time highestAirY has already been reset to the floor, so the
    /// stumble is never asked for. Both are the decompiled source, read line by line; the
    /// stumble the port used to hold on a 0.7s timer was its own.
    /// </remarks>
    static void CheckStumble(SpriteBank bank)
    {
        int stumbles = 0, shown = 0;
        for (float start = 60f; start < 70f; start += 0.25f)
        {
            var p = OnFloor(bank, 100f, start);
            var played = new List<string>();
            var onChange = p.Sprite.OnChange;
            p.Sprite.OnChange = (last, next) => { played.Add(next); onChange?.Invoke(last, next); };
            var input = new PetInput { MoveX = 1 };
            p.Speed = new PointF(150f, 0f);
            for (int i = 0; i < 90 && !p.onGround; i++)
            {
                played.Clear();
                Step(p, input);
                if (played.Contains("runStumble")) stumbles++;
                if (p.AnimId == "runStumble") shown++;
            }
        }
        Check($"a hard landing asks for runStumble ({stumbles} of 40 drops)", stumbles > 0);
        Check("and UpdateSprite takes it back the same frame", shown == 0);
    }
}
