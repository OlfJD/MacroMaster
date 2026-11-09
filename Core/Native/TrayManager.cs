using System.Drawing;
using System.Windows.Forms;
using MacroMaster.Core.Engine;

namespace MacroMaster.Core.Native;

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MacroEngine _engine;
    private readonly Action _onRestoreWindow;

    public TrayManager(MacroEngine engine, Action onRestoreWindow)
    {
        _engine = engine;
        _onRestoreWindow = onRestoreWindow;

        Icon? appIcon = null;
        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                appIcon = new Icon(iconPath, SystemInformation.SmallIconSize);
            }
            else if (Environment.ProcessPath != null)
            {
                appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            }
        }
        catch
        {
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    appIcon = new Icon(iconPath);
                }
            }
            catch { }
        }

        _notifyIcon = new NotifyIcon
        {
            Text = "MacroMaster",
            Visible = true,
            Icon = appIcon ?? SystemIcons.Application
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open MacroMaster", null, (s, e) => _onRestoreWindow());
        var engineItem = menu.Items.Add("Pause engine", null, (s, e) => _engine.SetMasterEngineEnabled(!_engine.Settings.MasterEngineEnabled));
        var stopItem = menu.Items.Add("Stop everything", null, (s, e) => _engine.EmergencyStop());
        // Labels follow the current state and the configured kill-switch key
        menu.Opening += (s, e) =>
        {
            engineItem.Text = _engine.Settings.MasterEngineEnabled ? "Pause engine" : "Resume engine";
            stopItem.Text = $"Stop everything ({_engine.Settings.EmergencyKillswitchKey})";
        };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) =>
        {
            _notifyIcon.Visible = false;
            _engine.Dispose();
            System.Windows.Application.Current.Shutdown();
        });

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => _onRestoreWindow();
        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _onRestoreWindow();
            }
        };
    }

    public void UpdateTooltip(string text)
    {
        if (text.Length > 63) text = text.Substring(0, 60) + "...";
        _notifyIcon.Text = text;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        GC.SuppressFinalize(this);
    }
}
