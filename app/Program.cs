using System.Windows;
using System.Windows.Threading;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace WinWidgets;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "WinWidgets.SingleInstance", out bool first);
        if (!first) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Update(Config.Cur);
        Host.Start();
        app.Run();
    }
}

static class Host
{
    static readonly Dictionary<string, Widget> widgets = [];
    static SettingsWindow? settings;
    static WF.NotifyIcon? tray;
    static SD.Icon? icon;
    static readonly List<WF.ToolStripMenuItem> widgetItems = [];
    static WF.ToolStripMenuItem? topItem;

    public static void Start()
    {
        BuildTray();
        Sync();
        Config.Changed += () => { Sync(); RefreshTray(); };

        var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        trim.Tick += (_, _) => Native.TrimWorkingSet();
        trim.Start();
        // Win+D raises the desktop above bottom-most windows: put the widgets back on top of it
        var desk = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var lastFg = IntPtr.Zero;
        var settle = new DispatcherTimer();
        int step = 0;
        settle.Tick += (_, _) =>
        {
            foreach (var w in widgets.Values) w.Refresh(true);
            // window animations (minimize, restore) take a moment: look again a bit later
            if (step++ == 0) settle.Interval = TimeSpan.FromMilliseconds(700);
            else settle.Stop();
        };
        desk.Tick += (_, _) =>
        {
            var fg = Native.GetForegroundWindow();
            if (fg != lastFg)
            {
                // focus moved (Win+D, app switch): what is behind the widgets changed, re-capture
                lastFg = fg;
                step = 0;
                settle.Stop();
                settle.Interval = TimeSpan.FromMilliseconds(350);
                settle.Start();
            }
            if (!Native.DesktopIsForeground()) return;
            foreach (var w in widgets.Values) w.PlaceAboveDesktop();
        };
        desk.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        first.Tick += (_, _) => { first.Stop(); Native.TrimWorkingSet(); };
        first.Start();
    }

    static Widget Create(string key) => key switch
    {
        "calendar" => new CalendarWidget(),
        "clock" => new ClockWidget(),
        _ => new SystemWidget(),
    };

    static void Sync()
    {
        foreach (var key in Widget.Keys)
        {
            bool want = Config.Cur.Enabled.GetValueOrDefault(key, true);
            if (want && !widgets.ContainsKey(key))
            {
                var w = Create(key);
                widgets[key] = w;
                w.Show();
            }
            else if (!want && widgets.Remove(key, out var old)) old.Close();
        }
        foreach (var w in widgets.Values)
        {
            w.ApplyOnTop();
            w.Refresh(false);
        }
    }

    public static void ResetPositions()
    {
        Config.Cur.Pos.Clear();
        foreach (var w in widgets.Values) w.ResetPos();
        Config.Save();
    }

    public static void OpenSettings()
    {
        if (settings == null)
        {
            settings = new SettingsWindow();
            settings.Closed += (_, _) => { settings = null; Native.TrimWorkingSet(); };
            settings.Show();
        }
        settings.Activate();
    }

    static void BuildTray()
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Настройки", null, (_, _) => OpenSettings());
        menu.Items.Add(new WF.ToolStripSeparator());
        (string K, string L)[] ws = [("calendar", "Календарь"), ("clock", "Часы"), ("system", "Система")];
        foreach (var (k, l) in ws)
        {
            var it = new WF.ToolStripMenuItem(l) { CheckOnClick = true, Tag = k };
            it.Click += (_, _) => { Config.Cur.Enabled[k] = it.Checked; Config.Commit(); };
            widgetItems.Add(it);
            menu.Items.Add(it);
        }
        menu.Items.Add(new WF.ToolStripSeparator());
        topItem = new WF.ToolStripMenuItem("Поверх всех окон") { CheckOnClick = true };
        topItem.Click += (_, _) => { Config.Cur.OnTop = topItem.Checked; Config.Commit(); };
        menu.Items.Add(topItem);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            tray!.Visible = false;
            tray.Dispose();
            Application.Current.Shutdown();
        });

        tray = new WF.NotifyIcon { Text = "WinWidgets", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) OpenSettings(); };
        RefreshTray();
    }

    static void RefreshTray()
    {
        foreach (var it in widgetItems) it.Checked = Config.Cur.Enabled.GetValueOrDefault((string)it.Tag!, true);
        topItem!.Checked = Config.Cur.OnTop;

        using var bmp = new SD.Bitmap(32, 32);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            var accent = SD.Color.FromArgb(Theme.AR, Theme.AG, Theme.AB);
            using var fill = new SD.Drawing2D.LinearGradientBrush(new SD.Rectangle(0, 0, 32, 32), SD.Color.FromArgb(255, 255, 255, 255), accent, 60f);
            g.FillEllipse(fill, 2, 2, 28, 28);
            using var pen = new SD.Pen(SD.Color.FromArgb(120, 255, 255, 255), 1.5f);
            g.DrawEllipse(pen, 2, 2, 28, 28);
        }
        var h = bmp.GetHicon();
        var fresh = (SD.Icon)SD.Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        tray!.Icon = fresh;
        icon?.Dispose();
        icon = fresh;
    }
}
