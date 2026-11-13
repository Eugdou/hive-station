using WorkingCenter.Core;
using WorkingCenter.Models;

namespace WorkingCenter.ViewModels;

/// <summary>Callback surface the workspace implements for reminder rows.</summary>
internal interface IReminderHost
{
    void BeginEditReminder(ReminderItemViewModel item);
    void OnReminderItemToggled(ReminderItemViewModel item);   // enabled checkbox
    void DeleteReminderItem(ReminderItemViewModel item);       // shows inline confirm
    void CommitDeleteReminder(string id);
    void CancelReminderAction();
    string CommitEditReminder(ReminderItemViewModel item);     // returns error text or ""
}

/// <summary>One reminder row in the settings list. Holds edit buffers so live
/// typing never mutates the model until commit (same pattern as projects).</summary>
internal sealed class ReminderItemViewModel : ViewModelBase
{
    private readonly IReminderHost _host;
    public Reminder Model { get; }

    public string Id => Model.Id;
    public ReminderRecurrence Recurrence => Model.Recurrence;

    // ---- display ----
    public string Title { get => Model.Title; set { Model.Title = value; RefreshDisplay(); } }

    /// <summary>如 "每周三 09:30"，由 ReminderRules 推导。</summary>
    public string Label => ReminderRules.Describe(Model);

    /// <summary>标题/循环描述变化后由 VM 调用，刷新行内两段文本。</summary>
    public void RefreshDisplay()
    {
        Raise(nameof(Title));
        Raise(nameof(Label));
    }

    // 编辑哪些附加字段的可见性（编辑行用）
    public bool IsOnce => Model.Recurrence == ReminderRecurrence.Once;
    public bool IsDaily => Model.Recurrence == ReminderRecurrence.Daily;
    public bool IsWeekly => Model.Recurrence == ReminderRecurrence.Weekly;
    public bool IsMonthly => Model.Recurrence == ReminderRecurrence.Monthly;
    /// <summary>周/月共用的"第 X"选择（周一=1..7 / 1..31）。</summary>
    public bool HasDay => IsWeekly || IsMonthly;

    // ---- enabled ----
    public bool IsEnabled
    {
        get => _enabled;
        set { if (SetProperty(ref _enabled, value)) _host.OnReminderItemToggled(this); }
    }
    private bool _enabled;

    // ---- inline edit buffers ----
    private string _editTitle = "";
    public string EditTitle { get => _editTitle; set => SetProperty(ref _editTitle, value); }

    private string _editTime = "";
    public string EditTime { get => _editTime; set => SetProperty(ref _editTime, value); }

    private string _editDate = "";
    public string EditDate { get => _editDate; set => SetProperty(ref _editDate, value); }

    private int _editDay = 1;
    public int EditDay
    {
        get => _editDay;
        set
        {
            if (SetProperty(ref _editDay, Math.Clamp(value, 1, 31)))
                Raise(nameof(EditDayIndex));
        }
    }

    /// <summary>周/月 ComboBox 的 SelectedIndex（0 起）。周一=1..7 / 1..31 日的下标化。</summary>
    public int EditDayIndex
    {
        get => EditDay - 1;
        set { if (value >= 0) EditDay = value + 1; }
    }

    private bool _isEditing;
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

    private bool _isDeleteConfirm;
    public bool IsDeleteConfirm { get => _isDeleteConfirm; set => SetProperty(ref _isDeleteConfirm, value); }

    private string _editError = "";
    public string EditError { get => _editError; set => SetProperty(ref _editError, value); }

    public ReminderItemViewModel(Reminder model, IReminderHost host)
    {
        Model = model;
        _host = host;
        _enabled = model.Enabled;
    }

    public void BeginEdit()
    {
        EditTitle = Model.Title;
        EditTime = Model.Time;
        EditDate = Model.OnceDate;
        EditDay = Model.Day;
        EditError = "";
        IsDeleteConfirm = false;
        IsEditing = true;
    }
}
