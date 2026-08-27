using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DraftPilot.App;

/// <summary>Command-line switches meant for development only.</summary>
/// <param name="DemoRecording">Replay this recording instead of connecting to the client.</param>
/// <param name="DemoFrames">How many frames of the recording to play before holding.</param>
/// <param name="ScreenshotPath">Render the window to this PNG and exit.</param>
/// <param name="ExpandRows">Expand this many recommendation rows before capturing.</param>
/// <param name="Phase">Simulate this gameflow phase (e.g. InProgress) before capturing.</param>
public sealed record DevOptions(
    string? DemoRecording,
    int DemoFrames,
    string? ScreenshotPath,
    double ScreenshotDelaySeconds,
    double DemoSpeed,
    int ExpandRows,
    string? Phase)
{
    public bool IsDemo => DemoRecording is { Length: > 0 };

    public bool IsScreenshot => ScreenshotPath is { Length: > 0 };
}

/// <summary>
/// Lets the window be driven from a recording and captured to a file.
/// <para>
/// This exists because a window cannot be reviewed by reading its XAML. Rendering it to a PNG makes
/// layout, contrast and spacing checkable, including the drop-down, whose popup lives in its own
/// visual tree and therefore has to be captured separately.
/// </para>
/// </summary>
public static class DevHarness
{
    /// <summary>
    /// Set for demo/screenshot runs: they skip the single-instance handshake and may run next to
    /// a live instance, so they must not write their throwaway window position into the real
    /// settings.json (last full-file write wins there).
    /// </summary>
    public static bool SuppressPlacementSave { get; set; }

    public static DevOptions Parse(string[] args)
    {
        string? recording = null;
        string? screenshot = null;
        var frames = int.MaxValue;
        var delay = 1.5;
        var speed = double.PositiveInfinity;
        var expand = 0;
        string? phase = null;

        // Whether one of OUR dev switches was seen yet. GetCommandLineArgs starts with the
        // executable path, Windows and wrappers append their own tokens — none of that may
        // abort a normal start. Only stray tokens AFTER a recognised dev switch are almost
        // always an unquoted path that fell apart, and those must fail loudly.
        var sawDevFlag = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--demo" or "--frames" or "--screenshot" or "--screenshot-delay" or "--speed" or "--expand" or "--phase")
                sawDevFlag = true;

            switch (args[i])
            {
                case "--demo" when i + 1 < args.Length:
                    recording = args[++i];
                    break;

                case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed):
                    frames = parsed;
                    i++;
                    break;

                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    break;

                case "--screenshot-delay" when i + 1 < args.Length
                    && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var parsedDelay):
                    delay = parsedDelay;
                    i++;
                    break;

                // Real-time-ish replay. Needed to observe transitions (e.g. the draft ending):
                // at infinite speed a session frame and the end frame land inside the same
                // coalescing window and only the end survives.
                case "--speed" when i + 1 < args.Length
                    && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var parsedSpeed)
                    && parsedSpeed > 0:
                    speed = parsedSpeed;
                    i++;
                    break;

                // The score breakdown is behind a chevron the harness cannot click, and it is
                // exactly the part whose readability needs looking at.
                case "--expand" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedExpand):
                    expand = parsedExpand;
                    i++;
                    break;

                // A recording has no gameflow events; the in-game view needs the phase injected.
                case "--phase" when i + 1 < args.Length:
                    phase = args[++i];
                    break;

                // Development-only flags may still not fail silently: an unquoted path with a
                // space arrives as several tokens and used to be truncated without a word (the
                // crash log has a FileNotFoundException for 'D:\Claude' to prove it), and an
                // unparsable value fell back to its default as if nothing happened.
                default:
                    // Unknown "--" switches and stray tokens abort only a HARNESS invocation;
                    // in a normal start they belong to Windows or a wrapper, not to us.
                    if (sawDevFlag && args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"Unbekannter oder unvollständiger Schalter: {args[i]}");

                    if (sawDevFlag)
                    {
                        throw new ArgumentException(
                            $"Unerwartetes Argument „{args[i]}“ — Pfade mit Leerzeichen in Anführungszeichen setzen.");
                    }

                    break;
            }
        }

        if (recording is not null && !File.Exists(recording))
            throw new FileNotFoundException(
                $"Aufnahme nicht gefunden: {recording} — Pfad in Anführungszeichen setzen?", recording);

        return new DevOptions(recording, frames, screenshot, delay, speed, expand, phase);
    }

    /// <summary>
    /// Writes the window to <paramref name="path"/>, plus a second image of the first open
    /// drop-down next to it.
    /// </summary>
    public static void Capture(Window window, string path)
    {
        // 2x so the small type is legible when the image is reviewed.
        const double Scale = 2.0;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        window.UpdateLayout();
        Save(window, path, Scale);

        CaptureDropDown(window, Path.Combine(
            directory ?? ".",
            Path.GetFileNameWithoutExtension(path) + "-dropdown.png"), Scale);
    }

    /// <summary>
    /// Opens the first combo box and captures its popup. A popup is hosted in a separate window, so
    /// it never appears in a render of the main window - and it is exactly the part that was
    /// unreadable, so it needs its own check.
    /// </summary>
    private static void CaptureDropDown(Window window, string path, double scale)
    {
        var combo = FindVisual<ComboBox>(window);
        if (combo is null)
            return;

        combo.IsDropDownOpen = true;
        combo.UpdateLayout();

        // Let the popup build its visual tree before rendering it.
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        if (combo.Template.FindName("PART_Popup", combo) is Popup { Child: FrameworkElement child })
        {
            child.UpdateLayout();
            if (child.ActualWidth > 0 && child.ActualHeight > 0)
                Save(child, path, scale);
        }

        combo.IsDropDownOpen = false;
    }

    private static void Save(FrameworkElement element, string path, double scale)
    {
        var width = (int)Math.Ceiling(element.ActualWidth * scale);
        var height = (int)Math.Ceiling(element.ActualHeight * scale);

        if (width <= 0 || height <= 0)
            return;

        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
                return match;

            if (FindVisual<T>(child) is { } nested)
                return nested;
        }

        return null;
    }
}
