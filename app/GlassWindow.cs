using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinWidgets;

/// <summary>Borderless layered window that paints the refracted desktop behind it.</summary>
abstract class GlassWindow : Window
{
    protected readonly Grid Body = new();
    readonly Image img = new() { Stretch = Stretch.None, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    readonly bool widget;
    readonly DispatcherTimer moveTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    readonly DispatcherTimer recapTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    bool dirty, userMoved;
    IntPtr hwnd;
    byte[] src = [], buf = [];
    WriteableBitmap? wb;
    int pw, ph;

    protected GlassWindow(double width, double height, bool widget)
    {
        this.widget = widget;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Width = width;
        Height = height;
        ShowActivated = !widget;
        Topmost = widget && Config.Cur.OnTop;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(img);
        root.Children.Add(Body);
        Content = root;

        SourceInitialized += (_, _) => OnInit();
        Loaded += (_, _) => Refresh(true);
        LocationChanged += (_, _) => { if (!dragging) { dirty = true; moveTimer.Start(); } };
        DpiChanged += (_, _) => Refresh(true);
        IsVisibleChanged += (_, _) => { if (IsVisible) recapTimer.Start(); else recapTimer.Stop(); };
        moveTimer.Tick += (_, _) =>
        {
            if (dirty) { dirty = false; Refresh(true); }
            else { moveTimer.Stop(); if (userMoved) { userMoved = false; OnMoveSettled(); } }
        };
        recapTimer.Tick += (_, _) => Refresh(true);
    }

    protected virtual void OnMoveSettled() { }

    protected void MakeDraggable(UIElement e) =>
        e.MouseLeftButtonDown += (_, a) =>
        {
            if (a.ButtonState != MouseButtonState.Pressed) return;
            BeginDrag();
            bool moved = false;
            try { DragMove(); moved = true; } catch { }
            EndDrag();
            if (moved) OnMoveSettled();
        };

    // While dragging, the whole monitor is captured and blurred once; every frame then only
    // re-draws the pane from that cache at the new position, so the glass follows without lag.
    byte[]? bigStrong, bigLight;
    int bigW, bigH, bigX, bigY;
    bool dragging;

    void BeginDrag()
    {
        if (hwnd == IntPtr.Zero || wb == null) return;
        var b = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
        bigX = b.X; bigY = b.Y; bigW = b.Width; bigH = b.Height;
        int n = bigW * bigH * 4;
        var raw = new byte[n];
        var scratch = new byte[n];
        bigStrong = new byte[n];
        bigLight = new byte[n];
        Native.GrabScreen(bigX, bigY, bigW, bigH, raw);
        Glass.Prepare(raw, bigW, bigH, (float)VisualTreeHelper.GetDpi(this).DpiScaleX, bigStrong, bigLight, scratch);
        dragging = true;
        CompositionTarget.Rendering += OnDragFrame;
    }

    void OnDragFrame(object? s, EventArgs e)
    {
        if (wb == null || bigStrong == null || bigLight == null) return;
        Native.GetWindowRect(hwnd, out var r);
        int ox = r.Left - bigX, oy = r.Top - bigY;
        if (ox < 0 || oy < 0 || ox + pw > bigW || oy + ph > bigH) return; // off this monitor: keep last frame
        Glass.Draw(bigStrong, bigLight, bigW, bigH, ox, oy, pw, ph, (float)VisualTreeHelper.GetDpi(this).DpiScaleX, buf);
        wb.WritePixels(new Int32Rect(0, 0, pw, ph), buf, pw * 4, 0);
    }

    void EndDrag()
    {
        if (!dragging) return;
        dragging = false;
        CompositionTarget.Rendering -= OnDragFrame;
        bigStrong = bigLight = null;
        dirty = false;
        Refresh(true);
        Glass.ReleaseScratch();
        Native.TrimWorkingSet();
    }

    void OnInit()
    {
        hwnd = new WindowInteropHelper(this).Handle;
        if (Environment.GetEnvironmentVariable("WW_NOAFFINITY") == null)
            Native.SetWindowDisplayAffinity(hwnd, 0x11); // exclude from capture, so we can see what is behind us
        if (widget)
        {
            // tool window + no activation: no taskbar / alt-tab entry, never steals focus
            var ex = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
            Native.SetWindowLongPtr(hwnd, -20, (IntPtr)(ex | 0x80 | 0x08000000));
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        }
    }

    IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0046 && !Config.Cur.OnTop) // WM_WINDOWPOSCHANGING: keep widgets under normal windows
        {
            var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(l);
            if ((wp.flags & 0x0004) == 0)
            {
                var after = Native.AboveDesktop();
                if (after != h) { wp.hwndInsertAfter = after; Marshal.StructureToPtr(wp, l, false); }
            }
        }
        return IntPtr.Zero;
    }

    public void ApplyOnTop()
    {
        if (!widget) return;
        Topmost = Config.Cur.OnTop;
        if (!Config.Cur.OnTop && hwnd != IntPtr.Zero)
            PlaceAboveDesktop();
    }

    /// <summary>Win+D raises the desktop over bottom-most windows; this puts the widget back above it.</summary>
    public void PlaceAboveDesktop()
    {
        if (hwnd == IntPtr.Zero || Config.Cur.OnTop) return;
        var after = Native.AboveDesktop();
        if (after != hwnd) Native.SetWindowPos(hwnd, after, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
    }

    public void Refresh(bool recapture)
    {
        if (hwnd == IntPtr.Zero || !IsVisible) return;
        Native.GetWindowRect(hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return;

        if (w != pw || h != ph || wb == null)
        {
            pw = w; ph = h;
            src = new byte[w * h * 4];
            buf = new byte[w * h * 4];
            var dpi = VisualTreeHelper.GetDpi(this);
            wb = new WriteableBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
            img.Source = wb;
            recapture = true;
        }
        if (recapture) Native.GrabScreen(r.Left, r.Top, w, h, src);

        Glass.Render(src, w, h, (float)VisualTreeHelper.GetDpi(this).DpiScaleX, buf);
        wb.WritePixels(new Int32Rect(0, 0, w, h), buf, w * 4, 0);
    }
}
