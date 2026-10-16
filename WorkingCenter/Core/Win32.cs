using System.Runtime.InteropServices;

namespace WorkingCenter.Core;

internal static class Win32
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    // ---- Acrylic blur via SetWindowCompositionAttribute (Win10 1809+ / Win11) ----
    // Same Win32 call WPF-UI and similar libs use internally; no extra dependency.
    private const int ACCENT_ENABLE_ACRYLICBLURONVIEW = 4;
    private const int WCA_ACCENT_POLICY = 19;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;  // ABGR: 0xTT BB GG RR (T = alpha 0..255)
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    // ---- Acrylic / Mica backdrop ----
    // Win11 (22H2+) official API: DwmSetWindowAttribute with SYSTEMBACKDROP_TYPE.
    // DWMSBT_MAINWINDOW=2 (Mica), DWMSBT_TRANSIENTWINDOW=3 (Acrylic), DWMSBT_TABBEDWINDOW=4.
    // Older path below (SetWindowCompositionAttribute) covers Win10 1809+.
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Win11 22H2+ Mica (translucent system-tinted, opaque) backdrop.
    /// Returns true if the DWM call accepted it. Because Mica is opaque it does
    /// NOT need a transparent window, so the slide animation won't expose a
    /// mismatched HWND background (no flicker). On older Windows it silently
    /// no-ops and the caller's solid Card background shows — still opaque, still
    /// flicker-free. DWMSBT_MAINWINDOW=2 (Mica), 3=Acrylic, 4=Tabbed.</summary>
    public static bool TryEnableMica(IntPtr hwnd)
    {
        try
        {
            // Mica needs the window to opt out of a dark title bar explicitly
            // (not strictly required for borderless, but harmless) and the
            // SYSTEMBACKDROP_TYPE attribute set.
            int dark = 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int v = 2; // DWMSBT_MAINWINDOW (Mica)
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, sizeof(int));
            return hr == 0;
        }
        catch { return false; }
    }

    /// <summary>Win11 acrylic (transient backdrop). Returns true if the DWM call
    /// accepted it. Requires a real (non-AllowTransparency) HWND with a transparent
    /// background so the backdrop shows through the WPF content.</summary>
    public static bool TryEnableAcrylicDwm(IntPtr hwnd)
    {
        try
        {
            int v = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, sizeof(int));
            return hr == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Enables an acrylic blur background on the window (Win10 1809+ fallback).
    /// The window must be a real (non-AllowTransparency) HWND with a transparent
    /// background so the blur shows through. Returns false on older Windows.
    /// </summary>
    public static bool TryEnableAcrylic(IntPtr hwnd, byte alpha)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURONVIEW,
                AccentFlags = 0,
                // Light tint (white) at the given alpha: ABGR = alpha,0xFF,0xFF,0xFF
                GradientColor = (uint)(0xFFFFFF | (alpha << 24)),
            };
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = Marshal.AllocHGlobal(Marshal.SizeOf(accent)),
                SizeOfData = Marshal.SizeOf(accent),
            };
            Marshal.StructureToPtr(accent, data.Data, false);
            int ok = SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(data.Data);
            return ok == 1;  // returns 1 on success on supported systems
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps the window out of the taskbar and Alt+Tab (WS_EX_TOOLWINDOW) but
    /// lets it activate on an explicit click so TextBoxes inside it can receive
    /// keyboard focus and IME input. Hover-show uses Show(activate:false) so
    /// expanding the panel never steals focus from the user's active app; only
    /// clicking a text field activates it (the intended "use when needed" model).
    /// </summary>
    public static void ApplyToolWindow(IntPtr hwnd)
    {
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }
}
