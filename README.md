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
running both plugins together. The main installer in release `v1.1.2` can switch
back to Coop-Divers and back up/disable the Steam-invite plugin.
The [Steam-invite prototype source archive](https://github.com/FireTva/dave-the-diver-coop/releases/latest/download/SteamInvitePrototype-source.zip)
is included with the release.

### Linux and Steam Deck (Proton)

The Linux downloader installs the Windows game files needed by Proton; it does
not install the game or configure Steam automatically. Close the game, then run
in a terminal:

```bash
curl -fL https://github.com/FireTva/dave-the-diver-coop/releases/latest/download/install-steam-invite-linux.sh \
  -o install-steam-invite-linux.sh
bash install-steam-invite-linux.sh
```

The script searches common Steam library locations. To choose a library
explicitly, pass the game folder:

```bash
bash install-steam-invite-linux.sh --game-dir "$HOME/.local/share/Steam/steamapps/common/DAVE THE DIVER"
```

After installation, set this game's Steam launch option to
`WINEDLLOVERRIDES="winhttp=n,b" %command%`. Install the mod on both players'
computers. The script requires `curl`, `sha256sum`, and `unzip`.

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
installed files. The Linux downloader applies the same BepInEx core build check,
preserves existing files, and backs up the previous co-op plugin before switching.

The v1.1.5 installer includes the .NET 6.0.7 runtime and the Microsoft.Extensions
assemblies required by BepInEx to start. It repairs an existing compatible
installation when the runtime or those support assemblies are missing, without
overwriting existing runtime files. The first game launch after installation can
take longer while BepInEx generates local IL2CPP interop files.

## Important limitations and safety

This is a community pre-release, not an official game patch. The Coop-Divers
README says its direct UDP traffic is unencrypted and unauthenticated. Use only
with trusted friends; do not expose a public lobby. The mod author's README lists
known unsupported situations and possible desynchronization.

The mod loaded in our isolated game smoke test, but that startup emitted
Il2CppInterop assembly-registration errors and a Harmony compatibility warning.
We have not verified a two-client connection, so this installer does not claim
that online co-op works end-to-end.

The separate Steam-invite prototype provides in-game host/join controls and a
friends-only Steam lobby. Its hideable Co-op Settings overlay uses a solid dark
background and is visible during gameplay and while the game's Settings screen
is open; it does not add buttons to the native Settings menu. Hosts can create
or join a lobby from the Blue Hole, boat, or sushi bar. A visual partner avatar
is attempted when both players are in the same supported scene. Boat and
sushi-bar avatars are movement proxies only: interactions and shared world or
story state are not synchronized. Returning from a dive while connected uses an
accept/decline request. Two-client gameplay has not been verified, so this
remains experimental.

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
- The repackaged BepInEx and .NET runtime payload embedded in this setup project's
  installer has SHA-256 `6f7b3edde53186d3ae3dbbfaa46704ef3192069687d701922e0063b0a25246c4`.
- The bundled .NET 6.0.7 x64 runtime was downloaded from Microsoft and verified
  against the official SHA-512
  `8124484bfe21f0cf7c2bb4b15992afc8cd9fdb676f7c3f36cb720c3f1459107b2bb8a5ced770fd40292a0c42f5fe24cb39bcb064d06e4f20fdbf11ef5dd55140`.
  Its `LICENSE.txt` and `ThirdPartyNotices.txt` are installed beside the runtime.
- The Coop-Divers release ZIP SHA-256 is
  `ede121676daa1019d4368206f831e81de963463718c7907938ee4ab6e49d32d7`.
- The Steam Deck/Linux downloader targets the Windows game running through
  Proton; set `WINEDLLOVERRIDES="winhttp=n,b" %command%` in Steam launch options.
- Dave the Diver and its game files/assets are not included.
