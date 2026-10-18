using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinWidgets;

sealed class Config
{
    public string Accent { get; set; } = "#7db7ff";
    public double Tint { get; set; } = 0.22;
    public string Mode { get; set; } = "dark";
    public bool OnTop { get; set; }
    public bool Autostart { get; set; }
    public Dictionary<string, bool> Enabled { get; set; } = new() { ["calendar"] = true, ["clock"] = true, ["system"] = true };
    public Dictionary<string, double[]> Pos { get; set; } = new();

    public static Config Cur { get; private set; } = Load();
    public static event Action? Changed;

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinWidgets", "config.json");

    static Config Load()
    {
        try { return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Cur, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>Persist, refresh the theme and tell every window.</summary>
    public static void Commit(bool persist = true)
    {
        if (persist) { Save(); SetAutostart(Cur.Autostart); }
        Theme.Update(Cur);
        Changed?.Invoke();
    }

    static void SetAutostart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (on) key.SetValue("WinWidgets", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("WinWidgets", false);
        }
        catch { }
    }
}

/// <summary>Live brushes: mutate the colour and every control using the brush follows.</summary>
static class Theme
{
    public static readonly SolidColorBrush Fg = new(), FgDim = new(), Track = new(), Hover = new(), Accent = new(), OnAccent = new();
    public static bool Light;
    public static byte AR, AG, AB;
    public static float Tint;

    public static void Update(Config c)
    {
        var col = (Color)ColorConverter.ConvertFromString(c.Accent);
        AR = col.R; AG = col.G; AB = col.B;
        Accent.Color = col;
        Light = c.Mode == "light" || (c.Mode == "system" && SystemIsLight());
        Fg.Color = Light ? Color.FromArgb(235, 18, 22, 34) : Color.FromArgb(240, 255, 255, 255);
        FgDim.Color = Light ? Color.FromArgb(150, 18, 22, 34) : Color.FromArgb(150, 255, 255, 255);
        Track.Color = Light ? Color.FromArgb(40, 18, 22, 34) : Color.FromArgb(46, 255, 255, 255);
        Hover.Color = Light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255);
        var lum = (0.299 * col.R + 0.587 * col.G + 0.114 * col.B) / 255;
        OnAccent.Color = lum > 0.55 ? Color.FromRgb(11, 16, 32) : Colors.White;
        Tint = (float)c.Tint;
    }

    static bool SystemIsLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (k?.GetValue("AppsUseLightTheme") as int?) == 1;
        }
        catch { return false; }
    }
}
