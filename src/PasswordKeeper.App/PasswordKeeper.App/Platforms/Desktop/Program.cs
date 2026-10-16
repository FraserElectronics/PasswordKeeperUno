using Uno.UI.Hosting;

namespace PasswordKeeper.App;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // An app launched from Finder starts with "/" as its working directory; make relative asset
        // lookups (icon and text fonts) behave as they do when started from the build folder.
        try { Environment.CurrentDirectory = AppContext.BaseDirectory; } catch { }

        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
