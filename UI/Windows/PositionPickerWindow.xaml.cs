using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MacroMaster.Core.Models;

namespace MacroMaster.UI.Windows;

/// <summary>
/// Lets the user drag a stand-in for the notification popup or the mode indicator to any spot on
/// any monitor. The result is the centre of the stand-in as a <see cref="ScreenSpot"/> (fractions of
/// that monitor's work area), so the real popup lands in the same place at any resolution.
/// </summary>
public partial class PositionPickerWindow : Window
{
    private readonly ScreenSpot _start;
    private readonly IntPtr _nearHwnd;
    private bool _done;

    /// <summary>The chosen spot, or null when cancelled.</summary>
    public ScreenSpot? Result { get; private set; }

    /// <param name="sampleText">What the stand-in says ("Notifications show up here").</param>
    /// <param name="start">Where it starts.</param>
    /// <param name="near">A window on the monitor to start on (the settings page's window).</param>
    public PositionPickerWindow(string sampleText, ScreenSpot start, Window? near)
    {
        InitializeComponent();
        SampleText.Text = sampleText;
        _start = start;
        _nearHwnd = near != null ? new WindowInteropHelper(near).Handle : IntPtr.Zero;

        UiScale.ApplyTo(Frame, UiScale.Current);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000; // off screen until it is centred on the start spot in Loaded
        Top = -10000;

        Loaded += (_, _) => CentreOn(OverlayPlacement.PointFor(_start, _nearHwnd != IntPtr.Zero ? _nearHwnd : new WindowInteropHelper(this).Handle));
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Finish(keep: true); return; }
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) Finish(keep: true);
            else if (e.Key == Key.Escape) Finish(keep: false);
        };
        // Clicking anywhere else counts as "that's the spot"
        Deactivated += (_, _) => Finish(keep: true);
    }

    /// <summary>Shows the picker and waits for the user; returns the spot or null when cancelled.</summary>
    public static ScreenSpot? Pick(string sampleText, ScreenSpot start, Window? near)
    {
        var picker = new PositionPickerWindow(sampleText, start, near);
        picker.ShowDialog();
        return picker.Result;
    }

    private void CentreOn(System.Drawing.Point target)
    {
        var current = CentreOnScreen();
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        Left += (target.X - current.X) / dpi.DpiScaleX;
        Top += (target.Y - current.Y) / dpi.DpiScaleY;
        Activate();
        Focus();
    }

    /// <summary>Centre of the window in physical screen pixels.</summary>
    private System.Drawing.Point CentreOnScreen()
    {
        var p = PointToScreen(new System.Windows.Point(ActualWidth / 2, ActualHeight / 2));
        return new System.Drawing.Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    private void Finish(bool keep)
    {
        if (_done) return;
        _done = true;
        Result = keep ? OverlayPlacement.SpotAt(CentreOnScreen()) : null;
        // Close later: Deactivated can fire while the window is already closing
        Dispatcher.BeginInvoke(() => { try { Close(); } catch (InvalidOperationException) { } });
    }
}
