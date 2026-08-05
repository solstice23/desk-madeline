# Checks

Frame-level checks for the ported gameplay. They drive the real `Player.Update` at a fixed
60Hz over synthetic `Solid`s and assert vanilla's numbers, which is the bar `AGENTS.md` sets
for anything touching movement.

```
dotnet run --project tests\DeskMadeline.Tests -c Release
```

Exits non-zero if any check fails. Add `SFXCHECK=1` to also play every sound event once
against an installed Celeste — it needs speakers, so it is opt-in and skipped otherwise.

There is no test framework here on purpose: a framework would add a dependency without
adding an assertion, and what these need is a fixed timestep and vanilla's constants.

## What is covered

| File | Checks |
| --- | --- |
| `GridChecks.cs` | Whole-pixel grid: climbing a wall at every window/drag offset, window borders surviving the snap at every scale, landing on a window top, respawn after a Seeker death landing on whole pixels |
| `DreamChecks.cs` | Dream blocks: death position on straight and diagonal dashes, the display-edge rule on both axes, assist mode bouncing instead of dying, being held silent and still inside a block |
| `SoundChecks.cs` | Super and hyper at each input timing: one dash sound, the jump, and its super or superslide layer |
| `DreamHyperChecks.cs` | The dream hyper, and the crouch on the way in that decides whether it lands as a hyper or a super |
| `SettingsChecks.cs` | Settings defaults on a fresh install, and an existing `settings.txt` still winning |
| `SnapChecks.cs` | Snapping back onto the displays after a drag, including seams between mismatched monitors and each wrap axis |
| `SoundBankChecks.cs` | Every event the port plays resolves in Celeste's banks (opt-in, see above) |

## How it builds

The project compiles the app's own sources into its own assembly rather than referencing
`DeskMadeline.csproj`:

- internals like `PetSettings` and `SoundEffects` are reachable from a check;
- building the checks never writes to `bin\Release`, so a running `DeskMadeline.exe` cannot
  block them, and they cannot disturb it.

`DeskMadeline.csproj` excludes `tests\**` for the same reason in reverse — otherwise the app
would compile the checks and find two entry points.
