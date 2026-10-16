using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkingCenter.ViewModels;

/// <summary>Attached behavior: Enter submits (fires SubmitCommand), Esc cancels
/// (fires CancelCommand), but ONLY when not mid-IME-composition. Guards the add
/// task input and inline edit against the Enter that confirms a Chinese
/// composition candidate. Uses TextCompositionManager events to track composition
/// state per control. (Note: these are a best-effort hint; the panel hide itself
/// is pure pointer-driven and does not rely on this — see AppState.)</summary>
internal static class ImeBehavior
{
    public static readonly DependencyProperty SubmitCommandProperty =
        DependencyProperty.RegisterAttached("SubmitCommand", typeof(ICommand), typeof(ImeBehavior),
            new PropertyMetadata(null, OnSubmitChanged));
    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.RegisterAttached("CancelCommand", typeof(ICommand), typeof(ImeBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty IsComposingProperty =
        DependencyProperty.RegisterAttached("IsComposing", typeof(bool), typeof(ImeBehavior), new PropertyMetadata(false));
    private static readonly DependencyProperty JustCommittedProperty =
        DependencyProperty.RegisterAttached("JustCommitted", typeof(bool), typeof(ImeBehavior), new PropertyMetadata(false));

    public static void SetSubmitCommand(DependencyObject d, ICommand v) => d.SetValue(SubmitCommandProperty, v);
    public static ICommand GetSubmitCommand(DependencyObject d) => (ICommand)d.GetValue(SubmitCommandProperty);
    public static void SetCancelCommand(DependencyObject d, ICommand v) => d.SetValue(CancelCommandProperty, v);
    public static ICommand GetCancelCommand(DependencyObject d) => (ICommand)d.GetValue(CancelCommandProperty);

    private static void OnSubmitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb || e.NewValue == null) return;
        tb.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Escape) return;
        var tb = (TextBox)sender;

        if (e.Key == Key.Escape)
        {
            var cancel = GetCancelCommand(tb);
            if (cancel?.CanExecute(null) == true) { cancel.Execute(null); e.Handled = true; }
            return;
        }

        // Enter:
        bool justCommitted = (bool)tb.GetValue(JustCommittedProperty);
        if (justCommitted)
        {
            // The Enter that accepted the IME candidate — swallow it so it
            // doesn't also submit.
            tb.SetValue(JustCommittedProperty, false);
            e.Handled = true;
            return;
        }
        if ((bool)tb.GetValue(IsComposingProperty)) return; // let IME commit; don't handle

        var cmd = GetSubmitCommand(tb);
        if (cmd?.CanExecute(null) == true) { cmd.Execute(null); e.Handled = true; }
    }
}
