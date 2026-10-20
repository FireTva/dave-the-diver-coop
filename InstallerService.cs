using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace DaveDiverCoopInstaller;

internal static class InstallerService
{
    private const string BepInExResourceName =
        "DaveDiverCoopInstaller.BepInEx-Unity-IL2CPP-Win-x64-be.789.zip";
    private const string BepInExSha256 =
        "82d0e910d6c6a4c6099cc55c1ac473edef22697a333e500445524ee48bdc4f83";
    private const string SteamInviteResourceName =
        "DaveDiverCoopInstaller.DaveTheDiverCoop.dll";
    private const string SteamInviteSha256 =
        "8ebdcf5e860611d0b85d835e8cf0dfd8aee00faee0f2a9e4721f0f09c818955f";
    private const string LicenseResourceName = "DaveDiverCoopInstaller.LICENSE";
    private const string ModDownloadUrl =
        "https://github.com/S1ii/Coop-Divers/releases/download/v0.18.3-ci.26/DaveTheDiverMP-0.18.3-ci.26.zip";
    private const string ModSha256 =
        "ede121676daa1019d4368206f831e81de963463718c7907938ee4ab6e49d32d7";
    private const string ModDllEntry =
        "BepInEx/plugins/DaveTheDiverMP/DaveTheDiverMP.dll";
    private const string ModLicenseEntry =
        "BepInEx/plugins/DaveTheDiverMP/LICENSE.txt";
    private const string SteamInvitePluginPath =
        "DaveTheDiverCoop/DaveTheDiverCoop.dll";
    private const string CommunityPluginPath =
        "DaveTheDiverMP/DaveTheDiverMP.dll";
    private const long MaxBepInExUncompressedBytes = 64 * 1024 * 1024;
    private const int MaxModArchiveBytes = 4 * 1024 * 1024;
    private const int MaxPluginBytes = 1024 * 1024;

    private static readonly HttpClient HttpClient = CreateHttpClient();

    internal static async Task VerifyPackagesAsync(InstallMode mode)
    {
        byte[] bepInEx = await ReadBepInExPayloadAsync();
        VerifySha256(bepInEx, BepInExSha256, "BepInEx payload");
        await VerifyBepInExInstallAsync();

        if (mode == InstallMode.Community)
        {
            byte[] mod = await DownloadModAsync();
            _ = ReadModPayload(mod);
            return;
        }

        byte[] plugin = await ReadSteamInvitePluginAsync();
        VerifySha256(plugin, SteamInviteSha256, "Steam-invite plugin");
        byte[] license = await ReadLicenseAsync();
        await VerifySteamInviteInstallAsync(plugin, license);
    }

    internal static void ValidateGameFolder(string gamePath)
    {
        if (!Directory.Exists(gamePath))
        {
            throw new DirectoryNotFoundException("The selected game folder does not exist.");
        }

        string[] requiredFiles =
        {
            "DaveTheDiver.exe",
            "GameAssembly.dll",
            "UnityPlayer.dll",
            Path.Combine("DaveTheDiver_Data", "il2cpp_data", "Metadata", "global-metadata.dat")
        };

        string missing = requiredFiles.FirstOrDefault(file => !File.Exists(Path.Combine(gamePath, file)))
            ?? string.Empty;
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"This does not look like a complete Steam DAVE THE DIVER folder. Missing: {missing}");
        }
    }

    internal static bool HasBepInExFiles(string gamePath)
    {
        return File.Exists(Path.Combine(gamePath, "winhttp.dll"))
            || File.Exists(Path.Combine(gamePath, "doorstop_config.ini"))
            || Directory.Exists(Path.Combine(gamePath, "BepInEx"));
    }

    internal static void ValidateExistingBepInEx(string gamePath)
    {
        string corePath = Path.Combine(gamePath, "BepInEx", "core", "BepInEx.Core.dll");
        string il2CppPath = Path.Combine(gamePath, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll");
        string loaderPath = Path.Combine(gamePath, "winhttp.dll");
        string configPath = Path.Combine(gamePath, "doorstop_config.ini");

        if (!File.Exists(corePath)
            || !File.Exists(il2CppPath)
            || !File.Exists(loaderPath)
            || !File.Exists(configPath))
        {
            throw new InvalidOperationException(
                "A partial or different mod-loader installation already exists. Back it up and install or repair BepInEx 6.0.0-be.789 manually before running this installer.");
        }

        string? version = FileVersionInfo.GetVersionInfo(corePath).ProductVersion;
        if (version?.Contains("6.0.0-be.789", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidOperationException(
                $"Found BepInEx version '{version ?? "unknown"}'. This installer only accepts the tested BepInEx 6.0.0-be.789 build and will not overwrite an existing loader.");
        }
    }

    internal static async Task InstallBepInExAsync(string gamePath)
    {
        byte[] archiveBytes = await ReadBepInExPayloadAsync();
        VerifySha256(archiveBytes, BepInExSha256, "BepInEx payload");

        if (HasBepInExFiles(gamePath))
        {
            throw new InvalidOperationException("BepInEx files appeared in the game folder during installation. No files were overwritten.");
        }

        string staging = Path.Combine(Path.GetTempPath(), "DaveDiverCoopSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();

        try
        {
            using var memory = new MemoryStream(archiveBytes, writable: false);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
            long totalLength = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                    || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    continue;
                }

                totalLength += entry.Length;
                if (totalLength > MaxBepInExUncompressedBytes)
                {
                    throw new InvalidDataException("BepInEx payload exceeded the expected extraction size.");
                }

                string destination = GetSafeDestination(staging, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using Stream input = entry.Open();
                await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output);
            }

            string[] required =
            {
                Path.Combine("BepInEx", "core", "BepInEx.Core.dll"),
                Path.Combine("BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"),
                "winhttp.dll",
                "doorstop_config.ini"
            };
            foreach (string file in required)
            {
                if (!File.Exists(Path.Combine(staging, file)))
                {
                    throw new InvalidDataException($"BepInEx payload is missing {file}.");
                }
            }

            foreach (string stagedFile in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(staging, stagedFile);
                string destination = Path.Combine(gamePath, relativePath);
                EnsureDirectoryTracked(Path.GetDirectoryName(destination)!, createdDirectories);
                if (File.Exists(destination))
                {
                    throw new IOException($"Refusing to overwrite existing file: {relativePath}");
                }

                File.Copy(stagedFile, destination);
                createdFiles.Add(destination);
            }
        }
        catch
        {
            foreach (string file in createdFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            foreach (string directory in createdDirectories.AsEnumerable().Reverse())
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: false);
                }
            }

            throw;
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    internal static async Task<string?> InstallModAsync(string gamePath, InstallMode mode)
    {
        if (mode == InstallMode.Community)
        {
            ModPayload payload = ReadModPayload(await DownloadModAsync());
            return await InstallPluginFilesAsync(
                gamePath,
                InstallMode.Community,
                payload.Plugin,
                payload.License);
        }

        byte[] plugin = await ReadSteamInvitePluginAsync();
        VerifySha256(plugin, SteamInviteSha256, "Steam-invite plugin");
        return await InstallPluginFilesAsync(
            gamePath,
            InstallMode.SteamInvite,
            plugin,
            await ReadLicenseAsync());
    }

    private static async Task<string?> InstallPluginFilesAsync(
        string gamePath,
        InstallMode mode,
        byte[] plugin,
        byte[] license)
    {
        string pluginsDirectory = Path.Combine(gamePath, "BepInEx", "plugins");
        string targetDirectory = Path.Combine(
            pluginsDirectory,
            mode == InstallMode.SteamInvite ? "DaveTheDiverCoop" : "DaveTheDiverMP");
        string targetDll = Path.Combine(
            targetDirectory,
            mode == InstallMode.SteamInvite ? "DaveTheDiverCoop.dll" : "DaveTheDiverMP.dll");
        string targetLicense = Path.Combine(targetDirectory, "LICENSE.txt");
        string conflictingDll = Path.Combine(
            pluginsDirectory,
            mode == InstallMode.SteamInvite ? CommunityPluginPath : SteamInvitePluginPath);

        Directory.CreateDirectory(targetDirectory);
        string? disabledPlugin = null;
        string? dllBackup = null;
        string? licenseBackup = null;
        bool dllWriteAttempted = false;
        bool licenseWriteAttempted = false;

        try
        {
            disabledPlugin = DisableExistingPlugin(conflictingDll);
            dllBackup = BackupExistingFile(targetDll);
            licenseBackup = BackupExistingFile(targetLicense);
            dllWriteAttempted = true;
            await WriteAtomicallyAsync(targetDll, plugin);
            licenseWriteAttempted = true;
            await WriteAtomicallyAsync(targetLicense, license);
            return disabledPlugin;
        }
        catch (Exception installException)
        {
            var rollbackExceptions = new List<Exception>();
            if (dllWriteAttempted)
            {
                RestoreBackup(targetDll, dllBackup, rollbackExceptions);
            }
            if (licenseWriteAttempted)
            {
                RestoreBackup(targetLicense, licenseBackup, rollbackExceptions);
            }
            RestoreDisabledPlugin(conflictingDll, disabledPlugin, rollbackExceptions);

            if (rollbackExceptions.Count > 0)
            {
                rollbackExceptions.Insert(0, installException);
                throw new AggregateException(
                    "Plugin installation failed and rollback was incomplete.",
                    rollbackExceptions);
            }

            throw;
        }
    }

    private static string? DisableExistingPlugin(string pluginPath)
    {
        if (!File.Exists(pluginPath))
        {
            return null;
        }

        string disabledPath = pluginPath + ".disabled-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        if (File.Exists(disabledPath))
        {
            disabledPath += "-" + Guid.NewGuid().ToString("N");
        }

        File.Move(pluginPath, disabledPath);
        return disabledPath;
    }

    private static void RestoreDisabledPlugin(
        string originalPath,
        string? disabledPath,
        List<Exception> rollbackExceptions)
    {
        if (disabledPath is null || !File.Exists(disabledPath))
        {
            return;
        }

        try
        {
            if (!File.Exists(originalPath))
            {
                File.Move(disabledPath, originalPath);
            }
        }
        catch (Exception exception)
        {
            rollbackExceptions.Add(exception);
        }
    }

    private static async Task<byte[]> ReadBepInExPayloadAsync()
    {
        return await ReadEmbeddedResourceAsync(BepInExResourceName, MaxBepInExUncompressedBytes);
    }

    private static async Task<byte[]> ReadSteamInvitePluginAsync()
    {
        return await ReadEmbeddedResourceAsync(SteamInviteResourceName, MaxPluginBytes);
    }

    private static async Task<byte[]> ReadLicenseAsync()
    {
        return await ReadEmbeddedResourceAsync(LicenseResourceName, MaxPluginBytes);
    }

    private static async Task<byte[]> ReadEmbeddedResourceAsync(string resourceName, long maxBytes)
    {
        await using Stream resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The bundled resource '{resourceName}' is missing.");
        using var buffer = new MemoryStream();
        await resource.CopyToAsync(buffer);
        if (buffer.Length > maxBytes)
        {
            throw new InvalidDataException($"The bundled resource '{resourceName}' exceeded its expected size.");
        }

        return buffer.ToArray();
    }

    private static async Task<byte[]> DownloadModAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ModDownloadUrl);
        using HttpResponseMessage response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength > MaxModArchiveBytes)
        {
            throw new InvalidDataException("The Coop-Divers release exceeded the expected download size.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + bytesRead > MaxModArchiveBytes)
            {
                throw new InvalidDataException("The Coop-Divers release exceeded the expected download size.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead));
        }

        byte[] bytes = buffer.ToArray();
        VerifySha256(bytes, ModSha256, "Coop-Divers release");
        return bytes;
    }

    private static ModPayload ReadModPayload(byte[] archiveBytes)
    {
        using var memory = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        ZipArchiveEntry dll = archive.GetEntry(ModDllEntry)
            ?? throw new InvalidDataException("The Coop-Divers release does not contain its plugin DLL.");
        ZipArchiveEntry license = archive.GetEntry(ModLicenseEntry)
            ?? throw new InvalidDataException("The Coop-Divers release does not contain its license.");

        return new ModPayload(ReadEntry(dll), ReadEntry(license));
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static string GetSafeDestination(string root, string entryName)
    {
        string normalized = entryName
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException("BepInEx payload contains an absolute path.");
        }

        string destination = Path.GetFullPath(Path.Combine(root, normalized));
        string rootPrefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("BepInEx payload contains an unsafe path.");
        }

        return destination;
    }

    private static void EnsureDirectoryTracked(string directory, List<string> createdDirectories)
    {
        var missingDirectories = new Stack<string>();
        string? current = directory;
        while (current is not null && !Directory.Exists(current))
        {
            missingDirectories.Push(current);
            current = Path.GetDirectoryName(current);
        }

        if (current is null)
        {
            throw new IOException("Could not resolve the game installation directory.");
        }

        while (missingDirectories.Count > 0)
        {
            string missing = missingDirectories.Pop();
            Directory.CreateDirectory(missing);
            createdDirectories.Add(missing);
        }
    }

    private static async Task VerifyBepInExInstallAsync()
    {
        string temporaryGame = Path.Combine(
            Path.GetTempPath(),
            "DaveDiverCoopSetup-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryGame);

        try
        {
            await InstallBepInExAsync(temporaryGame);
            ValidateExistingBepInEx(temporaryGame);
            if (!File.Exists(Path.Combine(temporaryGame, "THIRD-PARTY-LICENSE-LGPL-2.1.txt")))
            {
                throw new InvalidDataException("The installed BepInEx runtime is missing its LGPL license.");
            }
        }
        finally
        {
            Directory.Delete(temporaryGame, recursive: true);
        }
    }

    private static async Task VerifySteamInviteInstallAsync(byte[] plugin, byte[] license)
    {
        string temporaryGame = Path.Combine(
            Path.GetTempPath(),
            "DaveDiverSteamInviteSetup-verify-" + Guid.NewGuid().ToString("N"));
        string communityDll = Path.Combine(
            temporaryGame,
            "BepInEx",
            "plugins",
            "DaveTheDiverMP",
            "DaveTheDiverMP.dll");
        byte[] existingCommunityPlugin = { 0x4D, 0x5A, 0x01, 0x02 };
        Directory.CreateDirectory(Path.GetDirectoryName(communityDll)!);

        try
        {
            await File.WriteAllBytesAsync(communityDll, existingCommunityPlugin);
            string? disabledPlugin = await InstallPluginFilesAsync(
                temporaryGame,
                InstallMode.SteamInvite,
                plugin,
                license);

            string installedPlugin = Path.Combine(
                temporaryGame,
                "BepInEx",
                "plugins",
                SteamInvitePluginPath);
            string installedLicense = Path.Combine(
                temporaryGame,
                "BepInEx",
                "plugins",
                "DaveTheDiverCoop",
                "LICENSE.txt");
            string[] disabledCommunityPlugins = Directory.GetFiles(
                Path.GetDirectoryName(communityDll)!,
                "DaveTheDiverMP.dll.disabled-*");

            if (!File.Exists(installedPlugin)
                || !File.ReadAllBytes(installedPlugin).SequenceEqual(plugin)
                || !File.Exists(installedLicense)
                || !File.ReadAllBytes(installedLicense).SequenceEqual(license)
                || disabledPlugin is null
                || disabledCommunityPlugins.Length != 1
                || !File.ReadAllBytes(disabledCommunityPlugins[0]).SequenceEqual(existingCommunityPlugin))
            {
                throw new InvalidDataException(
                    "The Steam-invite package installation or reversible mod switch failed verification.");
            }
        }
        finally
        {
            if (Directory.Exists(temporaryGame))
            {
                Directory.Delete(temporaryGame, recursive: true);
            }
        }
    }

    private static async Task WriteAtomicallyAsync(string destination, byte[] content)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string? BackupExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string backup = path + ".backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        if (File.Exists(backup))
        {
            backup += "-" + Guid.NewGuid().ToString("N");
        }
        File.Copy(path, backup, overwrite: false);
        return backup;
    }

    private static void RestoreBackup(
        string originalPath,
        string? backupPath,
        List<Exception> rollbackExceptions)
    {
        try
        {
            if (backupPath is not null && File.Exists(backupPath))
            {
                File.Copy(backupPath, originalPath, overwrite: true);
            }
            else if (File.Exists(originalPath))
            {
                File.Delete(originalPath);
            }
        }
        catch (Exception exception)
        {
            rollbackExceptions.Add(exception);
        }
    }

    private static void VerifySha256(byte[] bytes, string expected, string packageName)
    {
        string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(expected)))
        {
            throw new InvalidDataException($"{packageName} SHA-256 verification failed.");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DaveDiverCoopSetup/1.0");
        return client;
    }

    private sealed record ModPayload(byte[] Plugin, byte[] License);
}
