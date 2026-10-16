using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WorkingCenter.Core;
using WorkingCenter.Data;
using WorkingCenter.ViewModels;
using WorkingCenter.Views;

namespace WorkingCenter;

public partial class PanelWindow : Window
{
    private readonly AppState _state;
    private readonly WorkspaceViewModel _vm;
    public bool Pinned { get; set; }
    private bool _settledVisible;  // true once the slide-in animation has completed

    private static readonly TimeSpan SlideDur = TimeSpan.FromMilliseconds(160);

    internal PanelWindow(AppState state)
    {
        InitializeComponent();
        _state = state;

        // Build the store + root view model. Stage 5 swaps JsonStore for a
        // SqliteStore in this one line — the VM depends only on IStore.
        IStore store = new JsonStore();
        _vm = new WorkspaceViewModel(store);
        WorkspaceHost.DataContext = _vm;
        SettingsHost.DataContext = _vm;

        MouseEnter += (_, _) => _state.OnPanelMouseEnter();
        MouseLeave += (_, _) => _state.OnPanelMouseLeave();

        // While any editing is happening inside the panel, pause auto-hide.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.IsEditingAny))
                _state.IsEditing = _vm.IsEditingAny;
        };

        // Apply edge position changes (settings page) to AppState.
        _vm.RequestSetEdge = edge => _state.SetEdge(edge);
        _vm.InitEdge(_state.Edge);
        _vm.InitTheme(ThemeService.Current);

        // Toggle the settings icon between gear (-> open) and back arrow (-> close).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.CurrentView))
                UpdateSettingsIcon();
        };

        // Smooth slide animation uses AllowsTransparency=True (see PanelWindow.xaml);
        // Mica/Acrylic need AllowsTransparency=False and cost animation smoothness,
        // so we don't enable a DWM backdrop here.
    }

    /// <summary>Flush any pending (debounced) writes — called before hide-to-tray,
    /// on close, and on session end so notes are never lost.</summary>
    public void FlushNotes() => _vm.FlushNow();

    /// <summary>Offset the card sits at when fully hidden, by edge.
    /// Right edge -> card slides off to the right (+W), etc.</summary>
    private (double x, double y) HiddenOffset()
    {
        return _state.Edge switch
        {
            EdgePosition.Right => (ActualWidth, 0),
            EdgePosition.Left => (-ActualWidth, 0),
            EdgePosition.Top => (0, -ActualHeight),
            EdgePosition.Bottom => (0, ActualHeight),
            _ => (0, 0),
        };
    }

    private bool IsHorizontal => _state.Edge is EdgePosition.Left or EdgePosition.Right;

    public void ShowPanel()
    {
        if (IsVisible) return;          // already visible or sliding in
        var off = HiddenOffset();
        Slide.X = off.x; Slide.Y = off.y;
        Visibility = Visibility.Visible;
        _settledVisible = false;

        if (IsHorizontal)
        {
            var a = new DoubleAnimation(off.x, 0, SlideDur) { EasingFunction = new CubicEase() };
            a.Completed += (_, _) => _settledVisible = true;
            Slide.BeginAnimation(TranslateTransform.XProperty, a);
        }
        else
        {
            var a = new DoubleAnimation(off.y, 0, SlideDur) { EasingFunction = new CubicEase() };
            a.Completed += (_, _) => _settledVisible = true;
            Slide.BeginAnimation(TranslateTransform.YProperty, a);
        }
    }

    public void HidePanel()
    {
        if (!IsVisible) return;
        // If the slide-in hasn't finished, the user has barely seen the panel;
        // let it settle rather than immediately reversing.
        if (!_settledVisible) return;
        _settledVisible = false;
        var off = HiddenOffset();

        if (IsHorizontal)
        {
            var a = new DoubleAnimation(0, off.x, SlideDur) { EasingFunction = new CubicEase() };
            a.Completed += (_, _) => Visibility = Visibility.Hidden;
            Slide.BeginAnimation(TranslateTransform.XProperty, a);
        }
        else
        {
            var a = new DoubleAnimation(0, off.y, SlideDur) { EasingFunction = new CubicEase() };
            a.Completed += (_, _) => Visibility = Visibility.Hidden;
            Slide.BeginAnimation(TranslateTransform.YProperty, a);
        }
    }

    /// <summary>No-animation hide (used on startup and "hide to tray").</summary>
    public void HidePanelImmediate()
    {
        Slide.BeginAnimation(TranslateTransform.XProperty, null);
        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        var off = HiddenOffset();
        Slide.X = off.x; Slide.Y = off.y;
        Visibility = Visibility.Hidden;
        _settledVisible = false;
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _state.TogglePin();
        // Pinned state is reflected via the Tag trigger in PinHeaderBtn style, so the
        // fill brush stays a DynamicResource (PinFill) and re-themes on theme switch.
        // Tag must be a string "True"/"False" to match the XAML trigger Value.
        PinBtn.Tag = _state.IsPinned ? "True" : "False";
    }

    // ⚙ / ← toggles between the work view and the settings view (no popup).
    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        => _vm.ToggleSettingsCommand.Execute(null);

    private void UpdateSettingsIcon()
    {
        bool inSettings = _vm.CurrentView == AppView.Settings;
        SettingsIcon.Data = (System.Windows.Media.Geometry)FindResource(inSettings ? "BackGlyph" : "SettingsGlyph");
        SettingsBtn.ToolTip = inSettings ? "返回工作台" : "设置";
    }

    // ✕ = flush notes, then hide everything to the tray (app keeps running).
    private void HideBtn_Click(object sender, RoutedEventArgs e)
    {
        FlushNotes();
        _state.HideToTray();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Last-chance flush on real close (tray 退出 / shutdown).
        FlushNotes();
        base.OnClosing(e);
    }
}
