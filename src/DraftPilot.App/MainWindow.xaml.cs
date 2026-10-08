using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DraftPilot.App.ViewModels;
using Size = System.Windows.Size;

namespace DraftPilot.App;

public partial class MainWindow : Window
{
    private const int GwlStyle = -16;
    private const int WsMaximizeBox = 0x00010000;
    private const int WmExitSizeMove = 0x0232;

    private readonly MainViewModel _model;
    private readonly Size _designedSize;
    private bool _allowClose;

    public MainWindow(MainViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;

        _designedSize = new Size(Width, Height);
        Topmost = model.Settings.KeepOnTop;
        RestorePlacement(model.Settings);
    }

    /// <summary>
    /// True while the window is minimised because the user pressed the minimise button. The
    /// start of a game minimises the window too, and App puts it back then — but not this.
    /// </summary>
    public bool MinimisedByHand { get; private set; }

    /// <summary>Lets the next close request through; called right before a real shutdown.</summary>
    public void AllowClose() => _allowClose = true;

    /// <summary>
    /// Puts the window at <paramref name="size"/>, or at the size it was designed for. Harness
    /// runs only: a screenshot has to show a known size, not whatever the live instance was last
    /// dragged to.
    /// </summary>
    public void UseSize(Size? size)
    {
        var target = size ?? _designedSize;
        Width = target.Width;
        Height = target.Height;
    }

    /// <summary>
    /// Takes the maximise box off the window. ResizeMode="CanResize" is what lets the edges be
    /// dragged, and it brings the maximise box with it — which means dragging the title bar
    /// against the top of the screen maximises the panel. A maximised borderless window hangs
    /// over the screen edge by its frame, and this one has no button to restore it.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GwlStyle, GetWindowLong(handle, GwlStyle) & ~WsMaximizeBox);
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessages);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        // Win+Up does not ask for the maximise box. Same reasoning as above: back to normal.
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

        if (WindowState != WindowState.Minimized)
            MinimisedByHand = false;

        base.OnStateChanged(e);
    }

    private IntPtr WindowMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The end of a drag on an edge. Saved here rather than on SizeChanged, which fires for
        // every pixel of the drag and would rewrite the settings file each time.
        if (message == WmExitSizeMove)
            SavePlacement();

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

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

    /// <summary>Stores the window bounds so the panel comes back where and how the user left it.</summary>
    /// <remarks>
    /// Nothing about the window moves or resizes unless the user drags it. The window used to
    /// shrink to its content outside a draft and grow back when one started — which read as the
    /// tool "minimising itself" whenever the game began. The size the user drags to is the size
    /// for every view.
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
        settings.WindowWidth = ActualWidth;
        settings.WindowHeight = ActualHeight;
        settings.Save();
    }

    private void RestorePlacement(Core.Config.AppSettings settings)
    {
        // Size first: where the window may sit depends on how big it is. The minimum is enforced
        // by WPF anyway; the screen is checked in EnsureOnScreen.
        if (double.IsFinite(settings.WindowWidth) && settings.WindowWidth > 0)
            Width = Math.Max(MinWidth, settings.WindowWidth);

        if (double.IsFinite(settings.WindowHeight) && settings.WindowHeight > 0)
            Height = Math.Max(MinHeight, settings.WindowHeight);

        if (double.IsNaN(settings.WindowLeft) || double.IsNaN(settings.WindowTop))
        {
            // First run: park it against the right edge of the working area, clear of the client.
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Top + 80;
        }
        else
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }

        // The first run too: on a 768-pixel laptop the designed 900 used to hang off the bottom.
        EnsureOnScreen();
    }

    /// <summary>
    /// Pulls the window back onto a visible monitor. A stored position can point at a screen that
    /// is no longer attached, or half below the bottom edge, and a stored size can come from a
    /// bigger monitor than the one attached now — either way the footer (and its buttons) would
    /// be out of reach.
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
        Width = Math.Max(MinWidth, Math.Min(Width, virtualArea.Width));
        Left = Math.Max(virtualArea.Left, Math.Min(Left, virtualArea.Right - Width));

        // Vertically the taskbar matters, and it lives on the PRIMARY monitor: clamping against
        // the virtual screen parked the footer underneath it. WPF only exposes the primary's
        // work area without a window handle, so: primary monitor → its work area, any other →
        // the virtual screen (secondary monitors have no taskbar by default). A screen shorter
        // than the window shrinks it down to the minimum; below that, Math.Max last, so the title
        // bar wins.
        var primary = SystemParameters.WorkArea;
        var centreX = Left + (Width / 2);
        var vertical = centreX >= primary.Left && centreX <= primary.Right ? primary : virtualArea;

        Height = Math.Max(MinHeight, Math.Min(Height, vertical.Height));
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

    /// <summary>Into the taskbar, unlike the close glyph, which hides into the tray.</summary>
    private void Minimise_Click(object sender, RoutedEventArgs e)
    {
        SavePlacement();
        MinimisedByHand = true;
        WindowState = WindowState.Minimized;
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _model.Settings.KeepOnTop = Topmost;
        _model.Settings.Save();
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
