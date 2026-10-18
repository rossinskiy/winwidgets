using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinWidgets;

abstract class Widget : GlassWindow
{
    public const double Gap = 14;
    public static readonly string[] Keys = ["calendar", "clock", "system"];
    static readonly double[] Heights = [304, 150, 184];
    public readonly string Key;

    protected Widget(string key, double w, double h) : base(w, h, true)
    {
        Key = key;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var (x, y) = Config.Cur.Pos.TryGetValue(key, out var p) && p.Length == 2 ? (p[0], p[1]) : DefaultPos(key);
        Left = x;
        Top = y;
    }

    public static (double, double) DefaultPos(string key)
    {
        double y = 48;
        for (int i = 0; i < Keys.Length && Keys[i] != key; i++) y += Heights[i] + Gap;
        return (24, y);
    }

    public void ResetPos() => (Left, Top) = DefaultPos(Key);

    protected override void OnMoveSettled()
    {
        Config.Cur.Pos[Key] = [Left, Top];
        Config.Save();
    }

    protected static void OpenSettings() => Host.OpenSettings();
}

sealed class CalendarWidget : Widget
{
    static readonly string[] Months = ["январь", "февраль", "март", "апрель", "май", "июнь", "июль", "август", "сентябрь", "октябрь", "ноябрь", "декабрь"];
    static readonly string[] Dow = ["ПН", "ВТ", "СР", "ЧТ", "ПТ", "СБ", "ВС"];
    DateTime view = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    int shownDay = DateTime.Now.Day;
    readonly TextBlock month = UI.Txt("", 13, Theme.Fg, FontWeights.SemiBold);
    readonly Grid grid = new();
    readonly DispatcherTimer midnight = new() { Interval = TimeSpan.FromMinutes(1) };

    public CalendarWidget() : base("calendar", 300, 304)
    {
        var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        var head = UI.Header("Календарь", UI.MenuIcon, OpenSettings);
        MakeDraggable(head);
        stack.Children.Add(head);

        var nav = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 6) };
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nav.ColumnDefinitions.Add(new ColumnDefinition());
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var prev = UI.IconButton(UI.PrevIcon, () => Shift(-1));
        var next = UI.IconButton(UI.NextIcon, () => Shift(1));
        Grid.SetColumn(month, 1);
        month.HorizontalAlignment = HorizontalAlignment.Center;
        month.VerticalAlignment = VerticalAlignment.Center;
        month.Cursor = Cursors.Hand;
        month.ToolTip = "Вернуться к текущему месяцу";
        month.MouseLeftButtonUp += (_, _) => { view = new(DateTime.Now.Year, DateTime.Now.Month, 1); Fill(); };
        Grid.SetColumn(next, 2);
        nav.Children.Add(prev);
        nav.Children.Add(month);
        nav.Children.Add(next);
        stack.Children.Add(nav);

        for (int i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 7; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        stack.Children.Add(grid);
        Body.Children.Add(stack);

        Fill();
        midnight.Tick += (_, _) =>
        {
            if (DateTime.Now.Day != shownDay) { shownDay = DateTime.Now.Day; Fill(); }
        };
        midnight.Start();
    }

    void Shift(int d)
    {
        view = view.AddMonths(d);
        Fill();
    }

    void Cell(string text, int row, int col, double size, Brush fg, Brush? bg = null, FontWeight? w = null)
    {
        var t = UI.Txt(text, size, fg, w);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Margin = new Thickness(1.5), CornerRadius = new CornerRadius(8), Background = bg, Child = t };
        Grid.SetRow(b, row);
        Grid.SetColumn(b, col);
        grid.Children.Add(b);
    }

    void Fill()
    {
        month.Text = $"{Months[view.Month - 1]} {view.Year}".ToUpperInvariant();
        grid.Children.Clear();
        for (int i = 0; i < 7; i++) Cell(Dow[i], 0, i, 10, Theme.FgDim);
        int first = ((int)view.DayOfWeek + 6) % 7;
        int days = DateTime.DaysInMonth(view.Year, view.Month);
        var now = DateTime.Now;
        for (int d = 1; d <= days; d++)
        {
            int idx = first + d - 1, col = idx % 7;
            bool today = d == now.Day && view.Month == now.Month && view.Year == now.Year;
            if (today) Cell(d.ToString(), 1 + idx / 7, col, 13, Theme.OnAccent, Theme.Accent, FontWeights.Bold);
            else Cell(d.ToString(), 1 + idx / 7, col, 13, col > 4 ? Theme.Accent : Theme.Fg);
        }
    }
}

sealed class ClockWidget : Widget
{
    static readonly string[] Days = ["воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота"];
    readonly TextBlock time = UI.Txt("", 58, Theme.Fg, FontWeights.Light);
    readonly TextBlock date = UI.Txt("", 12, Theme.Accent, FontWeights.SemiBold);
    readonly DispatcherTimer timer = new();

    public ClockWidget() : base("clock", 300, 150)
    {
        var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
        var head = UI.Header("Сейчас", UI.MenuIcon, OpenSettings);
        MakeDraggable(head);
        stack.Children.Add(head);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        date.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(time);
        stack.Children.Add(date);
        Body.Children.Add(stack);
        timer.Tick += (_, _) => Tick();
        Tick();
        timer.Start();
    }

    void Tick()
    {
        var n = DateTime.Now;
        time.Text = n.ToString("HH:mm", CultureInfo.InvariantCulture);
        date.Text = $"{Days[(int)n.DayOfWeek]} · {n:dd.MM}".ToUpperInvariant();
        // wake once per minute, right after the minute rolls over
        timer.Interval = TimeSpan.FromMilliseconds(60000 - (n.Second * 1000 + n.Millisecond) + 80);
    }
}

sealed class SystemWidget : Widget
{
    sealed record Row(TextBlock Value, ColumnDefinition Fill, ColumnDefinition Rest);

    readonly Row cpu, ram, disk;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    ulong prevIdle, prevTotal;

    public SystemWidget() : base("system", 300, 184)
    {
        var stack = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        var head = UI.Header("Система", UI.MenuIcon, OpenSettings);
        MakeDraggable(head);
        stack.Children.Add(head);
        cpu = AddRow(stack, "CPU");
        ram = AddRow(stack, "ОЗУ");
        disk = AddRow(stack, "ДИСК");
        Body.Children.Add(stack);
        Update();
        timer.Tick += (_, _) => Update();
        timer.Start();
    }

    static Row AddRow(StackPanel parent, string label)
    {
        var top = new Grid { Margin = new Thickness(0, 6, 0, 5) };
        top.Children.Add(UI.Txt(label, 12, Theme.FgDim, FontWeights.SemiBold));
        var val = UI.Txt("", 12, Theme.Fg);
        val.HorizontalAlignment = HorizontalAlignment.Right;
        top.Children.Add(val);

        var fill = new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) };
        var rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var inner = new Grid();
        inner.ColumnDefinitions.Add(fill);
        inner.ColumnDefinitions.Add(rest);
        inner.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Theme.Accent });
        var track = new Border { Height = 5, CornerRadius = new CornerRadius(3), Background = Theme.Track, ClipToBounds = true, Child = inner, Margin = new Thickness(0, 0, 0, 6) };

        parent.Children.Add(top);
        parent.Children.Add(track);
        return new Row(val, fill, rest);
    }

    static void Set(Row r, double frac, string text)
    {
        frac = Math.Clamp(frac, 0.0001, 1);
        r.Fill.Width = new GridLength(frac, GridUnitType.Star);
        r.Rest.Width = new GridLength(1.0001 - frac, GridUnitType.Star);
        r.Value.Text = text;
    }

    static string Gib(double bytes) => (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

    void Update()
    {
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong i = idle.Value, total = kernel.Value + user.Value;
            ulong dt = total - prevTotal, di = i - prevIdle;
            prevIdle = i; prevTotal = total;
            double load = dt > 0 ? 1.0 - (double)di / dt : 0;
            Set(cpu, load, $"{Math.Round(load * 100)}%");
        }

        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref m))
        {
            double used = m.ullTotalPhys - m.ullAvailPhys;
            Set(ram, used / m.ullTotalPhys, $"{Gib(used)} / {Gib(m.ullTotalPhys)} GiB");
        }

        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            double free = d.AvailableFreeSpace, tot = d.TotalSize;
            Set(disk, (tot - free) / tot, $"{Math.Round(free / 1073741824.0)} GiB своб.");
        }
        catch { }
    }
}
