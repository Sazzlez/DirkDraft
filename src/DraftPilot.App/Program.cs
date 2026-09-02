using System.Windows;
using Velopack;

namespace DraftPilot.App;

/// <summary>
/// The real entry point. WPF generates a <c>Main</c> of its own, but Velopack has to run before
/// anything else in the process: on install, update and uninstall the installer starts the exe
/// with hook arguments, expects it to do its bookkeeping and exit — and that must happen before
/// the single-instance mutex is taken or a window exists, or every update would look like a
/// second instance and get swallowed.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
