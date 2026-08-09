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
| `AtlasChecks.cs` | Sprites read out of an installed Celeste match the dumped originals pixel for pixel, and every id the animations ask for resolves (skipped without an install) |
| `EntityDreamChecks.cs` | A crystal, jelly or seeker dropped inside a dream block is held there, while a window border still lets it go |
| `TheoChecks.cs` | How the Theo crystal breaks: the death sound, the forest-green burst, and being gone once it finishes |
| `DreamHyperChecks.cs` | The dream hyper, and the crouch on the way in that decides whether it lands as a hyper or a super |
| `BumperChecks.cs` | The pinball bumper: the launch it throws her into, the six tenths it then sits out, its reach, and everything else going straight through it |
| `PufferChecks.cs` | The pufferfish: bounced off the top, set off from below, the Theo crystal its blast throws, and its swim back to where it was left |
| `PushChecks.cs` | A window edge arriving where she stands: the push, the crush at every drag speed, where a crush puts her back, and the same for the crystal, the jelly and the seeker |
| `WaterChecks.cs` | Swimming, when the windows are water: the speeds, the surface, the dash refill, and what a held crystal does down there |
| `MoonChecks.cs` | Windows as moon blocks: the drift, the sink under whoever rides one, the shove from a dash, a home that survives being dragged, and her standing on and hanging off one |
| `KevinChecks.cs` | Windows as kevin blocks: the windup, the charge into another window or the desktop's edge, the crawl home along the return stack, the mid-flight turn, and the rebound she gets off an activated face |
| `InputChecks.cs` | Three keys and three buttons to an action: any one of them going down is a press, held siblings or not |
| `HairChecks.cs` | The hair table read out of the game's `Sprites.xml`, and which entries this port tunes away from it |
| `UpdateChecks.cs` | Whether the build server's newest build is one this copy does not have, including a build made from work that was never pushed |
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
