# Celeste's assets, and how this project reaches them

Nothing of Celeste's ships with the ordinary build. Artwork is read from the game's atlases
and sound from its FMOD banks, both at startup, both from an installed copy found by
`CelesteInstall` (or `CELESTE_PATH`). `-p:BundleAssets=true` copies them beside the exe
instead, for a machine with no Celeste on it. See `CLAUDE.md` for the two builds.

## The dump, the atlas and the index all line up

`celeste_graphics_dump/` is an unpacked copy of the atlases, and its layout mirrors them
exactly, so the three names for one sprite are mechanical translations of each other:

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
