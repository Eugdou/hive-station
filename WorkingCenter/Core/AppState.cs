using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace WorkingCenter.Core;

/// <summary>
/// Which screen edge the dock sits on. Drives both positioning and the
/// direction of the slide-in / slide-out animation.
/// </summary>
public enum EdgePosition { Right, Left, Top, Bottom }

/// <summary>
/// Central coordinator for stage 2: owns the edge handle, the main panel,
/// the single auto-hide timer, the tray icon, and the "hidden to tray" mode.
///
/// Window states:
///   Docked      — small edge handle visible; panel shows on hover.
///   Visible     — panel is shown; auto-hide armed (unless pinned).
///   Pinned      — panel shown and locked; auto-hide suspended.
///   ToTray      — handle + panel both hidden; only the tray icon remains.
/// </summary>
internal sealed class AppState : IDisposable
{
    private readonly DispatcherTimer _hideTimer;
    private NotifyIcon? _tray;
    private HandleWindow? _handle;
    private PanelWindow? _panel;

    private double _panelWidth = 360;
    private int _hideDelayMs = 300;
    private EdgePosition _edge = EdgePosition.Right;
    private string _settingsPath = "";

    // Re-arm gate: after a manual hide, the handle ignores MouseEnter until
    // the mouse has left the tab, so a lingering cursor can't instantly re-pop it.
    private bool _handleArmed = true;

    // When true, handle + panel are both gone; only the tray icon remains.
    private bool _toTray = false;

    // Placeholder for stage 3/4: when true, auto-hide is suspended (editing in progress).
    public bool IsEditing { get; set; }
    public EdgePosition Edge => _edge;

    public AppState()
    {
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(_hideDelayMs),
        };
        _hideTimer.Tick += OnHideTimerTick;
    }

    public void Start()
    {
        LoadEdgeSettings();
        _handle = new HandleWindow(this);
        _panel = new PanelWindow(this);
        PositionWindows();
        ApplyNoActivateTo(_handle);
        ApplyToolWindowTo(_panel);

        _handle.Show();
        _panel.ShowActivated = false;  // creating HWND without grabbing focus
        _panel.Show();                  // create HWND; stays inactive (no focus steal)
        _panel.HidePanelImmediate();   // hidden but alive

        _tray = new NotifyIcon
        {
            Icon = IconGenerator.CreateBeeIcon(),
            Text = "Hive Station",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.DoubleClick += (s, e) => ToggleFromTray();

        Logger.Info("AppState started: handle + panel ready, tray active (no global hotkey)");
    }

    public void Shutdown()
    {
        _hideTimer.Stop();
        FlushNotes();                   // last-chance flush before closing the panel
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _panel?.Close();
        _handle?.Close();
        Logger.Info("AppState shutdown");
    }

    /// <summary>Flush pending (debounced) note writes through the panel.</summary>
    public void FlushNotes() => _panel?.FlushNotes();

    public void Dispose() => Shutdown();

    // ---- positioning -------------------------------------------------------

    private void PositionWindows()
    {
        var area = SystemParameters.WorkArea;   // primary monitor, excludes taskbar
        Logger.Info($"WorkArea: L={area.Left} T={area.Top} R={area.Right} B={area.Bottom} ({area.Width}x{area.Height}) edge={_edge}");

        // The handle window is wider than its visible bar so the hit-zone is
        // forgiving when the mouse sweeps the edge quickly. It sits flush to
        // the physical edge (a couple px beyond WorkArea to cover bezel insets).
        const double handleW = 18, handleH = 360, edgeOverscan = 2;
        switch (_edge)
        {
            case EdgePosition.Right:
                _handle!.Width = handleW; _handle.Height = handleH;
                _handle.Left = area.Right - handleW + edgeOverscan;
                _handle.Top = area.Top + (area.Height - handleH) / 2;
                _panel!.Width = _panelWidth; _panel.Height = area.Height;
                _panel.Left = area.Right - _panelWidth; _panel.Top = area.Top;
                break;
            case EdgePosition.Left:
                _handle!.Width = handleW; _handle.Height = handleH;
                _handle.Left = area.Left - edgeOverscan;
                _handle.Top = area.Top + (area.Height - handleH) / 2;
                _panel!.Width = _panelWidth; _panel.Height = area.Height;
                _panel.Left = area.Left; _panel.Top = area.Top;
                break;
            case EdgePosition.Top:
                _handle!.Width = handleH; _handle.Height = handleW;  // horizontal tab
                _handle.Left = area.Left + (area.Width - handleH) / 2;
                _handle.Top = area.Top;
                _panel!.Width = area.Width; _panel.Height = area.Height * 0.7;
                _panel.Left = area.Left; _panel.Top = area.Top;
                break;
            case EdgePosition.Bottom:
                _handle!.Width = handleH; _handle.Height = handleW;
                _handle.Left = area.Left + (area.Width - handleH) / 2;
                _handle.Top = area.Bottom - handleW;
                _panel!.Width = area.Width; _panel.Height = area.Height * 0.7;
                _panel.Left = area.Left; _panel.Top = area.Bottom - _panel.Height;
                break;
        }
    }

    // The edge handle never takes input, so it can stay fully no-activate
    // (never steals focus, never appears in taskbar/Alt+Tab).
    // ---- edge position (persisted) ---------------------------------------

    private void LoadEdgeSettings()
    {
        try
        {
            _settingsPath = Path.Combine(AppContext.BaseDirectory, "data", "settings.json");
            if (File.Exists(_settingsPath))
            {
                var txt = File.ReadAllText(_settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(txt);
                if (doc.RootElement.TryGetProperty("edge", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String
                    && Enum.TryParse<EdgePosition>(e.GetString(), out var parsed))
                    _edge = parsed;
            }
        }
        catch { /* keep default */ }
    }

    private void SaveEdgeSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_settingsPath, "{\"edge\":\"" + _edge + "\"}");
        }
        catch { /* best-effort */ }
    }

    /// <summary>Switch the dock edge (left/right) at runtime: reposition handle +
    /// panel and persist. Called from the settings page via the VM callback.</summary>
    public void SetEdge(EdgePosition edge)
    {
        if (edge == _edge) return;
        _edge = edge;
        SaveEdgeSettings();
        PositionWindows();
        Logger.Info($"Edge set to {_edge}; windows repositioned");
    }

    private static void ApplyNoActivateTo(Window w)
    {
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            Win32.ApplyToolWindow(hwnd);
            int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            ex |= 0x08000000; // WS_EX_NOACTIVATE
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex);
            Logger.Info($"{w.GetType().Name}: WS_EX_NOACTIVATE|TOOLWINDOW applied (hwnd={hwnd})");
        };
    }

    // The panel must let TextBoxes receive focus/IME on an explicit click, so
    // it only gets WS_EX_TOOLWINDOW (no taskbar/Alt+Tab) but can activate on
    // click. Hover-show uses Show(activate:false) so expanding never steals focus.
    private static void ApplyToolWindowTo(Window w)
    {
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            Win32.ApplyToolWindow(hwnd);
            Logger.Info($"{w.GetType().Name}: WS_EX_TOOLWINDOW applied (activates on click) (hwnd={hwnd})");
        };
    }

    // ---- docked / visible state machine -----------------------------------

    public void OnHandleMouseEnter()
    {
        if (_toTray) return;
        if (!_handleArmed)
        {
            Logger.Info("Handle enter ignored (disarmed after manual hide; waiting for mouse to leave tab)");
            return;
        }
        ShowPanel(trigger: "handle-mouseenter");
    }

    public void OnHandleMouseLeave()
    {
        if (!_handleArmed)
            Logger.Info("Handle left -> re-armed");
        _handleArmed = true;
    }

    // IME composition gate: set true while a Chinese IME is mid-composition
    // (pinyin typed, candidate not yet chosen). The one and only carve-out from
    // the pointer-driven hide — yanking the window away mid-composition loses
    // the candidate window and the user's typing. Plain "text box has focus"
    // does NOT count; only an active composition holds the panel. The setter
    // records the time so a stuck/abandoned composition can't hold the panel
    // forever (see the safety cap in OnHideTimerTick).
    public bool ImeComposing
    {
        get => _imeComposing;
        set { _imeComposing = value; _imeComposingSince = value ? DateTime.UtcNow : DateTime.MinValue; }
    }
    private bool _imeComposing;
    private DateTime _imeComposingSince = DateTime.MinValue;

    public void OnPanelMouseLeave()
    {
        // Pure pointer-driven hide (QQ-style, verified in stage 2). We do NOT
        // block on IsEditing/keyboard focus — that caused the panel to get stuck
        // open after editing the notes box (focus flags didn't reliably clear).
        // The IME false-leave is handled by the PanelContainsMouse() check below:
        // a Chinese IME candidate window can fire a spurious MouseLeave while the
        // pointer is still inside, so verify the cursor is really outside first.
        if (PanelContainsMouse())
        {
            Logger.Info("Panel leave ignored — pointer still inside (spurious, likely IME)");
            return;
        }
        StartHideTimer("panel-mouseleave");
    }

    public void OnPanelMouseEnter()
    {
        StopHideTimer("panel-mouseenter");
    }

    private bool PanelContainsMouse()
    {
        // Use the OS cursor position against the panel's real screen rect, not
        // WPF Mouse.GetPosition. WPF hit-testing is unreliable for this
        // transparent borderless topmost tool window (it can report an inside
        // coordinate even when the cursor is outside, and IME candidate windows
        // perturb it). GetCursorPos + PointToScreen is authoritative.
        if (_panel == null || !_panel.IsVisible) return false;
        if (!Win32.GetCursorPos(out var pt)) return false;
        var tl = _panel.PointToScreen(new System.Windows.Point(0, 0));
        var br = _panel.PointToScreen(new System.Windows.Point(_panel.ActualWidth, _panel.ActualHeight));
        return pt.X >= tl.X && pt.Y >= tl.Y && pt.X <= br.X && pt.Y <= br.Y;
    }

    private void StartHideTimer(string reason)
    {
        if (_panel is { Pinned: true }) { Logger.Info("Hide suppressed (pinned)"); return; }
        _hideTimer.Stop();
        _hideTimer.Start();
        Logger.Info($"Hide timer started ({_hideDelayMs}ms) reason={reason}");
    }

    private void StopHideTimer(string reason)
    {
        if (_hideTimer.IsEnabled)
        {
            _hideTimer.Stop();
            Logger.Info($"Hide timer cancelled reason={reason}");
        }
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        // Pointer has been outside the panel for the delay. Hide — UNLESS a Chinese
        // IME composition is mid-flight (pinyin in, candidate not yet chosen);
        // stealing the window then would lose the candidate window + typing.
        // Safety cap: if the flag claims composition but >2s passed, the composition
        // was almost certainly abandoned/cancelled without a clear event — force hide.
        if (ImeComposing && (DateTime.UtcNow - _imeComposingSince).TotalSeconds < 2)
        {
            Logger.Info("Hide timer fired during IME composition — holding panel");
            _hideTimer.Start();
            return;
        }
        if (ImeComposing) Logger.Info("IME flag stuck >2s — forcing hide");
        HidePanel(reason: "timer");
    }

    public void ShowPanel(string trigger)
    {
        StopHideTimer("show");
        _panel?.ShowPanel();
        Logger.Info($"Panel shown trigger={trigger}");
    }

    public void HidePanel(string reason)
    {
        StopHideTimer("hide");
        _panel?.HidePanel();
        Logger.Info($"Panel hidden reason={reason}");
    }

    // ---- "hidden to tray" mode (✕ button) ----------------------------------
    // Both the handle and the panel disappear; the app keeps running with
    // only the tray icon. Re-summon via tray double-click or the menu.

    public void HideToTray()
    {
        StopHideTimer("hide-to-tray");
        _toTray = true;
        _panel?.HidePanelImmediate();
        _handle?.Hide();
        Logger.Info("Hidden to tray (handle + panel gone; app still running)");
    }

    public void ShowFromTray()
    {
        _toTray = false;
        _handleArmed = true;
        _handle?.Show();
        ShowPanel("tray-show");
        Logger.Info("Restored from tray");
    }

    public void ToggleFromTray()
    {
        if (_toTray) ShowFromTray();
        else HideToTray();
    }

    // ---- pinned toggle (called by the panel's pin button) -----------------

    public void TogglePin()
    {
        if (_panel == null) return;
        _panel.Pinned = !_panel.Pinned;
        if (_panel.Pinned)
        {
            StopHideTimer("pin-on");
            Logger.Info("Pinned ON");
        }
        else
        {
            Logger.Info("Pinned OFF (auto-hide re-armed)");
        }
    }

    public bool IsPinned => _panel?.Pinned ?? false;

    // ---- tray menu ---------------------------------------------------------

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        var show = menu.Items.Add("显示工作台");
        show.Click += (s, e) => ShowFromTray();
        var hide = menu.Items.Add("隐藏到托盘");
        hide.Click += (s, e) => HideToTray();
        menu.Items.Add("-");
        var exit = menu.Items.Add("退出");
        exit.Click += (s, e) => Application.Current.Shutdown();
        return menu;
    }
}
