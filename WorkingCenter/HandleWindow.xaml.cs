using System.Windows;
using System.Windows.Input;
using WorkingCenter.Core;

namespace WorkingCenter;

public partial class HandleWindow : Window
{
    private readonly AppState _state;

    internal HandleWindow(AppState state)
    {
        InitializeComponent();
        _state = state;
        MouseEnter += (_, _) => _state.OnHandleMouseEnter();
        MouseLeave += (_, _) => _state.OnHandleMouseLeave();
    }
}
