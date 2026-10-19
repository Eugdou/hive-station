using System.Windows.Input;
using WorkingCenter.Models;

namespace WorkingCenter.ViewModels;

/// <summary>One task row. Holds an edit buffer (so live typing never mutates
/// the model until commit). Begin/Commit/Cancel mirror the original web app's
/// inline edit (Enter/blur save, Esc cancel, blank keeps the old title).</summary>
internal sealed class TodoTaskViewModel : ViewModelBase
{
    private readonly ITaskHost _host;
    public TodoTask Model { get; }

    public string Id => Model.Id;
    public string ProjectId => Model.ProjectId;

    private string _title;
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (SetProperty(ref _isCompleted, value))
            {
                Model.IsCompleted = value;
                Model.CompletedAt = value ? DateTime.UtcNow : null;
                Model.UpdatedAt = DateTime.UtcNow;
                _host.OnTaskMutated(this);
            }
        }
    }

    private bool _isUrgent;
    public bool IsUrgent
    {
        get => _isUrgent;
        set
        {
            if (SetProperty(ref _isUrgent, value))
            {
                Model.IsUrgent = value;
                Model.UpdatedAt = DateTime.UtcNow;
                _host.OnTaskMutated(this);
            }
        }
    }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetProperty(ref _isEditing, value))
                _host.OnEditingChanged();
        }
    }

    private string _editBuffer = "";
    public string EditBuffer { get => _editBuffer; set => SetProperty(ref _editBuffer, value); }

    // inline delete-confirmation (no popup): DeleteCommand enters this state,
    // ConfirmDeleteCommand commits the real delete, CancelDeleteCommand backs out.
    private bool _isDeleteConfirm;
    public bool IsDeleteConfirm { get => _isDeleteConfirm; set => SetProperty(ref _isDeleteConfirm, value); }

    // ---- detail (stage 4) -------------------------------------------------
    // Detail is bound directly (no commit buffer): it's free text that auto-saves,
    // with no blank rejection. Each keystroke marks the task's detail dirty and
    // schedules a debounced snapshot save (handled by the host).
    private string _detail;
    public string Detail
    {
        get => _detail;
        set
        {
            if (SetProperty(ref _detail, value))
            {
                Model.Detail = value;
                _host.OnDetailChanged(this);
            }
        }
    }

    private bool _isDetailOpen;
    public bool IsDetailOpen { get => _isDetailOpen; set => SetProperty(ref _isDetailOpen, value); }

    private string _detailStatus = "";
    public string DetailStatus { get => _detailStatus; set => SetProperty(ref _detailStatus, value); }

    public ICommand BeginEditCommand { get; }
    public ICommand CommitEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand ToggleUrgentCommand { get; }
    public ICommand ToggleDetailCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand CancelDeleteCommand { get; }

    public TodoTaskViewModel(TodoTask model, ITaskHost host)
    {
        Model = model;
        _host = host;
        _title = model.Title;
        _isCompleted = model.IsCompleted;
        _isUrgent = model.IsUrgent;
        _detail = model.Detail;

        BeginEditCommand = new RelayCommand(() => _host.BeginEdit(this));
        CommitEditCommand = new RelayCommand(CommitEdit);
        CancelEditCommand = new RelayCommand(CancelEdit);
        ToggleUrgentCommand = new RelayCommand(() => IsUrgent = !IsUrgent);
        ToggleDetailCommand = new RelayCommand(() => IsDetailOpen = !IsDetailOpen);
        // Delete shows an inline confirm row (no popup); confirm/cancel below.
        DeleteCommand = new RelayCommand(() => { _host.BeginDeleteTask(this); });
        ConfirmDeleteCommand = new RelayCommand(() => _host.CommitDeleteTask(this));
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirm = false);
    }

    /// <summary>Called by the host when this task enters edit mode.</summary>
    public void EnterEdit()
    {
        EditBuffer = Title;
        IsEditing = true;
    }

    private void CommitEdit()
    {
        var v = EditBuffer.Trim();
        if (!string.IsNullOrEmpty(v))
        {
            Model.Title = v;
            Model.UpdatedAt = DateTime.UtcNow;
            Title = v;
            _host.OnTaskMutated(this);
        }
        IsEditing = false;
    }

    internal void CancelEdit()
    {
        EditBuffer = Title;
        IsEditing = false;
    }
}

/// <summary>Callback surface the workspace implements, so TodoTaskViewModel
/// doesn't depend on the concrete WorkspaceViewModel.</summary>
internal interface ITaskHost
{
    void BeginEdit(TodoTaskViewModel task);      // cancels any other task being edited
    void OnTaskMutated(TodoTaskViewModel task);  // persist + rebuild groups
    void OnDetailChanged(TodoTaskViewModel task);// debounced save of task detail + status
    void OnEditingChanged();                     // recompute IsEditingAny
    void BeginDeleteTask(TodoTaskViewModel task);// show inline delete-confirm row
    void CommitDeleteTask(TodoTaskViewModel task);// actually remove + persist
}
