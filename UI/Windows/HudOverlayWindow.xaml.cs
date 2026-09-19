using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MacroMaster.Core.Models;

namespace MacroMaster.UI.Windows;

public partial class HudOverlayWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new();
    private Storyboard? _showAnim;
    private Storyboard? _hideAnim;

    public HudOverlayWindow()
    {
        InitializeComponent();

        _hideTimer.Interval = TimeSpan.FromMilliseconds(2200);
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            HideHud();
        };

        SourceInitialized += (s, e) =>
        {
            SetWindowExTransparent();
            _showAnim = (Storyboard)FindResource("ShowNotificationAnim");
            _hideAnim = (Storyboard)FindResource("HideNotificationAnim");
            if (_hideAnim != null)
            {
                _hideAnim.Completed += (s2, e2) => Visibility = Visibility.Hidden;
            }
        };
    }

    private void SetWindowExTransparent()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public void ShowNotification(string title, string subtitle, string iconKey, string accentColorHex, HudPosition position = HudPosition.TopCenter, int durationMs = 2200)
    {
        Dispatcher.Invoke(() =>
        {
            HudTitle.Text = title;
            HudSubtitle.Text = subtitle;
            HudIcon.IconKey = iconKey;

            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accentColorHex);
                var brush = new SolidColorBrush(color);
                HudIcon.Foreground = brush;
                IconBadge.BorderBrush = brush;
                HudGlow.Color = color;
            }
            catch
            {
                // Fallback default emerald
            }

            RepositionWindow(position);

            if (!IsVisible)
            {
                Show();
            }

            Visibility = Visibility.Visible;
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(durationMs);

            _showAnim?.Begin(this);
            _hideTimer.Start();
        });
    }

    private void HideHud()
    {
        _hideAnim?.Begin(this);
    }

    private void RepositionWindow(HudPosition position)
    {
        UpdateLayout();
        double screenW = SystemParameters.PrimaryScreenWidth;
        double screenH = SystemParameters.PrimaryScreenHeight;
        double winW = ActualWidth > 0 ? ActualWidth : 340;
        double winH = ActualHeight > 0 ? ActualHeight : 80;

        switch (position)
        {
            case HudPosition.TopCenter:
                Left = (screenW - winW) / 2;
                Top = 30;
                break;
            case HudPosition.TopRight:
                Left = screenW - winW - 30;
                Top = 30;
                break;
            case HudPosition.BottomRight:
                Left = screenW - winW - 30;
                Top = screenH - winH - 60;
                break;
            case HudPosition.BottomCenter:
                Left = (screenW - winW) / 2;
                Top = screenH - winH - 60;
                break;
            case HudPosition.TopLeft:
                Left = 30;
                Top = 30;
                break;
        }
    }

    #region Win32 Constants

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    #endregion
}
