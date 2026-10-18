using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WEllipse = System.Windows.Shapes.Ellipse;

namespace WinWidgets;

sealed class GlassSlider : Grid
{
    readonly double min, max;
    readonly Border fill, thumb;
    double val;
    public event Action<double>? Changed;
    public event Action? Released;

    public GlassSlider(double min, double max)
    {
        this.min = min; this.max = max;
        Height = 24;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Track, VerticalAlignment = VerticalAlignment.Center });
        fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        thumb = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White, BorderBrush = Theme.Accent, BorderThickness = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(fill);
        Children.Add(thumb);
        SizeChanged += (_, _) => Place();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Move(e); };
        MouseMove += (_, e) => { if (IsMouseCaptured) Move(e); };
        MouseLeftButtonUp += (_, _) => { ReleaseMouseCapture(); Released?.Invoke(); };
    }

    public double Value { set { val = value; Place(); } }

    void Move(MouseEventArgs e)
    {
        double f = Math.Clamp((e.GetPosition(this).X - 8) / Math.Max(1, ActualWidth - 16), 0, 1);
        val = min + (max - min) * f;
        Place();
        Changed?.Invoke(val);
    }

    void Place()
    {
        if (ActualWidth < 16) return;
        double f = (val - min) / (max - min) * (ActualWidth - 16);
        thumb.Margin = new Thickness(f, 0, 0, 0);
        fill.Width = f + 8;
    }
}

sealed class Toggle : Border
{
    readonly Border knob = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = Brushes.White, Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
    bool on;
    public event Action<bool>? Toggled;

    public Toggle()
    {
        Width = 38; Height = 20;
        CornerRadius = new CornerRadius(6);
        Cursor = Cursors.Hand;
        Child = knob;
        Look();
        MouseLeftButtonUp += (_, _) => { On = !on; Toggled?.Invoke(on); };
    }

    public bool On { get => on; set { on = value; Look(); } }

    void Look()
    {
        Background = on ? Theme.Accent : Theme.Track;
        knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
}

sealed class SettingsWindow : GlassWindow
{
    static readonly (string Hex, string Name)[] Swatches =
    [
        ("#7db7ff", "Небесный"), ("#5ee0d6", "Бирюзовый"), ("#8fe388", "Мятный"), ("#d6e86a", "Лаймовый"),
        ("#ffc46b", "Янтарный"), ("#ff9a62", "Оранжевый"), ("#ff8fb8", "Розовый"), ("#ff7a7a", "Красный"), ("#ffffff", "Белый"),
    ];

    readonly List<(Border B, string Hex)> swatches = [];
    readonly List<(Border B, TextBlock T, string V)> themes = [];
    readonly Dictionary<string, Toggle> widgetToggles = [];
    readonly TextBlock hex = UI.Txt("", 12, Theme.FgDim);
    readonly GlassSlider tint = new(0.04, 0.6);
    readonly Toggle onTop = new(), autostart = new();

    public SettingsWindow() : base(400, 650, false)
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Настройки WinWidgets";
        var stack = new StackPanel { Margin = new Thickness(20, 14, 20, 16) };
        var head = UI.Header("Настройки", UI.CloseIcon, Close);
        MakeDraggable(head);
        stack.Children.Add(head);

        Section(stack, "Цвет");
        var wrap = new WrapPanel();
        foreach (var (h, name) in Swatches)
        {
            var b = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)),
                BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = name,
            };
            var hh = h;
            b.MouseLeftButtonUp += (_, _) => SetAccent(hh);
            swatches.Add((b, h));
            wrap.Children.Add(b);
        }
        stack.Children.Add(wrap);
        var custom = new Grid();
        var customBtn = ButtonBox("Свой цвет…", PickColor);
        customBtn.HorizontalAlignment = HorizontalAlignment.Left;
        hex.HorizontalAlignment = HorizontalAlignment.Right;
        hex.VerticalAlignment = VerticalAlignment.Center;
        custom.Children.Add(customBtn);
        custom.Children.Add(hex);
        stack.Children.Add(custom);

        Section(stack, "Стекло");
        var tl = new Grid();
        tl.Children.Add(UI.Txt("Насыщенность тонировки", 13, Theme.Fg));
        stack.Children.Add(tl);
        tint.Changed += v => { Config.Cur.Tint = Math.Round(v, 2); Config.Commit(false); };
        tint.Released += () => Config.Commit();
        stack.Children.Add(tint);

        Section(stack, "Тема");
        var seg = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        var segWrap = new Border { CornerRadius = new CornerRadius(10), Background = Theme.Track, Padding = new Thickness(3), Child = seg };
        (string V, string L)[] opts = [("dark", "Тёмная"), ("light", "Светлая"), ("system", "Как в системе")];
        for (int i = 0; i < opts.Length; i++)
        {
            seg.ColumnDefinitions.Add(new ColumnDefinition());
            var t = UI.Txt(opts[i].L, 12.5, Theme.Fg);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(4, 7, 4, 7), Cursor = Cursors.Hand, Child = t, Background = Brushes.Transparent };
            var v = opts[i].V;
            b.MouseLeftButtonUp += (_, _) => { Config.Cur.Mode = v; Config.Commit(); };
            Grid.SetColumn(b, i);
            seg.Children.Add(b);
            themes.Add((b, t, v));
        }
        stack.Children.Add(segWrap);

        Section(stack, "Виджеты");
        (string K, string L)[] ws = [("calendar", "Календарь"), ("clock", "Часы"), ("system", "Система")];
        foreach (var (k, l) in ws)
        {
            var t = new Toggle();
            var kk = k;
            t.Toggled += v => { Config.Cur.Enabled[kk] = v; Config.Commit(); };
            widgetToggles[k] = t;
            stack.Children.Add(Row(l, t));
        }

        Section(stack, "Поведение");
        onTop.Toggled += v => { Config.Cur.OnTop = v; Config.Commit(); };
        autostart.Toggled += v => { Config.Cur.Autostart = v; Config.Commit(); };
        stack.Children.Add(Row("Поверх всех окон", onTop));
        stack.Children.Add(Row("Запускать вместе с Windows", autostart));
        var reset = ButtonBox("Сбросить положение виджетов", Host.ResetPositions);
        reset.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(reset);

        Body.Children.Add(stack);
        Sync();
        Config.Changed += Sync;
        Closed += (_, _) => Config.Changed -= Sync;
    }

    static void Section(StackPanel p, string title)
    {
        p.Children.Add(new Border { Height = 1, Background = Theme.Track, Margin = new Thickness(0, 12, 0, 10) });
        var t = UI.Txt(title.ToUpperInvariant(), 11, Theme.FgDim, FontWeights.SemiBold);
        t.Margin = new Thickness(0, 0, 0, 8);
        p.Children.Add(t);
    }

    static Grid Row(string text, Toggle t)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.Children.Add(UI.Txt(text, 13, Theme.Fg));
        t.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(t);
        return g;
    }

    static Border ButtonBox(string text, Action click)
    {
        var t = UI.Txt(text, 12.5, Theme.Fg);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Background = Theme.Track, Cursor = Cursors.Hand, Child = t };
        b.MouseEnter += (_, _) => b.Background = Theme.Hover;
        b.MouseLeave += (_, _) => b.Background = Theme.Track;
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    static void SetAccent(string h)
    {
        Config.Cur.Accent = h;
        Config.Commit();
    }

    void PickColor()
    {
        var c = (Color)ColorConverter.ConvertFromString(Config.Cur.Accent);
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetAccent($"#{dlg.Color.R:x2}{dlg.Color.G:x2}{dlg.Color.B:x2}");
    }

    void Sync()
    {
        var c = Config.Cur;
        foreach (var (b, h) in swatches)
            b.BorderBrush = string.Equals(h, c.Accent, StringComparison.OrdinalIgnoreCase) ? Theme.Fg : Brushes.Transparent;
        hex.Text = c.Accent.ToUpperInvariant();
        tint.Value = c.Tint;
        foreach (var (b, t, v) in themes)
        {
            bool sel = v == c.Mode;
            b.Background = sel ? Theme.Accent : Brushes.Transparent;
            t.Foreground = sel ? Theme.OnAccent : Theme.Fg;
            t.FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal;
        }
        foreach (var (k, t) in widgetToggles) t.On = c.Enabled.GetValueOrDefault(k, true);
        onTop.On = c.OnTop;
        autostart.On = c.Autostart;
    }
}
