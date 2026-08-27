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

    /// <summary>Whether the icon handle is ours to dispose. False for the shared
    /// <see cref="SystemIcons.Application"/> fallback — disposing that would corrupt a
    /// process-wide cached instance.</summary>
    private readonly bool _ownsIcon;

    public TrayPresence(string tooltip)
    {
        _icon = new NotifyIcon
        {
            Text = tooltip,
            Visible = true,
            Icon = LoadIcon(out _ownsIcon),
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
        // .NET's NotifyIcon caps the text at 127 characters; longer throws. Never cut through a
        // surrogate pair — a half character renders as a broken glyph.
        if (text.Length > 127)
        {
            var cut = char.IsHighSurrogate(text[126]) ? 126 : 127;
            text = text[..cut];
        }

        _icon.Text = text;
    }

    private static Icon LoadIcon(out bool owned)
    {
        // The icon is embedded in the executable, so this is the same image the taskbar shows.
        var path = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(path))
        {
            var extracted = Icon.ExtractAssociatedIcon(path);
            if (extracted is not null)
            {
                owned = true;
                return extracted;
            }
        }

        owned = false;
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;

        // NotifyIcon.Dispose releases neither the menu nor the icon handle. The menu is always
        // ours; the icon only when we extracted it ourselves. Icon last — it must outlive the
        // NotifyIcon that still references it.
        var icon = _icon.Icon;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();

        if (_ownsIcon)
            icon?.Dispose();
    }
}
