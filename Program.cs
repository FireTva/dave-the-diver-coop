namespace DaveDiverCoopInstaller;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length == 1 && args[0].Equals("--verify", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                InstallerService.VerifyPackagesAsync(InstallerBuild.Mode).GetAwaiter().GetResult();
                Console.WriteLine("Pinned BepInEx and selected co-op package verified.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        Application.Run(new Form1(InstallerBuild.Mode));
        return 0;
    }
}
