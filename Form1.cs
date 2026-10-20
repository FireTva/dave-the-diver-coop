using System.Diagnostics;
using System.Drawing;

namespace DaveDiverCoopInstaller;

public sealed class Form1 : Form
{
    private readonly InstallMode _mode;

    private readonly TextBox _gamePath = new()
    {
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        Width = 520
    };

    private readonly Button _browse = new()
    {
        Text = "Browse...",
        AutoSize = true
    };

    private readonly Button _install = new()
    {
        Text = "Download and install",
        AutoSize = true
    };

    private readonly ProgressBar _progress = new()
    {
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        Width = 620,
        Style = ProgressBarStyle.Marquee,
        Visible = false
    };

    private readonly TextBox _log = new()
    {
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Width = 620,
        Height = 160
    };

    internal Form1(InstallMode mode)
    {
        _mode = mode;
        bool steamInvite = mode == InstallMode.SteamInvite;
        Text = steamInvite
            ? "Dave the Diver Steam Invite Setup"
            : "Dave the Diver Co-op Setup";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 490);
        ClientSize = new Size(700, 490);
        Font = new Font("Segoe UI", 9f);

        var warning = new Label
        {
            Text = steamInvite
                ? "Experimental Steam-invite prototype. Not affiliated with MINTROCKET, NEXON, Steam, or mod authors.\r\n"
                    + "Only diver position and facing are synchronized; two-client gameplay has not been verified.\r\n"
                    + "If Coop-Divers is installed, its plugin DLL will be backed up and disabled."
                : "Community pre-release installer. Not affiliated with MINTROCKET, NEXON, Steam, or the mod authors.\r\n"
                    + "The mod uses unencrypted, unauthenticated UDP. Only play with trusted friends; never expose a public lobby.\r\n"
                    + "Two-client gameplay has not been verified by this installer project.",
            AutoSize = false,
            Height = 68,
            Width = 650,
            ForeColor = Color.DarkRed,
            Location = new Point(20, 18)
        };
        Controls.Add(warning);

        Controls.Add(new Label
        {
            Text = "Select the Steam game folder containing DaveTheDiver.exe:",
            AutoSize = true,
            Location = new Point(20, 102)
        });
        _gamePath.Location = new Point(20, 128);
        _gamePath.Width = 520;
        Controls.Add(_gamePath);

        _browse.Location = new Point(550, 126);
        _browse.Click += BrowseClicked;
        Controls.Add(_browse);

        _install.Location = new Point(20, 170);
        if (steamInvite)
        {
            _install.Text = "Install Steam-invite prototype";
        }
        _install.Click += InstallClicked;
        Controls.Add(_install);

        _progress.Location = new Point(20, 210);
        _progress.Width = 640;
        Controls.Add(_progress);

        var details = new LinkLabel
        {
            Text = steamInvite
                ? "Prototype source and notes: DaveTheDiverCoop"
                : "Mod release and source: github.com/S1ii/Coop-Divers",
            AutoSize = true,
            Location = new Point(20, 242)
        };
        if (!steamInvite)
        {
            details.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(
                "https://github.com/S1ii/Coop-Divers/releases/tag/v0.18.3-ci.26")
            {
                UseShellExecute = true
            });
        }
        Controls.Add(details);

        _log.Location = new Point(20, 270);
        _log.Width = 640;
        _log.Height = 190;
        Controls.Add(_log);

        AcceptButton = _install;
        AppendLog(steamInvite
            ? "This installer uses a pinned, hash-verified Steam-invite prototype and BepInEx package."
            : "This installer downloads pinned, hash-verified BepInEx and Coop-Divers packages.");
        AppendLog("It will not launch the game. First launch may take longer while BepInEx creates interop files.");
    }

    private void BrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Steam DAVE THE DIVER folder containing DaveTheDiver.exe",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _gamePath.Text = dialog.SelectedPath;
        }
    }

    private async void InstallClicked(object? sender, EventArgs e)
    {
        _install.Enabled = false;
        _browse.Enabled = false;
        _progress.Visible = true;

        try
        {
            string gamePath = Path.GetFullPath(_gamePath.Text.Trim());
            _gamePath.Text = gamePath;
            AppendLog($"Checking game folder: {gamePath}");
            InstallerService.ValidateGameFolder(gamePath);

            if (IsGameRunningInFolder())
            {
                throw new InvalidOperationException("Close Dave the Diver before installing the co-op mod.");
            }

            bool hasBepInEx = InstallerService.HasBepInExFiles(gamePath);
            if (hasBepInEx)
            {
                InstallerService.ValidateExistingBepInEx(gamePath);
                AppendLog("Compatible BepInEx 6 build found; preserving the existing installation.");
            }
            else
            {
                AppendLog("Installing bundled BepInEx 6.0.0-be.789 runtime...");
                await InstallerService.InstallBepInExAsync(gamePath);
                AppendLog("BepInEx runtime installed.");
            }

            AppendLog(_mode == InstallMode.SteamInvite
                ? "Verifying and installing the Steam-invite prototype..."
                : "Downloading and verifying Coop-Divers 0.18.3-ci.26...");
            string? disabledPlugin = await InstallerService.InstallModAsync(gamePath, _mode);
            if (disabledPlugin is not null)
            {
                AppendLog($"Backed up and disabled the other co-op plugin: {disabledPlugin}");
            }
            AppendLog("Co-op mod installed successfully.");
            AppendLog("Next: launch the game once, wait for the title screen, then close it.");
            if (_mode == InstallMode.SteamInvite)
            {
                AppendLog("Enter the same Blue Hole dive, host a friends-only dive, then use Invite Steam friend.");
                AppendLog("The invited friend accepts the Steam invite and enters that same dive.");
            }
            else
            {
                AppendLog("After that, open Online on the title screen. Host shares their IP and port with the friend.");
            }
            AppendLog("Both players need this same setup and the same game build.");

            MessageBox.Show(
                this,
                _mode == InstallMode.SteamInvite
                    ? "Installation finished. Launch the game once so BepInEx can generate its local interop files, then close and reopen it. Both players should enter the same Blue Hole dive; the host selects Host friends-only dive, then Invite Steam friend. The guest accepts the Steam invite and enters that dive.\r\n\r\nThis prototype only synchronizes diver position and facing. A two-client session has not been verified."
                    : "Installation finished. Launch the game once so BepInEx can generate its local interop files, then close it and reopen the game. Use the Online menu to host or join.\r\n\r\nThis prerelease has not been verified in a two-client session. Only connect to trusted friends.",
                "Installation complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            AppendLog($"ERROR: {exception.Message}");
            MessageBox.Show(this, exception.Message, "Installation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _install.Enabled = true;
            _browse.Enabled = true;
            _progress.Visible = false;
        }
    }

    private bool IsGameProcessInFolder(Process process)
    {
        try
        {
            return process.MainModule?.FileName is string executable
                && string.Equals(
                    Path.GetDirectoryName(executable),
                    _gamePath.Text.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private bool IsGameRunningInFolder()
    {
        Process[] processes = Process.GetProcessesByName("DaveTheDiver");
        try
        {
            return processes.Any(IsGameProcessInFolder);
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void AppendLog(string message)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
