# Celeste's assets, and how this project reaches them

Nothing of Celeste's ships with the ordinary build. Artwork is read from the game's atlases
and sound from its FMOD banks, both at startup, both from an installed copy found by
`CelesteInstall`. `-p:BundleAssets=true` copies them beside the exe instead, for a machine
with no Celeste on it. See `CLAUDE.md` for the two builds.

An ordinary Steam install is found by looking, and the first run writes the result to
`CelestePath` in `settings.txt` — the setting is what a copy somewhere unexpected, or one of
several, is named in, whether by the folder picker the first run puts up when looking finds
nothing, by the tray menu's **Celeste folder…** afterwards, or by hand. `CELESTE_PATH`
overrides it. A build takes the same from `-p:CelestePath=…`, `CELESTE_PATH` or
`celeste-path.txt`, the last of which is a development file the app itself never reads.

`assets\` holds the fourteen files the game has no sprite for: `fly00`–`fly08`, the elytra
from CommunalHelper; `catbangs00`–`02`; `dashParticle`, standing in for a particle Celeste
draws as a plain rectangle; and `portrait.png`, which is her dialogue portrait and also the
tray icon. Everything else it used to hold — 1028 files — is read from the game now.

## Unpacking the art to look at it

`celeste_graphics_dump/` is too large for the repository, so make your own:

```
tools\dump-graphics.ps1                                   # into celeste_graphics_dump
tools\dump-graphics.ps1 -Destination D:\scratch\art       # or anywhere
```

It writes one png per sprite for all 8371 of them, in the layout below. Nothing needs it —
the app reads the atlases directly and the checks skip when it is absent — but it is the
quickest way to see what a sprite actually looks like. Point the checks at a fresh one with
`DUMPROOT` to confirm it came out right.

## The dump, the atlas and the index all line up

`celeste_graphics_dump/` is an unpacked copy of the atlases, one png per sprite, and the
correspondence is one to one in both directions — checked, not assumed. Every one of the
**8371** pngs is a sprite of the game, every one of the **8371** sprites is a png, across the
same **22** atlases, with nothing left over on either side. `AtlasChecks` verifies it, so a
game update that adds art the dump has never seen would show up as a failure rather than as a
puzzle later.

The rule is a plain translation of the path, which means no lookup table is needed:

```
celeste_graphics_dump/Graphics/Atlases/<atlas>/<path>.png   <->   atlas <atlas>, sprite <path>
```

So the three names for one sprite are mechanical translations of each other:

| | |
| --- | --- |
| dump file | `celeste_graphics_dump/Graphics/Atlases/Gameplay/characters/player/idle00.png` |
| atlas | `Gameplay`, sprite path `characters/player/idle00` |
| index row | `Gameplay⇥characters/player/idle00⇥32⇥32⇥8⇥20⇥13⇥12` |

`celeste-atlas-index.tsv` beside this file lists all 8371 sprites of all 22 atlases, whether
this project uses them or not, so a change can find what art exists — and how big it is —
without an install, without the dump, and without unpacking anything. Columns are the atlas,
the sprite path, the untrimmed frame the game draws into, and the trimmed region actually
stored: `frameW frameH trimX trimY trimW trimH`.

The trim is worth understanding before using a sprite. The packer cuts transparent edges away
and records where the remainder belongs, so `characters/player/idle00` is a 32×32 frame
holding 13×12 pixels of Madeline at (8,20). Anything that draws by frame, as the port does,
wants the frame size; anything that measures the art itself wants the trim.

Regenerate the index after a game update with:

```
set ATLASINDEX=1 && dotnet run --project tests\DeskMadeline.Tests -c Release
```

## Which folders the pet draws from

`Sprites` flattens atlas folders into the ids the rest of the code asks for, which are the
names `assets\` used to hold:

| atlas folder | sprite id | count |
| --- | --- | --- |
| `characters/player/` | the name alone — `idle00` | 579 |
| `characters/player_badeline/` | the name alone, when the Badeline skin is on | 543 |
| `objects/glider/` | `glider/` + name | 24 |
| `characters/monsters/` | `seeker/` + name | 182 |
| `characters/theoCrystal/` | `theoCrystal/` + name | 19 |
| `pico8/` | `pico8/` + name | 4 |

Celeste keeps a few of the player's animations in folders of their own, and `assets\`
flattened those into the folder name followed by the frame, which `Sprites` reproduces:
`characters/player/sweat/climb00` is `sweatClimb00`, and `characters/player/wakeUp/00` is
`wakeUp00`.

Skins in `skins\` are the user's own and are read from disk in either build.

## Reading the formats

`CelesteAtlas` ports both from `celeste_reference/Monocle/`:

- **`.meta`**, from `Atlas.ReadAtlasData`'s `Packer` case: a header, then per page a name and a
  run of sprites, each a path and eight `short`s. The trim offset is stored negated, and
  Monocle un-negates it on the way in; so does this.
- **`.data`**, from `VirtualTexture`'s `".data"` case: width, height, a flag for whether alpha
  is stored, then runs of a length byte, an alpha byte, and the colour as B, G, R — which is
  already the byte order a GDI+ 32bpp bitmap wants, so the colour copies straight across. A
  run with zero alpha stores no colour at all.

`AtlasChecks` compares sprites read this way against the dumped originals pixel for pixel,
which is what a change to either reader should keep passing.
