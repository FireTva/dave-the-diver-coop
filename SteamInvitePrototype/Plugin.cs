using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace DaveTheDiverCoop;

[BepInPlugin("coop-anything.davethediver", "DAVE THE DIVER Co-op", "0.2.0")]
public sealed class Plugin : BasePlugin
{
    public override void Load()
    {
        new Harmony("coop-anything.davethediver").PatchAll(typeof(Plugin).Assembly);
        AddComponent<CoopBehaviour>();
        Log.LogInfo("DAVE THE DIVER Co-op prototype and return vote loaded.");
    }
}

[HarmonyPatch(typeof(PauseMenuPanel), nameof(PauseMenuPanel.OnToLobby))]
internal static class ReturnToBoatHarmonyPatch
{
    private static bool Prefix(PauseMenuPanel __instance)
    {
        return CoopBehaviour.OnVanillaReturnToBoat(__instance);
    }
}
