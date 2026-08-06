# Setting up

Needs Windows, the .NET 8 SDK, and Celeste installed. Then, from the repository root:

```powershell
Set-Content celeste-path.txt 'D:\SteamLibrary\steamapps\common\Celeste'  # your install
tools\dump-reference.ps1                                    # celeste_reference\  (~30s)
tools\dump-graphics.ps1                                     # celeste_graphics_dump\ (optional)
dotnet build DeskMadeline.csproj -c Release                 # must be 0 warnings, 0 errors
dotnet run --project tests\DeskMadeline.Tests -c Release    # must pass
bin\Release\net8.0-windows\DeskMadeline.exe
```

Three of those are Celeste's own files, so none of them is in git and each is made locally:

| | what it is | needed |
| --- | --- | --- |
| `celeste-path.txt` | the install folder, one line | for the scripts and `-p:BundleAssets=true`; the app finds it by itself at run time |
| `celeste_reference/` | the game decompiled, `Celeste/` and `Monocle/` | yes — every gameplay question is answered from it, never from memory |
| `celeste_graphics_dump/` | one png per sprite, a few hundred MB | no — the app and the checks read the atlases directly |

Then read **`AGENTS.md`** for the rules the code is written under, and **`CLAUDE.md`** for
building, running and checking. `docs/celeste-assets.md` covers where the artwork comes from.
