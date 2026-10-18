using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WPath = System.Windows.Shapes.Path;

namespace WinWidgets;

static class UI
{
    public static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI");

    public static TextBlock Txt(string s, double size, Brush fg, FontWeight? weight = null) => new()
    {
        Text = s,
        FontSize = size,
        Foreground = fg,
        FontFamily = Font,
        FontWeight = weight ?? FontWeights.Normal,
    };

    public const string MenuIcon = "M3,4.5 L13,4.5 M3,8 L13,8 M3,11.5 L13,11.5";
    public const string CloseIcon = "M4,4 L12,12 M12,4 L4,12";
    public const string PrevIcon = "M10,3 L5,8 L10,13";
    public const string NextIcon = "M6,3 L11,8 L6,13";

    public static Border IconButton(string pathData, Action click)
    {
        var b = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new WPath
            {
                Data = Geometry.Parse(pathData),
                Stroke = Theme.FgDim,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        b.MouseEnter += (_, _) => b.Background = Theme.Hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true; // do not start a window drag
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    /// <summary>Accent marker + small caps title on the left, a button on the right.</summary>
    public static Grid Header(string title, string iconPath, Action click)
    {
        var g = new Grid { Height = 24, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 8) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new Border { Width = 3, Height = 12, CornerRadius = new CornerRadius(2), Background = Theme.Accent, Margin = new Thickness(0, 0, 8, 0) });
        left.Children.Add(Txt(title.ToUpperInvariant(), 11, Theme.FgDim, FontWeights.SemiBold));
        g.Children.Add(left);
        var btn = IconButton(iconPath, click);
        btn.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(btn);
        return g;
    }
}
