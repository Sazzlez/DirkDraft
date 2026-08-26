using System.Drawing;
using System.Windows.Forms;

namespace DraftPilot.App;

/// <summary>
/// The notification-area icon. WPF has no tray support of its own; WinForms' NotifyIcon is the
/// reliable route and costs one extra assembly rather than a hand-rolled Shell_NotifyIcon wrapper.
/// </summary>
public sealed class TrayPresence : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayPresence(string tooltip)
    {
        _icon = new NotifyIcon
        {
            Text = tooltip,
            Visible = true,
            Icon = LoadIcon(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("Daten aktualisieren", null, (_, _) => UpdateRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitRequested?.Invoke());

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public event Action? ShowRequested;

    public event Action? UpdateRequested;

    public event Action? ExitRequested;

    /// <summary>One-off balloon notification, e.g. "still running down here" on the first hide.</summary>
    public void ShowHint(string title, string text)
        => _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    /// <summary>Short line shown on hover; kept to the connection state.</summary>
    public void SetTooltip(string text)
    {
        // NotifyIcon truncates past 63 characters and throws on longer text in some versions.
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    private static Icon LoadIcon()
    {
        // The icon is embedded in the executable, so this is the same image the taskbar shows.
        var path = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(path))
        {
            var extracted = Icon.ExtractAssociatedIcon(path);
            if (extracted is not null)
                return extracted;
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
