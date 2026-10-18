using System.Runtime.InteropServices;

namespace WinWidgets;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPels, biYPels, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint Low, High;
        public ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("user32")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("user32")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
    [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("kernel32")] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

    [DllImport("user32", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);

    /// <summary>The window that sits directly above the desktop (Progman) in z-order, or HWND_TOP.</summary>
    public static IntPtr AboveDesktop()
    {
        var prog = FindWindow("Progman", null);
        return prog == IntPtr.Zero ? (IntPtr)1 : GetWindow(prog, 3); // GW_HWNDPREV, 0 == HWND_TOP
    }

    public static bool DesktopIsForeground()
    {
        var sb = new System.Text.StringBuilder(64);
        GetClassName(GetForegroundWindow(), sb, 64);
        var c = sb.ToString();
        return c is "Progman" or "WorkerW";
    }

    public static void TrimWorkingSet()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
    }

    /// <summary>Grabs a screen rectangle (physical pixels) as top-down BGRA.</summary>
    public static void GrabScreen(int x, int y, int w, int h, byte[] dst)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dib = CreateDIBSection(screen, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(mem, dib);
        BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
        Marshal.Copy(bits, dst, 0, w * h * 4);
        SelectObject(mem, old);
        DeleteObject(dib);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
    }
}
