namespace DaveDiverCoopInstaller;

internal enum InstallMode
{
    Community,
    SteamInvite
}

internal static class InstallerBuild
{
#if STEAM_INVITE_SETUP
    internal const InstallMode Mode = InstallMode.SteamInvite;
#else
    internal const InstallMode Mode = InstallMode.Community;
#endif
}
