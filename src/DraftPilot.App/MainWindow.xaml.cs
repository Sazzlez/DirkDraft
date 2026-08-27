using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DraftPilot.App.ViewModels;

namespace DraftPilot.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private bool _allowClose;

    public MainWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;

        Topmost = model.Settings.AlwaysOnTop;
        RestorePlacement(model.Settings);
    }

    /// <summary>Lets the next close request through; called right before a real shutdown.</summary>
    public void AllowClose() => _allowClose = true;

    /// <summary>
    /// Alt+F4 and the taskbar's "close window" bypass our title-bar glyph and genuinely close the
    /// window — after which the process (ShutdownMode is explicit) lived on as a zombie: the tray
    /// icon stayed, but "Öffnen" hit a closed window and threw forever. Closing now means the same
    /// as the glyph: hide into the tray. A real exit announces itself via <see cref="AllowClose"/>.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        SavePlacement();

        if (_allowClose)
            return;

        e.Cancel = true;
        Hide();
        HiddenToTray?.Invoke();
    }

    /// <summary>Stores the window position so the panel comes back where the user put it.</summary>
    /// <remarks>
    /// Position only: the size is fixed at 860 × 900 for every view. The window used to shrink to
    /// its content outside a draft and grow back when one started — which read as the tool
    /// "minimising itself" whenever the game began. Now nothing about the window ever moves or
    /// resizes unless the user drags it.
    /// </remarks>
    public void SavePlacement()
    {
        // Harness runs share the settings file with a possibly live instance; see the flag.
        if (DevHarness.SuppressPlacementSave)
            return;

        // Only a normal window has meaningful bounds; a minimised one would store garbage.
        if (WindowState != WindowState.Normal)
            return;

        var settings = _model.Settings;
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
        settings.Save();
    }

    private void RestorePlacement(Core.Config.AppSettings settings)
    {
        if (double.IsNaN(settings.WindowLeft) || double.IsNaN(settings.WindowTop))
        {
            // First run: park it against the right edge of the working area, clear of the client.
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Top + 80;
            return;
        }

        Left = settings.WindowLeft;
        Top = settings.WindowTop;

        EnsureOnScreen();
    }

    /// <summary>
    /// Pulls the window back onto a visible monitor. A stored position can point at a screen that
    /// is no longer attached, or half below the bottom edge — with a fixed, non-resizable window
    /// that would leave the footer (and its buttons) permanently unreachable.
    /// </summary>
    private void EnsureOnScreen()
    {
        var virtualArea = SystemParameters.VirtualScreenWidth > 0
            ? new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight)
            : SystemParameters.WorkArea;

        // Horizontally any monitor is fine.
        Left = Math.Max(virtualArea.Left, Math.Min(Left, virtualArea.Right - Width));

        // Vertically the taskbar matters, and it lives on the PRIMARY monitor: clamping against
        // the virtual screen parked the footer underneath it. WPF only exposes the primary's
        // work area without a window handle, so: primary monitor → its work area, any other →
        // the virtual screen (secondary monitors have no taskbar by default). Math.Max last, so
        // the title bar wins on screens shorter than the window.
        var primary = SystemParameters.WorkArea;
        var centreX = Left + (Width / 2);
        var vertical = centreX >= primary.Left && centreX <= primary.Right ? primary : virtualArea;

        Top = Math.Max(vertical.Top, Math.Min(Top, vertical.Bottom - Height));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        DragMove();

        // DragMove returns after the drop. Persisting here means the position survives even a
        // hard process kill — publish.ps1 does exactly that, and OnExit never runs then.
        SavePlacement();
    }

    /// <summary>Raised when the user hides the window into the tray via the close glyph.</summary>
    public event Action? HiddenToTray;

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        SavePlacement();
        Hide();
        HiddenToTray?.Invoke();
    }

    /// <summary>
    /// Applies a lane override. The dropdown is bound one way, so this only ever fires for a real
    /// user edit — and it still compares against the model to be safe.
    /// </summary>
    private void LaneOverride_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { Tag: SlotViewModel slot } box)
            return;

        if (box.SelectedIndex < 0 || box.SelectedIndex == slot.LaneIndex)
            return;

        _model.SetManualLane(slot, box.SelectedIndex);
    }
}
