# Dave the Diver Co-op Setup

A Windows x64 installer for the community [Coop-Divers](https://github.com/S1ii/Coop-Divers)
pre-release. This is an unofficial helper for the Dave the Diver co-op work tracked
in [coop-anything issue #42](https://github.com/cranebeaker/coop-anything/issues/42).

## Download and quick start

Get the [latest installer](https://github.com/FireTva/dave-the-diver-coop/releases/latest/download/DaveDiverCoopSetup.exe)
and its [SHA-256 checksum](https://github.com/FireTva/dave-the-diver-coop/releases/latest/download/DaveDiverCoopSetup.exe.sha256).
Run the installer on both PCs and select each player's Steam game folder. Both
players need the same Dave the Diver game build and co-op mod version. After
the first launch initializes BepInEx, close the game and reopen it to use the
title-screen Online menu.

For the separate experimental Steam-invite prototype, use
[DaveDiverSteamInviteSetup.exe](https://github.com/FireTva/dave-the-diver-coop/releases/latest/download/DaveDiverSteamInviteSetup.exe)
instead. It installs a different mod and backs up/disables Coop-Divers to avoid
running both plugins together. The main installer in release `v1.1.0` can switch
back to Coop-Divers and back up/disable the Steam-invite plugin.
The [Steam-invite prototype source archive](https://github.com/FireTva/dave-the-diver-coop/releases/latest/download/SteamInvitePrototype-source.zip)
is included with the release.

## What it installs

- BepInEx Unity IL2CPP Windows x64 `6.0.0-be.789` and its Doorstop loader. The
  framework runtime is bundled in the installer, repackaged from the official
  BepInEx build used in our isolated smoke test. The archive excludes game files
  and generated game interop assemblies.
- Coop-Divers `v0.18.3-ci.26`, downloaded from its GitHub release and checked
  against the published SHA-256 before installation.

The installer expects the Steam game folder containing `DaveTheDiver.exe`,
`GameAssembly.dll`, `UnityPlayer.dll`, and IL2CPP metadata. It never launches the
game and never modifies save files. On a clean game folder it installs BepInEx;
when an existing BepInEx installation is detected, it only proceeds if the
existing runtime is the tested `6.0.0-be.789` build. It will not overwrite another
loader version. Replaced copies of the mod DLL/license are backed up beside the
installed files.

The first game launch after installation can take longer while BepInEx generates
local IL2CPP interop files. The helper asks the user to start the game and close
it before reopening it to use the title-screen Online menu.

## Important limitations and safety

This is a community pre-release, not an official game patch. The Coop-Divers
README says its direct UDP traffic is unencrypted and unauthenticated. Use only
with trusted friends; do not expose a public lobby. The mod author's README lists
known unsupported situations and possible desynchronization.

The mod loaded in our isolated game smoke test, but that startup emitted
Il2CppInterop assembly-registration errors and a Harmony compatibility warning.
We have not verified a two-client connection, so this installer does not claim
that online co-op works end-to-end.

The separate Steam-invite prototype opens Steam's friend-invite overlay from a
friends-only lobby and accepts Steam lobby invites. Both players still need to
enter the same Blue Hole dive; only diver position and facing are synchronized.
It has also not been tested with two clients and is not a complete shared-world
co-op implementation.

Do not install on a game copy that is currently running. This helper does not
touch saves, create firewall rules, configure routers, or alter Steam Cloud.

## Build and verify

Requires the .NET 8 SDK on Windows:

```powershell
dotnet build .\DaveDiverCoopInstaller.csproj -c Release
dotnet run --project .\DaveDiverCoopInstaller.csproj -c Release -- --verify
dotnet build .\DaveDiverSteamInviteSetup.csproj -c Release
dotnet run --project .\DaveDiverSteamInviteSetup.csproj -c Release -- --verify
dotnet publish .\DaveDiverCoopInstaller.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

The published single-file apps are `publish\DaveDiverCoopSetup.exe` and
`publish\DaveDiverSteamInviteSetup.exe`. `--verify` checks the selected installer
payload, BepInEx extraction, and reversible plugin-switch behavior without
installing into the game folder.

## Sources and licenses

- [Coop-Divers source and release](https://github.com/S1ii/Coop-Divers)
- [BepInEx source and bleeding-edge builds](https://github.com/BepInEx/BepInEx)
- BepInEx is LGPL-2.1; the license text is in
  [THIRD-PARTY-LICENSE-LGPL-2.1.txt](./THIRD-PARTY-LICENSE-LGPL-2.1.txt) and is
  also installed beside BepInEx.
- The bundled framework files came from official BepInEx build `6.0.0-be.789`,
  commit `6cea286d204eb3754d3b8363a5a1fd6640164a49`. The original downloaded
  archive SHA-256 was
  `29519614d0121a3eecf9df6d61436f0c09a2c5f85b0a7d92559a135ce3055eb1`.
- The repackaged BepInEx runtime embedded in this setup project's payload has
  SHA-256 `82d0e910d6c6a4c6099cc55c1ac473edef22697a333e500445524ee48bdc4f83`.
- The Coop-Divers release ZIP SHA-256 is
  `ede121676daa1019d4368206f831e81de963463718c7907938ee4ab6e49d32d7`.
- Dave the Diver and its game files/assets are not included.
