using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows;
using DraftPilot.Core.Config;

namespace DraftPilot.App;

/// <summary>
/// Captures WPF data-binding failures to a log file.
/// <para>
/// A broken binding is silent at runtime — the control simply stays empty — so without this the
/// only way to notice is to spot the gap by eye. Enabled by the <c>DRAFTPILOT_TRACE</c>
/// environment variable, or always in Debug builds.
/// </para>
/// </summary>
public static class BindingDiagnostics
{
    private static FileListener? _listener;

    public static string? LogPath { get; private set; }

    public static void EnableIfRequested()
    {
        var requested = Environment.GetEnvironmentVariable("DRAFTPILOT_TRACE") is { Length: > 0 };
#if DEBUG
        requested = true;
#endif
        if (!requested)
            return;

        try
        {
            AppPaths.EnsureDataDirectory();
            LogPath = Path.Combine(AppPaths.DataDirectory, "binding-trace.log");

            _listener = new FileListener(LogPath);
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(_listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics are optional; never let them stop the app from starting.
            _listener = null;
        }
    }

    public static void Flush() => _listener?.Flush();

    private sealed class FileListener(string path) : TraceListener
    {
        private readonly StreamWriter _writer = new(path, append: false, Encoding.UTF8) { AutoFlush = true };

        public override void Write(string? message) => _writer.Write(message);

        public override void WriteLine(string? message) => _writer.WriteLine(message);

        public override void Flush() => _writer.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _writer.Dispose();

            base.Dispose(disposing);
        }
    }
}
