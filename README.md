# WinWidgets

Liquid glass desktop widgets for Windows 11: calendar, clock and system monitor.

- Real glass: the desktop behind each widget is captured, blurred and refracted at the edges (lens bend, chromatic split, specular rim light), then tinted with the accent colour.
- Accent colour, tint strength and dark/light theme are set in the settings window (tray icon).
- Native .NET (WPF), about 25 MB of RAM.

## Build

```
cd app
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o ../dist
```

Requires the .NET 10 Desktop Runtime. Run `dist/WinWidgets.exe`.
