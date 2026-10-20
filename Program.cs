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
                InstallerService.VerifyPackagesAsync().GetAwaiter().GetResult();
                Console.WriteLine("Pinned BepInEx and co-op mod packages verified.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        Application.Run(new Form1());
        return 0;
    }
}
