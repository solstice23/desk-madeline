using System;
using System.Collections.Generic;
using System.IO;
using DeskMadeline;

// Bindings: three keys and three buttons per action, and what counts as a press.
//
// Monocle's Binding.Pressed asks every key bound to an action for its own edge and takes any
// one of them -- so holding one of the keys bound to jump and pressing another is a jump. Read
// as a single button ("is anything bound to this down?") that press disappears, because the
// button was already down. That is what these are here to keep fixed.
static class InputChecks
{
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    const int KeyA = 0x41, KeyB = 0x42, KeyC = 0x43;

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("BINDINGS: more than one key on the same action");
        Console.WriteLine(new string('=', 74));

        string path = Path.Combine(Path.GetTempPath(), "deskmadeline-bindings-check.txt");
        if (File.Exists(path)) File.Delete(path);
        var bindings = new KeyBindings(path);

        // A keyboard this can drive, in place of the one the desktop has.
        var held = new HashSet<int>();
        bindings.ReadKey = key => held.Contains(key);

        bindings.Set(PetAction.Jump, 0, KeyA);
        bindings.Set(PetAction.Jump, 1, KeyB);
        bindings.Set(PetAction.Jump, 2, 0);

        Console.WriteLine("  Two keys bound to jump");
        bindings.Poll();
        Check("nothing held is nothing pressed", !bindings.Pressed(PetAction.Jump));

        held.Add(KeyA);
        bindings.Poll();
        Check("the first going down is a press", bindings.Pressed(PetAction.Jump));
        Check("and the binding reads as held", bindings.IsDown(PetAction.Jump));

        bindings.Poll();
        Check("holding it is not a press again", !bindings.Pressed(PetAction.Jump));

        // The report: the first is still down, and the second arrives.
        held.Add(KeyB);
        bindings.Poll();
        Check("the second going down while the first is held is a press",
            bindings.Pressed(PetAction.Jump));

        bindings.Poll();
        Check("and holding both is not a press", !bindings.Pressed(PetAction.Jump));

        // Letting one go leaves the action held, and pressing it again is another press --
        // which reading the binding as one button also loses.
        held.Remove(KeyB);
        bindings.Poll();
        Check("letting the second go leaves it held by the first",
            bindings.IsDown(PetAction.Jump) && !bindings.Pressed(PetAction.Jump));
        held.Add(KeyB);
        bindings.Poll();
        Check("and pressing it again is a press", bindings.Pressed(PetAction.Jump));

        held.Clear();
        bindings.Poll();
        Check("with both let go it is neither down nor pressed",
            !bindings.IsDown(PetAction.Jump) && !bindings.Pressed(PetAction.Jump));

        Console.WriteLine();
        Console.WriteLine("  Keys held through a frame the pet was not listening");
        // Poll runs whether or not the pet is reading its input, so a key held down while
        // typing somewhere else is a key held rather than a fresh press on the way back.
        held.Add(KeyA);
        for (int i = 0; i < 5; i++) bindings.Poll();      // as if unfocused all the while
        Check("a key held across those frames is not pressed on the last of them",
            !bindings.Pressed(PetAction.Jump) && bindings.IsDown(PetAction.Jump));

        Console.WriteLine();
        Console.WriteLine("  One key on two actions");
        // Bindings are separate, so a key on two actions presses both -- Celeste asks each
        // binding for itself.
        held.Clear();
        bindings.Set(PetAction.Dash, 0, KeyC);
        bindings.Set(PetAction.Grab, 0, KeyC);
        bindings.Poll();
        held.Add(KeyC);
        bindings.Poll();
        Check("both actions see the press",
            bindings.Pressed(PetAction.Dash) && bindings.Pressed(PetAction.Grab));

        Console.WriteLine();
        Console.WriteLine("  Both directions held at once");
        // VirtualIntegerAxis at TakeNewer, which is what Celeste leaves MoveX on: the key that
        // arrived last wins, so pressing left without letting go of right turns her round.
        var axis = new IntegerAxis();
        Check("right alone is right", axis.Update(false, true) == 1);
        Check("holding it stays right", axis.Update(false, true) == 1);
        Check("left, without letting go of right, is left", axis.Update(true, true) == -1);
        Check("and it stays left while both are held", axis.Update(true, true) == -1);
        Check("letting go of left leaves her going right again", axis.Update(false, true) == 1);
        Check("pressing left again turns her round again", axis.Update(true, true) == -1);
        Check("letting go of right leaves her going left", axis.Update(true, false) == -1);
        Check("and letting go of both stops her", axis.Update(false, false) == 0);

        // Vanilla's oddity, kept: both arriving on one frame turn a zero around, which is zero.
        var together = new IntegerAxis();
        Check("both on the same frame from a standstill is a standstill",
            together.Update(true, true) == 0);
        Check("and stays one until a key is let go", together.Update(true, true) == 0);
        Check("letting go of one starts her the other way", together.Update(true, false) == -1);

        Console.WriteLine();
        Console.WriteLine("  The same on a controller");
        var pad = new PadBindings(Path.Combine(Path.GetTempPath(),
            "deskmadeline-pad-check.txt"));
        pad.Set(PetAction.Jump, 0, PadButton.A);
        pad.Set(PetAction.Jump, 1, PadButton.B);
        pad.Set(PetAction.Jump, 2, PadButton.None);
        PadState With(ushort held) => new PadState(true, held, 0f, 0f, 0f, 0f, 0f, 0f);
        var none = With(0);
        var aDown = With(XInputPad.GamepadA);
        var bothDown = With((ushort)(XInputPad.GamepadA | XInputPad.GamepadB));
        Check("a button going down is a press",
            pad.Pressed(aDown, none, PetAction.Jump, PadBindings.ButtonThreshold));
        Check("holding it is not",
            !pad.Pressed(aDown, aDown, PetAction.Jump, PadBindings.ButtonThreshold));
        Check("the second going down while the first is held is a press",
            pad.Pressed(bothDown, aDown, PetAction.Jump, PadBindings.ButtonThreshold));
        Check("and holding both is not",
            !pad.Pressed(bothDown, bothDown, PetAction.Jump, PadBindings.ButtonThreshold));
        // A pad reports nothing held while it is disconnected, so a button already down when it
        // arrives reads as a press. That is what XNA hands Monocle, and what the game does.
        Check("a button held as a pad is plugged in reads as a press",
            pad.Pressed(aDown, default, PetAction.Jump, PadBindings.ButtonThreshold));

        if (File.Exists(path)) File.Delete(path);
        return failed;
    }
}
