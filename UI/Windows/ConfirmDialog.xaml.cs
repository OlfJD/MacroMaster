using System.Windows;
using System.Windows.Input;

namespace MacroMaster.UI.Windows;

public partial class ConfirmDialog : Window
{
    private ConfirmDialog()
    {
        InitializeComponent();
        WindowHelper.Attach(this);
        UiScale.ApplyToDialog(this);
    }

    /// <summary>Shows a themed confirmation. Pass <c>cancelText: null</c> for a single-button notice.</summary>
    public static bool Show(Window owner, string title, string message, string confirmText = "OK",
                            string? cancelText = "Cancel", bool danger = false)
    {
        var dlg = new ConfirmDialog { Owner = owner };
        dlg.TxtTitle.Text = title;
        dlg.TxtMessage.Text = message;
        dlg.BtnConfirm.Content = confirmText;

        if (cancelText == null)
            dlg.BtnCancel.Visibility = Visibility.Collapsed;
        else
            dlg.BtnCancel.Content = cancelText;

        if (danger)
        {
            dlg.BtnConfirm.Style = (Style)dlg.FindResource("DangerButton");
            dlg.IconBadge.Background = (System.Windows.Media.Brush)dlg.FindResource("DangerSubtleBrush");
            dlg.BadgeIcon.Foreground = (System.Windows.Media.Brush)dlg.FindResource("DangerBrush");
            dlg.BadgeIcon.IconKey = "trash-2";
        }

        return dlg.ShowDialog() == true;
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
