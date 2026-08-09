# CLAUDE.md

Working notes for coding agents. **The contribution rules live in `AGENTS.md` — read it
first.** It defines the exact-port requirement, why behavior must be emergent rather than
special-cased, which engine details are part of the port, and how to verify movement. This
file covers only how to build, run and check the project. A checkout that has never been
built is missing what `SETUP.md` makes.

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
  Not compiled: excluded in the csproj, and not in the repository — run
  `tools\dump-reference.ps1` to decompile your own. The authority for every gameplay question.
- `celeste_graphics_dump/` — original sprites, the authority for visuals. Not in the
  repository: run `tools\dump-graphics.ps1` to unpack your own from an install. Its layout
  mirrors the game's atlases exactly, so dump file, atlas path and index row are the same
  name in three dresses. `docs/celeste-assets.md` explains that and which folders the pet
  draws from; `docs/celeste-atlas-index.tsv` lists every sprite of every atlas with its frame
  and trim, used here or not, so art can be looked up without an install or the dump.
- `tests/DeskMadeline.Tests/` — frame-level checks for the ported gameplay; see
  `tests/README.md`. Excluded from the app's csproj.

## Build and run

```
dotnet build DeskMadeline.csproj -c Release      # must be 0 warnings, 0 errors
bin\Release\net10.0-windows\DeskMadeline.exe
```

Two builds come out of this tree, and the difference is only what travels with the exe:

| | ships | needs Celeste installed |
| --- | --- | --- |
| `-c Release` | nothing of Celeste's (3 MB) | yes, for both artwork and sound |
| `-c Release -p:BundleAssets=true` | its atlas, portrait and the four SFX banks, copied from the install named below (204 MB) | no |

The app needs no flag to tell them apart, and looks in the same two places for both kinds of
content: beside the exe first, then the install. `CelesteInstall.AtlasesDirectory` and
`SoundEffects` each do that. `CelesteAtlas` reads the atlas formats, both ported from
`celeste_reference/Monocle/`.

Where that install is, for a build, is `-p:CelestePath=…`, then `CELESTE_PATH`, then a
`celeste-path.txt` beside the project holding the path on one line — gitignored, since a path
from one machine means nothing on another, and a development file only: the app never reads
it. At run time an install is `CELESTE_PATH` or a setting, `CelestePath` in `settings.txt`,
which `PetWindow.ResolveCelesteInstall` fills in on the first run with whatever
`CelesteInstall` finds and only asks for, with a folder picker, when it finds nothing. The
tray menu's **Celeste folder…** changes it later; her sprites and the banks are read once at
startup, so it offers a restart.

`assets\` holds only what Celeste has no sprite for — the CommunalHelper elytra, the cat
bangs, a stand-in for a particle the game draws as a rectangle, and her portrait, which is
also the tray icon. It is laid over the atlas and ships in both builds.

Keep exactly one build output for the app (`bin\Release`). Do not add a second output
directory. The checks under `tests/` build to their own, which is theirs and not the app's.

**A running `DeskMadeline.exe` locks `bin\Release` and the build fails with MSB3027.**
Close it first. If the instance is the user's, ask before killing it — they may be
mid-test. Also stop any instance you start yourself, and verify it actually exited.

## Checking gameplay changes

```
dotnet run --project tests\DeskMadeline.Tests -c Release    # exits non-zero on failure
```

The checks drive the real `Player.Update` at a fixed 60Hz over synthetic `Solid`s and assert
vanilla's numbers. They compile the app's sources into their own assembly, so they reach
internals like `PetSettings`, and they never touch `bin\Release` — a running
`DeskMadeline.exe` cannot block them. See `tests/README.md` for what each file covers and
"Verifying movement" in `AGENTS.md` for the values and the input contract.

For anything touching `Player.cs`, add a check there rather than a throwaway harness, then
run the app as a smoke test: frame-level assertions and a smoke test together are the bar
for "verified".

When porting something new, most round trips are omissions: a reference method never read,
or set aside as "only drawing". Read the whole reference class — every override, `Render`
included — and port from a written member list before declaring anything done; "Read all of
the reference" in `AGENTS.md` is the rule and the cautionary tale. For anything that draws,
the smoke test is visual and stronger than a glance: burst-capture the entity on screen and
diff consecutive frames, since a check cannot see a sprite vibrating by a pixel or an edge
gone soft to resampling. When a visual bug is reported anyway, reproduce it with such an
instrument *before* changing code, and consider it fixed when the same instrument goes
quiet.

`Player` exposes most state publicly (`Pos`, `Speed`, `Ducking`, `Dashes`, `Stamina`,
`onGround`); read the few private timers with reflection rather than widening the API for
a test.

Sound is testable the same way, without a speaker: `Player.PlaySound` queues onto the public
`SoundEvents`, so a check can drive a move and assert exactly which events came out, in
order and how many times. `SFXCHECK=1` additionally plays every event once against the
installed Celeste to confirm the paths resolve. See "Sound" in `AGENTS.md`.

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
