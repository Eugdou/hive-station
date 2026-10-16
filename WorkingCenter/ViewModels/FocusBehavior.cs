using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkingCenter.ViewModels;

/// <summary>Attached behavior: when the bound bool becomes true, focus + select
/// all in the TextBox. Handles both entering edit mode and re-focusing after a
/// group rebuild (the TextBox container is recreated but IsEditing stays true,
/// so this re-focuses it — mirroring the original render()'s inp.focus().select()).</summary>
internal static class FocusBehavior
{
    public static readonly DependencyProperty IsFocusedProperty =
        DependencyProperty.RegisterAttached("IsFocused", typeof(bool), typeof(FocusBehavior),
            new PropertyMetadata(false, OnIsFocusedChanged));

    public static void SetIsFocused(DependencyObject d, bool v) => d.SetValue(IsFocusedProperty, v);
    public static bool GetIsFocused(DependencyObject d) => (bool)d.GetValue(IsFocusedProperty);

    private static void OnIsFocusedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox tb && (bool)e.NewValue)
        {
            // Defer to next render pass so the container is fully loaded.
            tb.Dispatcher.BeginInvoke(new Action(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }));
        }
    }
}
