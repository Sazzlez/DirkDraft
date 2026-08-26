using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DraftPilot.App.ViewModels;

namespace DraftPilot.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    /// <summary>The height the user chose, kept while the window is auto-sized down.</summary>
    private double _draftHeight;

    public MainWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;

        Topmost = model.Settings.AlwaysOnTop;
        RestorePlacement(model.Settings);

        _draftHeight = Height;
        _model.DraftActiveChanged += OnDraftActiveChanged;

        // Start compact: outside a draft there is nothing to fill a tall window with.
        ApplyCompactMode(isDraftActive: false);
    }

    /// <summary>Stores the window placement so the panel comes back where the user put it.</summary>
    public void SavePlacement()
    {
        // Only a normal window has meaningful bounds; a minimised one would store garbage.
        if (WindowState != WindowState.Normal)
            return;

        var settings = _model.Settings;
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
        settings.WindowWidth = Width;

        // While compact the height is whatever the content needs, which is not what the user picked.
        settings.WindowHeight = SizeToContent == SizeToContent.Manual ? Height : _draftHeight;
        settings.Save();
    }

    private void OnDraftActiveChanged(bool isActive) => Dispatcher.Invoke(() => ApplyCompactMode(isActive));

    /// <summary>
    /// Shrinks the window to its content while no draft is running and restores the chosen height
    /// once one starts. Keeps a status card on screen instead of a mostly empty panel.
    /// </summary>
    private void ApplyCompactMode(bool isDraftActive)
    {
        if (isDraftActive)
        {
            if (SizeToContent == SizeToContent.Manual)
                return;

            SizeToContent = SizeToContent.Manual;
            Height = Math.Max(MinHeight, _draftHeight);
            return;
        }

        if (SizeToContent != SizeToContent.Manual)
            return;

        // Remember the draft-time height before letting the window collapse.
        _draftHeight = Height;
        SizeToContent = SizeToContent.Height;
    }

    private void RestorePlacement(Core.Config.AppSettings settings)
    {
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);

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
    /// is no longer attached, which would leave the panel invisible with no way to get it back.
    /// </summary>
    private void EnsureOnScreen()
    {
        var area = SystemParameters.VirtualScreenWidth > 0
            ? new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight)
            : SystemParameters.WorkArea;

        const double Margin = 40;

        if (Left + Margin > area.Right || Left + Width - Margin < area.Left)
            Left = Math.Max(area.Left, area.Right - Width - 24);

        if (Top + Margin > area.Bottom || Top + Height - Margin < area.Top)
            Top = area.Top + 80;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
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
