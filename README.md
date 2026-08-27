# Desk Madeline

A Madeline pet for Windows, recreating the physics of the game [Celeste](https://www.celestegame.com/) on the desktop. You can control Madeline to interact with your windows and spawn entities.

# Download

Download the latest build from [Release](https://github.com/solstice23/desk-madeline/releases/tag/nightly).
You can also check for updates in the pet's tray menu.

You need to own a copy of [Celeste](https://www.celestegame.com/) and have it locally installed to let the pet run, as it uses the game's assets on the fly.

Her sounds need one thing more: Celeste's 64-bit FMOD, which comes with [Everest](https://everestapi.github.io/)'s build of the game. The plain install carries the 32-bit one, which a 64-bit pet cannot load. On such a copy she offers, once, to fetch those two libraries herself -- about 1 MB out of Everest's release, kept beside the pet, with your Celeste folder left alone -- and the offer stays in the tray menu under Sound effects. Decline it and she runs and looks exactly the same, silently.

# Building

To build, you need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then

```
dotnet build DeskMadeline.csproj -c Release
bin\Release\net10.0-windows\DeskMadeline.exe
```

The ordinary build requires a Celeste copy to run. The CI only has the ordinary build.

To build a standalone version, you need to specify a Celeste install path and bundle the assets:

```
dotnet build DeskMadeline.csproj -c Release -p:BundleAssets=true -p:CelestePath="C:\path\to\Celeste"
bin\Release\net10.0-windows\DeskMadeline.exe
```

Alternatively, you can make a `celeste-path.txt` in the project root to permanently specify the path.

# Development

Read `SETUP.md`.

For Agents, also see `AGENTS.md` and `CLAUDE.md`.


# Credits

This project was originally created by [Eisyiah](https://space.bilibili.com/3493085122136852). This fork is an enhancement and continuation of the original project, focusing on matching the original game's behavior and adding new features.

This is a fan project, not affiliated with the developers of Celeste. Celeste is by [Extremely OK Games](https://exok.com), and none of its art, audio or code is included in the source code. 

This project references third-party mods: the Elytra from [CommunalHelper](https://gamebanana.com/mods/53697), and the cat ears and tail from [Cateline](https://gamebanana.com/mods/251793) by ladyfey. See `THIRD_PARTY_NOTICES.md` for third-party details and licenses.

