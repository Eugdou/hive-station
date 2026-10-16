using System.Windows.Media;
using WorkingCenter.Models;
using Brush = System.Windows.Media.Brush;

namespace WorkingCenter.ViewModels;

/// <summary>A project as the dropdown / manager / group header sees it.</summary>
internal sealed class ProjectViewModel : ViewModelBase
{
    private string _name;
    public string Id { get; }
    public int ColorIndex { get; }
    public int SortOrder { get; set; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public Brush DotBrush => ProjectPalette.BrushFor(ColorIndex);

    // ---- inline rename / delete-confirm (no popup) ----------------------
    private string _editBuffer = "";
    public string EditBuffer { get => _editBuffer; set => SetProperty(ref _editBuffer, value); }

    private bool _isRenameMode;
    public bool IsRenameMode { get => _isRenameMode; set => SetProperty(ref _isRenameMode, value); }

    private bool _isDeleteConfirm;
    public bool IsDeleteConfirm { get => _isDeleteConfirm; set => SetProperty(ref _isDeleteConfirm, value); }

    // when deleting a project that has tasks, we need to choose a target to move
    // them to. These hold that inline choice (set by the settings page's inline UI).
    private List<ProjectViewModel> _moveTargets = new();
    public List<ProjectViewModel> MoveTargets
    {
        get => _moveTargets;
        set { SetProperty(ref _moveTargets, value); Raise(nameof(HasMoveTargets)); }
    }
    public bool HasMoveTargets => MoveTargets.Count > 0;
    private ProjectViewModel? _moveTarget;
    public ProjectViewModel? MoveTarget { get => _moveTarget; set => SetProperty(ref _moveTarget, value); }

    public ProjectViewModel(Project p)
    {
        Id = p.Id;
        _name = p.Name;
        ColorIndex = p.ColorIndex;
        SortOrder = p.SortOrder;
    }

    public void BeginRename() { EditBuffer = Name; IsRenameMode = true; }
    public void CancelRename() { EditBuffer = Name; IsRenameMode = false; }

    public override string ToString() => Name; // for ComboBox display fallback
}
