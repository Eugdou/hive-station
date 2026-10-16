using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WorkingCenter.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkingCenter.Views;

public partial class Workspace : UserControl
{
    private WorkspaceViewModel? _vm;

    public Workspace()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm = DataContext as WorkspaceViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += Vm_PropertyChanged;
                WireDialogs();
            }
        };
    }

    // push UI focus flags into the VM so it can report IsEditingAny (which
    // pauses the panel auto-hide while the user is in the notes / add box)
    private void Notes_GotFocus(object sender, RoutedEventArgs e) => SetNotesFocused(true);
    private void Notes_LostFocus(object sender, RoutedEventArgs e) => SetNotesFocused(false);
    private void AddInput_GotFocus(object sender, RoutedEventArgs e) => SetAddFocused(true);
    private void AddInput_LostFocus(object sender, RoutedEventArgs e) => SetAddFocused(false);

    private void SetNotesFocused(bool v) { if (_vm != null) _vm.IsNotesFocused = v; }
    private void SetAddFocused(bool v) { if (_vm != null) _vm.IsAddInputFocused = v; }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // (IsEditingAny is consumed by PanelWindow; nothing to do here.)
    }

    // commit inline edit when the edit box loses focus (Enter already commits
    // via the command; this covers clicks elsewhere). Guard against double-commit.
    private void EditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TodoTaskViewModel tvm && tvm.IsEditing)
            tvm.CommitEditCommand.Execute(null);
    }

    // ---- bridge VM dialog requests to real WPF dialogs -------------------
    private void WireDialogs()
    {
        _vm!.RequestConfirm = (title, msg) => ConfirmDialog.Show(title, msg);
        _vm.RequestConfirmWithChoice = (title, msg, choices) => ConfirmDialog.ShowWithChoice(title, msg, choices);
        _vm.RequestAlert = msg => ConfirmDialog.Show("提示", msg);
        // Project management now lives in the settings page (no popup), so
        // RequestOpenProjectManager is unused here.
    }
}
