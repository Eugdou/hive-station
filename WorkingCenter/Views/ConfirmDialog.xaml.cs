using System.Windows;

namespace WorkingCenter.Views;

public partial class ConfirmDialog : Window
{
    private bool _ok;

    private ConfirmDialog()
    {
        InitializeComponent();
    }

    public static bool Show(string title, string message)
    {
        var d = new ConfirmDialog();
        d.TitleText.Text = title;
        d.MsgText.Text = message;
        d.ChoicePanel.Visibility = Visibility.Collapsed;
        d.InputBox.Visibility = Visibility.Collapsed;
        d.ShowDialog();
        return d._ok;
    }

    public static (bool ok, string? choice) ShowWithChoice(string title, string message, System.Collections.Generic.IEnumerable<string> choices)
    {
        var d = new ConfirmDialog();
        d.TitleText.Text = title;
        d.MsgText.Text = message;
        d.ChoicePanel.Visibility = Visibility.Visible;
        d.InputBox.Visibility = Visibility.Collapsed;
        foreach (var c in choices) d.ChoiceBox.Items.Add(c);
        if (d.ChoiceBox.Items.Count > 0) d.ChoiceBox.SelectedIndex = 0;
        d.ShowDialog();
        return (d._ok, d._ok ? d.ChoiceBox.SelectedItem as string : null);
    }

    public static string? ShowInput(string title, string message, string? initial = null)
    {
        var d = new ConfirmDialog();
        d.TitleText.Text = title;
        d.MsgText.Text = message;
        d.ChoicePanel.Visibility = Visibility.Collapsed;
        d.InputBox.Visibility = Visibility.Visible;
        d.InputBox.Text = initial ?? "";
        d.InputBox.SelectAll();
        d.InputBox.Focus();
        d.ShowDialog();
        return d._ok ? d.InputBox.Text : null;
    }

    private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { _ok = true; Close(); }
        else if (e.Key == System.Windows.Input.Key.Escape) { _ok = false; Close(); }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _ok = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _ok = false;
        Close();
    }
}
