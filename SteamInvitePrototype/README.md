# DAVE THE DIVER Steam-invite co-op prototype

This is an experimental BepInEx IL2CPP plugin for Steam app `1868140`, build
`25315876`, and Unity `6000.0.52f1`. It provides Steam-lobby controls and
scene-presence sharing, plus a visible second diver during a shared Blue Hole dive.

## Existing community multiplayer release

The public [Coop-Divers project](https://github.com/S1ii/Coop-Divers) already
targets online play and advertises direct host/client sessions and shared dive
replication. Its MIT-licensed pre-release `v0.18.3-ci.26` is available from
[GitHub Releases](https://github.com/S1ii/Coop-Divers/releases/tag/v0.18.3-ci.26).
The release archive SHA-256 is
`ede121676daa1019d4368206f831e81de963463718c7907938ee4ab6e49d32d7`.

That release was installed in the isolated Steam test copy and BepInEx loaded
`Dave the Diver Multiplayer 0.18.3`. Startup also logged three Il2CppInterop
assembly-registration errors and a missing Harmony target warning; no lobby or
two-player session was tested. The project README explicitly warns that its UDP
traffic is unencrypted and unauthenticated. Use it only with trusted friends;
do not expose a public lobby.

## This prototype's status

This folder contains a separate Steam-lobby/SteamNetworkingMessages prototype.
It builds against the isolated game's generated interop assemblies. In-game,
the **Co-op Settings** panel appears as an overlay, including while the game's
Settings screen is open. This does not add controls to the game's native Settings
menu. Players can host or join a friends-only Steam lobby from any in-game scene.
The lobby remains active as players move between scenes; each member's current
scene and friend name are shown in the panel.

The plugin draws the other player's diver as a tinted copy of the local diver
sprite only when both players are in the same Blue Hole scene. At the boat and
sushi bar it reports the friend's scene, but does not render a second character
or synchronize restaurant, boat, story, or other world interactions.

The return-to-boat action is intercepted while a partner is connected. The
requesting player waits for the other player to accept or decline; accepting
causes both clients to invoke the game's normal return action. The vote uses
reliable SteamNetworkingMessages, but has not been verified in a two-client
session.

**Neither mod has been verified in a two-client session here.** This prototype has
not tested hosting/joining, live packet exchange, or an in-dive remote diver.
Treat it as a development build, not a release. It synchronizes only diver
position and facing: fish, items, oxygen, damage, enemies, map generation,
interactions, and other world state are not synchronized, so it does not create a
complete shared game world.

## Build

The project references BepInEx and generated game interop assemblies from the
isolated Steam test copy. The interop assemblies are local build inputs; do not
include or distribute them.

```powershell
dotnet build .\DaveTheDiverCoop.csproj -p:GamePath="C:\path\to\Steam-Test-Copy"
```

The resulting plugin is `bin\Debug\net6.0\DaveTheDiverCoop.dll`. Copy it to
`BepInEx\plugins` in the isolated test copy only. Do not install or test it in the
canonical Steam installation.

The source is MIT licensed; see [LICENSE](./LICENSE).

## Steam invite controls

Both players must launch the game and use the same game build and plugin. From
any in-game scene, the host selects **Host friends-only session**, then **Invite
Steam friend** to open Steam's friend-invite overlay. The guest accepts the
invite and opens the game world; the plugin joins the invitation once a gameplay
scene is active. Alternatively, the guest can join using the displayed Steam
lobby ID. The lobby is limited to two players and checks the game build and mod
protocol.

The invite flow does not automatically load the guest into the host's scene.
Players must navigate there themselves to see the synchronized diver. The invite
overlay, scene presence, and return vote have not been tested with two Steam
clients.

Both players need matching game builds and the same plugin. The isolated test copy
is intended for local development; actual multiplayer testing still requires a
second Steam client/player.

## Validation

- Verified the isolated copy as app `1868140`, build `25315876`, Unity
  `6000.0.52f1`, metadata `31.1`.
- Built against the generated IL2CPP interop assemblies with zero warnings/errors.
- Confirmed BepInEx discovered and loaded the plugin after switching from the
  unsupported connection-status callback to SteamNetworkingMessages.
- The final startup log contains no plugin, callback, or networking errors.
- Not yet verified: hosting/joining with two clients, lobby presence updates,
  return acceptance/decline, or seeing the remote diver during a dive.
- Also loaded the community `Coop-Divers` prerelease in the isolated copy. Its
  startup emitted the Il2CppInterop assembly-registration errors and Harmony
  warning noted above; it has not been tested with a second client.
- The canonical Steam installation was not used for plugin testing. The isolated
  copy was launched directly; the existing pre-test Steam save files were
  byte-checked after the smoke test.
