# CLAUDE.md

Working notes for coding agents. **The contribution rules live in `AGENTS.md` — read it
first.** It defines the exact-port requirement, why behavior must be emergent rather than
special-cased, which engine details are part of the port, and how to verify movement. This
file covers only how to build, run and check the project.

## What this is

A Windows desktop pet: Celeste's Madeline running on a layered transparent window, with
real window rectangles as platforms. Gameplay is an exact port of Celeste; the desktop
shell around it (window platforms, tray menu, focus gating, persistence, skins) is not.

- `Player.cs` — the ported player: physics, state machine, hair. The heart of the project.
- `PetWindow.cs` — desktop shell: window polling, render loop, input sampling, tray menu.
- `Glider.cs`, `TheoCrystal.cs`, `Seeker.cs` — ported entities.
- `KeyBindings.cs` / `PadBindings.cs` + `XInputPad.cs` — keyboard and controller bindings.
- `Localization.cs` — in-code string catalogs; every key must exist in every language.
- `celeste_reference/` — decompiled Celeste (`Celeste/`) and engine (`Monocle/`) source.
  Not compiled: excluded in the csproj. This is the authority for every gameplay question.
- `celeste_graphics_dump/` — original sprites, the authority for visuals.

## Build and run

```
dotnet build DeskMadeline.csproj -c Release      # must be 0 warnings, 0 errors
bin\Release\net8.0-windows\DeskMadeline.exe
```

Keep exactly one build output (`bin\Release`). Do not add a second output directory.

**A running `DeskMadeline.exe` locks `bin\Release` and the build fails with MSB3027.**
Close it first. If the instance is the user's, ask before killing it — they may be
mid-test. Also stop any instance you start yourself, and verify it actually exited.

## Checking movement changes

There is no test project. For anything touching `Player.cs`, build a throwaway harness in
the scratchpad that references `DeskMadeline.csproj`, drives `Player.Update` at a fixed
60Hz over synthetic `Solid`s, and asserts vanilla's numbers. See "Verifying movement" in
`AGENTS.md` for the values and the input contract. Run the app afterwards as a smoke test;
frame-level assertions and a smoke test together are the bar for "verified".

`Player` exposes most state publicly (`Pos`, `Speed`, `Ducking`, `Dashes`, `Stamina`,
`onGround`); read the few private timers with reflection rather than widening the API for
a test.

## Cross-checking against the reference

Read the port and `celeste_reference/` side by side — do not work from memory of how
Celeste behaves. Useful entry points:

- `celeste_reference/Celeste/Player.cs` — `orig_Update`, `NormalUpdate`, `ClimbUpdate`,
  `DashUpdate`, `DashCoroutine`, `OnCollideH`/`OnCollideV`, the jump family.
- `celeste_reference/Celeste/Settings.cs` — `SetDefaultKeyboardControls`,
  `SetDefaultButtonControls`.
- `celeste_reference/Celeste/Input.cs` — which accessor reads a binding at which deadzone.
- `celeste_reference/Monocle/` — `StateMachine.cs`, `Coroutine.cs`, `MInput.cs`,
  `Binding.cs`. Frame timing and input semantics come from here.

When the reference cannot answer a question (this usually shouldn't happen. if the source of a part is not available locally, it's somewhere online), leave that part unbound or disabled and say so — do not
guess. Record any desktop-specific adaptation in a comment beside the code.
