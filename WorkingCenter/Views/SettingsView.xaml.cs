using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkingCenter.Core;
using WorkingCenter.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WorkingCenter.Views;

public partial class SettingsView : UserControl
{
    private WorkspaceViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm = DataContext as WorkspaceViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += Vm_PropertyChanged;
                UpdateEdgeRadios();
                UpdateThemeRadios();
            }
        };
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.Edge)) UpdateEdgeRadios();
        else if (e.PropertyName == nameof(WorkspaceViewModel.Theme)) UpdateThemeRadios();
    }

    private void UpdateEdgeRadios()
    {
        if (_vm == null) return;
        EdgeLeft.IsChecked = _vm.Edge == EdgePosition.Left;
        EdgeRight.IsChecked = _vm.Edge == EdgePosition.Right;
    }

    private void UpdateThemeRadios()
    {
        if (_vm == null) return;
        LightRadio.IsChecked = _vm.Theme == ThemeService.Theme.Light;
        DarkRadio.IsChecked = _vm.Theme == ThemeService.Theme.Dark;
    }

    private void AddProj_Click(object sender, RoutedEventArgs e) => DoAdd();
    private void NewProjName_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) DoAdd(); }

    private void DoAdd()
    {
        if (_vm == null) return;
        if (_vm.AddProject(NewProjName.Text))
            NewProjName.Text = "";
    }

    // ---- inline project rename / delete (no popup) ----
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null && IdOf(sender) is string id) _vm.BeginRenameProject(id);
    }

    private void SaveRename_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null && IdOf(sender) is string id) _vm.CommitRenameProject(id);
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        var id = IdOf(sender);
        if (id == null) return;
        if (e.Key == Key.Enter) _vm.CommitRenameProject(id);
        else if (e.Key == Key.Escape) _vm.CancelProjectAction();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null && IdOf(sender) is string id) _vm.BeginDeleteProject(id);
    }

    private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null && IdOf(sender) is string id) _vm.CommitDeleteProject(id);
    }

    private void CancelProj_Click(object sender, RoutedEventArgs e)
    {
        _vm?.CancelProjectAction();
    }

    // ---- project reorder (up/down icon buttons, click = one slot) ----------

    private void ProjectUp_Click(object sender, RoutedEventArgs e)
    {
        if (IdOf(sender) is string id) _vm?.MoveProjectUp(id);
    }

    private void ProjectDown_Click(object sender, RoutedEventArgs e)
    {
        if (IdOf(sender) is string id) _vm?.MoveProjectDown(id);
    }

    private static string? IdOf(object sender) => (sender as FrameworkElement)?.Tag as string;
}
