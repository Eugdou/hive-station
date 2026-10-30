using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WorkingCenter.Core;
using WorkingCenter.Data;
using WorkingCenter.Models;

namespace WorkingCenter.ViewModels;

/// <summary>Root view model for the workspace. Owns the in-memory AppData,
/// the debounced notes save, the grouped overview, and all commands.
/// Persistence timing: projects/tasks save IMMEDIATELY on every mutation
/// (like the original localStorage); notes save DEBOUNCED (with force-flush + a
/// status indicator) to avoid data loss. Depends only on IStore — stage 5
/// swaps JSON for SQLite behind it with no VM change.</summary>
/// <summary>Which top-level view the panel shows.</summary>
public enum AppView { Work, Settings }

internal sealed class WorkspaceViewModel : ViewModelBase, ITaskHost
{
    private readonly IStore _store;
    private readonly AppData _data;

    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _maxWait;
    private bool _noteDirty;
    private string? _detailDirtyId;   // id of the task whose detail has unsaved edits

    // stable VM caches (survive a group rebuild so inline-edit keeps focus)
    private readonly Dictionary<string, TodoTaskViewModel> _taskVm = new();
    private readonly Dictionary<string, ProjectViewModel> _projVm = new();

    private string? _editingTaskId;

    // ---- properties --------------------------------------------------------

    private string _noteContent;
    public string NoteContent { get => _noteContent; set { if (SetProperty(ref _noteContent, value)) ScheduleNoteSave(); } }

    private string _saveStatus = "已保存";
    public string SaveStatus { get => _saveStatus; set => SetProperty(ref _saveStatus, value); }

    private string _newTaskTitle = "";
    public string NewTaskTitle { get => _newTaskTitle; set => SetProperty(ref _newTaskTitle, value); }

    private ProjectViewModel? _selectedProject;
    public ProjectViewModel? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                _data.SelectedProjectId = value?.Id;
                PersistImmediately();
            }
        }
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = new();
    public ObservableCollection<ProjectGroupViewModel> Groups { get; } = new();

    public bool IsEditingAny
    {
        get => _isEditingAny;
        private set => SetProperty(ref _isEditingAny, value);
    }
    private bool _isEditingAny;

    // UI focus flags pushed from the view's GotFocus/LostFocus handlers.
    // When the notes box or add-task box has focus, treat it as "editing" so the
    // panel doesn't auto-hide while the user is actively in a text field.
    public bool IsNotesFocused
    {
        get => _isNotesFocused;
        set { if (SetProperty(ref _isNotesFocused, value)) RecomputeEditingAny(); }
    }
    private bool _isNotesFocused;

    public bool IsAddInputFocused
    {
        get => _isAddInputFocused;
        set { if (SetProperty(ref _isAddInputFocused, value)) RecomputeEditingAny(); }
    }
    private bool _isAddInputFocused;

    // empty-state for the "还没有任务" line
    public bool HasTasks => _data.Tasks.Count > 0;

    // ---- commands ---------------------------------------------------------

    public ICommand AddTaskCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ClearCommand { get; }              // opens the inline red confirm
    public ICommand ConfirmClearCommand { get; }      // executes the actual clear
    public ICommand CancelClearCommand { get; }
    public ICommand DismissDataNoticeCommand { get; } // 好的：closes green/red inline notices
    public ICommand ToggleSettingsCommand { get; }
    public ICommand SetEdgeLeftCommand { get; }
    public ICommand SetEdgeRightCommand { get; }
    public ICommand SetThemeLightCommand { get; }
    public ICommand SetThemeDarkCommand { get; }

    // callbacks the view code-behind hooks (dialog windows). Plain delegates
    // (not events) so the view can assign them directly.
    // (All former RequestConfirm/RequestAlert popup delegates removed —
    //  every notice is inline now.)
    public Action<EdgePosition>? RequestSetEdge;   // apply edge position (AppState)

    // ---- view switching (work <-> settings) -------------------------------
    public AppView CurrentView { get => _currentView; set => SetProperty(ref _currentView, value); }
    private AppView _currentView = AppView.Work;

    // edge position (left/right) for the dock; persisted by AppState.
    public EdgePosition Edge { get => _edge; private set => SetProperty(ref _edge, value); }
    private EdgePosition _edge = EdgePosition.Right;

    public WorkspaceViewModel(IStore store)
    {
        _store = store;
        _data = store.Load();

        _noteContent = _data.Note.Content;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _maxWait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _debounce.Tick += (_, _) => FlushNow();
        _maxWait.Tick += (_, _) => FlushNow();

        BuildProjectVms();
        RebuildGroups();
        SelectedProject = _data.SelectedProjectId != null
            ? Projects.FirstOrDefault(p => p.Id == _data.SelectedProjectId)
            : Projects.FirstOrDefault();

        AddTaskCommand = new RelayCommand(AddTask, () => !string.IsNullOrWhiteSpace(NewTaskTitle));
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        ClearCommand = new RelayCommand(() => { DataNotice = ""; DataError = ""; IsClearConfirm = true; });
        ConfirmClearCommand = new RelayCommand(() => { DoClearAll(); });
        CancelClearCommand = new RelayCommand(() => IsClearConfirm = false);
        ToggleSettingsCommand = new RelayCommand(() =>
            CurrentView = CurrentView == AppView.Settings ? AppView.Work : AppView.Settings);
        SetEdgeLeftCommand = new RelayCommand(() => SetEdge(EdgePosition.Left));
        SetEdgeRightCommand = new RelayCommand(() => SetEdge(EdgePosition.Right));
        SetThemeLightCommand = new RelayCommand(() => SetTheme(WorkingCenter.Views.ThemeService.Theme.Light));
        SetThemeDarkCommand = new RelayCommand(() => SetTheme(WorkingCenter.Views.ThemeService.Theme.Dark));
        DismissDataNoticeCommand = new RelayCommand(() => { DataNotice = ""; DataError = ""; });
    }

    /// <summary>数据管理的内联提示（绿色，导入/导出成功），空串隐藏，【好的】关闭。</summary>
    public string DataNotice { get => _dataNotice; set => SetProperty(ref _dataNotice, value); }
    private string _dataNotice = "";

    /// <summary>数据管理的内联错误（红色，导入/导出失败），空串隐藏，【好的】关闭。</summary>
    public string DataError { get => _dataError; set => SetProperty(ref _dataError, value); }
    private string _dataError = "";

    /// <summary>「清除全部」的内联红色二次确认开关（不再弹窗）。</summary>
    public bool IsClearConfirm { get => _isClearConfirm; set => SetProperty(ref _isClearConfirm, value); }
    private bool _isClearConfirm;

    private void DoClearAll()
    {
        _data.Tasks.Clear();
        _data.Note.Content = "";
        _noteContent = "";
        Raise(nameof(NoteContent));
        _taskVm.Clear();
        _editingTaskId = null;
        PersistImmediately();
        RebuildGroups();
        RecomputeEditingAny();
        IsClearConfirm = false;
    }

    /// <summary>开机自启失败的内联红字提示，空串隐藏。</summary>
    public string AutoStartError { get => _autoStartError; set => SetProperty(ref _autoStartError, value); }
    private string _autoStartError = "";

    /// <summary>开机自启开关。注册表 HKCU Run 键持久化，默认关闭；
    /// setter 写注册表，失败则回弹 UI 状态并提示。</summary>
    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        set
        {
            if (_autoStartEnabled == value) return;
            bool ok = value ? WorkingCenter.Core.AutoStart.Enable() : WorkingCenter.Core.AutoStart.Disable();
            if (ok) SetProperty(ref _autoStartEnabled, value);
            else
            {
                Raise(nameof(AutoStartEnabled));   // revert the checkbox
                AutoStartError = value ? "开机自启设置失败（可能被安全软件拦截）。" : "取消开机自启失败。";
            }
        }
    }
    private bool _autoStartEnabled = WorkingCenter.Core.AutoStart.IsEnabled();

    public Views.ThemeService.Theme Theme { get => _theme; private set => SetProperty(ref _theme, value); }
    private Views.ThemeService.Theme _theme = Views.ThemeService.Theme.Light;

    private void SetTheme(Views.ThemeService.Theme theme)
    {
        Theme = theme;
        Views.ThemeService.Apply(theme);
        Views.ThemeService.Save(theme);
    }

    public void InitTheme(Views.ThemeService.Theme theme) { _theme = theme; Raise(nameof(Theme)); }

    private void SetEdge(EdgePosition edge)
    {
        Edge = edge;
        RequestSetEdge?.Invoke(edge);
    }

    /// <summary>Called by PanelWindow on startup with the persisted edge so the
    /// settings page shows the right selection.</summary>
    public void InitEdge(EdgePosition edge) { _edge = edge; Raise(nameof(Edge)); }

    // ---- notes + detail (debounced save) --------------------------------

    private void ScheduleNoteSave()
    {
        _noteDirty = true;
        SaveStatus = "正在保存…";
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _debounce.Stop(); _debounce.Start();
        if (!_maxWait.IsEnabled) _maxWait.Start();
    }

    /// <summary>Force-write any pending (debounced) changes now — notes and/or a
    /// task's detail. Called by the timer tick and by all external flushers.</summary>
    public void FlushNow()
    {
        if (!_noteDirty && _detailDirtyId == null) return;
        _debounce.Stop(); _maxWait.Stop();

        if (_noteDirty)
        {
            _data.Note.Content = _noteContent;
            _data.Note.UpdatedAt = DateTime.UtcNow;
            _noteDirty = false;
            SaveStatus = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
        }

        var detailId = _detailDirtyId;
        _detailDirtyId = null;
        _store.Save(_data);   // single atomic snapshot save (covers notes + detail)

        if (detailId != null && _taskVm.TryGetValue(detailId, out var tvm))
            tvm.DetailStatus = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
    }

    // ---- projects --------------------------------------------------------

    private void BuildProjectVms()
    {
        _projVm.Clear();
        Projects.Clear();
        foreach (var p in _data.Projects.OrderBy(p => p.SortOrder))
        {
            var vm = new ProjectViewModel(p);
            _projVm[p.Id] = vm;
            Projects.Add(vm);
        }
    }

    /// <summary>Re-sync project VMs from the (possibly mutated) data — used after
    /// the project manager edits projects. Reuses existing VMs by Id so the
    /// dropdown selection and bindings stay alive where possible.</summary>
    public void RefreshProjects()
    {
        // update names / order on existing VMs, add new ones, drop removed ones
        var live = new HashSet<string>();
        int order = 0;
        foreach (var p in _data.Projects.OrderBy(x => x.SortOrder))
        {
            live.Add(p.Id);
            if (_projVm.TryGetValue(p.Id, out var vm))
            {
                vm.Name = p.Name;
                vm.SortOrder = order++;
            }
            else
            {
                vm = new ProjectViewModel(p) { SortOrder = order++ };
                _projVm[p.Id] = vm;
            }
        }
        // remove dead
        foreach (var dead in _projVm.Keys.Except(live).ToList())
        {
            var vm = _projVm[dead];
            _projVm.Remove(dead);
            Projects.Remove(vm);
        }
        // rebuild ordered Projects collection
        Projects.Clear();
        foreach (var p in _data.Projects.OrderBy(x => x.SortOrder))
            if (_projVm.TryGetValue(p.Id, out var vm)) Projects.Add(vm);

        // keep a valid selection
        if (SelectedProject == null || !_projVm.ContainsKey(SelectedProject.Id))
            SelectedProject = Projects.FirstOrDefault();
        RebuildGroups();
    }

    // ---- reorder projects (settings page up/down arrows) -------------------
    // Click moves the project one slot; SortOrders are rewritten 0..n-1,
    // persisted, then RefreshProjects + RebuildGroups propagate to the
    // dropdown and the grouped todo list.

    /// <summary>Move `id` up one slot (towards the top). No-op at top.</summary>
    public void MoveProjectUp(string id)
    {
        var ordered = _data.Projects.OrderBy(p => p.SortOrder).ToList();
        int i = ordered.FindIndex(p => p.Id == id);
        if (i <= 0) return;
        (ordered[i - 1], ordered[i]) = (ordered[i], ordered[i - 1]);
        ApplyProjectOrder(ordered);
    }

    /// <summary>Move `id` down one slot (towards the bottom). No-op at bottom.</summary>
    public void MoveProjectDown(string id)
    {
        var ordered = _data.Projects.OrderBy(p => p.SortOrder).ToList();
        int i = ordered.FindIndex(p => p.Id == id);
        if (i < 0 || i >= ordered.Count - 1) return;
        (ordered[i + 1], ordered[i]) = (ordered[i], ordered[i + 1]);
        ApplyProjectOrder(ordered);
    }

    private void ApplyProjectOrder(List<Project> ordered)
    {
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
            ordered[i].UpdatedAt = DateTime.UtcNow;
        }
        PersistImmediately();
        RefreshProjects();
        Logger.Info("Project order changed: " + string.Join(" > ", ordered.Select(p => p.Name)));
    }

    // ---- tasks -----------------------------------------------------------

    private void AddTask()
    {
        var v = NewTaskTitle.Trim();
        if (string.IsNullOrEmpty(v)) return;
        var proj = SelectedProject;
        var now = DateTime.UtcNow;
        var t = new TodoTask
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = proj?.Id ?? "",
            Title = v,
            Detail = "",
            IsCompleted = false,
            IsUrgent = false,
            SortOrder = (_data.Tasks.Count > 0 ? _data.Tasks.Max(x => x.SortOrder) : -1) + 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _data.Tasks.Add(t);
        NewTaskTitle = "";
        PersistImmediately();
        RebuildGroups();
    }

    void ITaskHost.BeginEdit(TodoTaskViewModel task)
    {
        if (_editingTaskId == task.Id) { task.EnterEdit(); return; }
        // cancel the previously edited task
        if (_editingTaskId != null && _taskVm.TryGetValue(_editingTaskId, out var prev) && prev.IsEditing)
            prev.CancelEdit();
        _editingTaskId = task.Id;
        task.EnterEdit();
    }

    void ITaskHost.OnTaskMutated(TodoTaskViewModel task)
    {
        PersistImmediately();
        RebuildGroups();
    }

    void ITaskHost.OnDetailChanged(TodoTaskViewModel task)
    {
        // Model.Detail is updated live by the binding; just schedule a debounced
        // snapshot save and show a per-task status. Do NOT rebuild groups (that
        // would recreate the detail TextBox and lose the caret mid-typing).
        _detailDirtyId = task.Id;
        task.DetailStatus = "正在保存…";
        ScheduleSave();
    }

    void ITaskHost.OnEditingChanged() => RecomputeEditingAny();

    void ITaskHost.BeginDeleteTask(TodoTaskViewModel task)
    {
        // close any other task's inline confirm first
        foreach (var t in _taskVm.Values) t.IsDeleteConfirm = false;
        task.IsDeleteConfirm = true;
    }

    void ITaskHost.CommitDeleteTask(TodoTaskViewModel task)
    {
        _data.Tasks.RemoveAll(t => t.Id == task.Id);
        if (_editingTaskId == task.Id) _editingTaskId = null;
        _taskVm.Remove(task.Id);
        PersistImmediately();
        RebuildGroups();
        RecomputeEditingAny();
    }

    private void RecomputeEditingAny()
    {
        bool any = _taskVm.Values.Any(t => t.IsEditing) || _isNotesFocused || _isAddInputFocused;
        if (any != _isEditingAny) { _isEditingAny = any; Raise(nameof(IsEditingAny)); }
    }

    // ---- grouped overview -----------------------------------------------

    /// <summary>Partition tasks by project, hide empty groups, 未分类 last, sort
    /// incomplete-before-complete (stable within). Stage 4 prepends urgent-first.
    /// Reuses stable TodoTaskViewModel instances so inline-edit survives.</summary>
    public void RebuildGroups()
    {
        EnsureTaskVms();

        var byProject = _data.Tasks.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        Groups.Clear();

        // ordered projects, then 未分类 last (if any)
        foreach (var p in _data.Projects.OrderBy(x => x.SortOrder))
        {
            if (!byProject.TryGetValue(p.Id, out var list) || list.Count == 0) continue;
            AddGroup(_projVm.GetValueOrDefault(p.Id) ?? new ProjectViewModel(p), list);
        }
        if (byProject.TryGetValue("", out var unc) && unc.Count > 0)
        {
            AddGroup(new ProjectViewModel(new Project { Id = "", Name = "未分类", ColorIndex = 5 }), unc);
        }
        Raise(nameof(HasTasks));
    }

    private static int SortRank(TodoTask t)
    {
        // 3-tier stable ordering (spec §四):
        //   0 = incomplete + urgent (top)
        //   1 = incomplete + not urgent (original order)
        //   2 = completed (any — urgent does NOT keep them on top)
        if (t.IsCompleted) return 2;
        return t.IsUrgent ? 0 : 1;
    }

    private void AddGroup(ProjectViewModel proj, List<TodoTask> list)
    {
        var ordered = list
            .OrderBy(t => SortRank(t))   // urgent-incomplete → incomplete → completed
            .ThenBy(t => t.SortOrder)    // stable within each tier
            .Select(t => _taskVm[t.Id]);
        var g = new ProjectGroupViewModel(proj.Name, proj.DotBrush);
        int done = 0;
        foreach (var vm in ordered) { g.Items.Add(vm); if (vm.IsCompleted) done++; }
        g.DoneCount = done;
        g.TotalCount = list.Count;
        Groups.Add(g);
    }

    private void EnsureTaskVms()
    {
        // add VMs for any new tasks; drop dead ones
        var live = new HashSet<string>();
        foreach (var t in _data.Tasks)
        {
            live.Add(t.Id);
            if (!_taskVm.ContainsKey(t.Id))
            {
                var vm = new TodoTaskViewModel(t, this);
                vm.PropertyChanged += TaskVm_PropertyChanged;
                _taskVm[t.Id] = vm;
            }
        }
        foreach (var dead in _taskVm.Keys.Except(live).ToList())
        {
            if (_taskVm[dead] is TodoTaskViewModel vm) vm.PropertyChanged -= TaskVm_PropertyChanged;
            _taskVm.Remove(dead);
        }
    }

    private void TaskVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodoTaskViewModel.IsEditing)) RecomputeEditingAny();
    }

    // ---- import / export / clear ----------------------------------------

    private void Export()
    {
        FlushNow();
        var dlg = new SaveFileDialog
        {
            Filter = "JSON 备份 (*.json)|*.json",
            FileName = "hivestation-backup-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var snapshot = new
            {
                note = _data.Note,
                projects = _data.Projects,
                tasks = _data.Tasks,
                selectedProjectId = _data.SelectedProjectId,
                exportedAt = DateTime.UtcNow,
            };
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.WriteAllText(dlg.FileName, json);
            DataNotice = "已导出备份。";
        }
        catch (Exception ex)
        {
            DataError = "导出失败：" + ex.Message;
        }
    }

    private void Import()
    {
        var dlg = new OpenFileDialog { Filter = "JSON 备份 (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var json = File.ReadAllText(dlg.FileName);
            if (!DataMigrator.TryImport(json, out var imported, out var error))
            {
                DataError = error;
                return;
            }
            // backup current before replacing
            BackupCurrent();
            _data.Projects = imported.Projects;
            _data.Tasks = imported.Tasks;
            _data.Note = imported.Note;
            _data.SelectedProjectId = imported.SelectedProjectId ?? imported.Projects.FirstOrDefault()?.Id;
            _noteContent = _data.Note.Content;
            Raise(nameof(NoteContent));

            _taskVm.Clear();
            BuildProjectVms();
            RebuildGroups();
            SelectedProject = _data.SelectedProjectId != null
                ? Projects.FirstOrDefault(p => p.Id == _data.SelectedProjectId)
                : Projects.FirstOrDefault();
            PersistImmediately();
            // success notice shows inline in the settings data card (green, no popup)
            DataNotice = $"导入成功：{imported.Projects.Count} 个项目，{imported.Tasks.Count} 条任务。";
        }
        catch (Exception ex)
        {
            DataError = "导入失败：" + ex.Message;
        }
    }

    private void BackupCurrent()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data", "backups");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "pre-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(_data, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        }
        catch { /* best-effort */ }
    }

    // ---- persistence -----------------------------------------------------

    private void PersistImmediately()
    {
        _store.Save(_data);
    }

    // ---- project manager hooks (called by ProjectManagerWindow) -----------

    public IEnumerable<ProjectViewModel> ProjectList => Projects;

    // empty/invalid add-project feedback shown inline (not a popup).
    private string _addProjectError = "";
    public string AddProjectError { get => _addProjectError; set => SetProperty(ref _addProjectError, value); }

    public bool AddProject(string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) { AddProjectError = "项目名称不能为空。"; return false; }
        if (IsDuplicateName(name, null)) { AddProjectError = "项目名称已存在（忽略前后空格）。"; return false; }
        AddProjectError = "";
        var now = DateTime.UtcNow;
        var p = new Project
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ColorIndex = _data.Projects.Count % ProjectPalette.Colors.Length,
            SortOrder = (_data.Projects.Count > 0 ? _data.Projects.Max(x => x.SortOrder) : -1) + 1,
            CreatedAt = now, UpdatedAt = now,
        };
        _data.Projects.Add(p);
        PersistImmediately();
        RefreshProjects();
        return true;
    }

    public bool RenameProject(string id, string name)
    {
        // validation errors show inline in the rename row (EditError), no popup
        var vm = _projVm.TryGetValue(id, out var v) ? v : null;
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) { if (vm != null) vm.EditError = "项目名称不能为空。"; return false; }
        if (IsDuplicateName(name, id)) { if (vm != null) vm.EditError = "项目名称已存在（忽略前后空格）。"; return false; }
        var p = _data.Projects.FirstOrDefault(x => x.Id == id);
        if (p == null) return false;
        p.Name = name;
        p.UpdatedAt = DateTime.UtcNow;
        PersistImmediately();
        RefreshProjects();
        return true;
    }

    // ---- inline rename / delete (no popup; confirm row appears under the button) ----

    public void BeginRenameProject(string id)
    {
        ClearProjectActions();
        if (_projVm.TryGetValue(id, out var vm)) { vm.EditError = ""; vm.BeginRename(); }
    }

    public bool CommitRenameProject(string id)
    {
        if (!_projVm.TryGetValue(id, out var vm)) return false;
        var ok = RenameProject(id, vm.EditBuffer);
        if (ok) vm.IsRenameMode = false;
        return ok;
    }

    /// <summary>Open the inline delete confirmation. If the project has tasks,
    /// the confirm row also shows a target-project picker (no popup).</summary>
    public void BeginDeleteProject(string id)
    {
        ClearProjectActions();
        if (!_projVm.TryGetValue(id, out var vm)) return;
        var hasTasks = _data.Tasks.Any(t => t.ProjectId == id);
        if (hasTasks && _data.Projects.Count <= 1)
        {
            // blocked path: show inline red message in the delete row instead of a popup
            vm.MoveTargets = new();
            vm.DeleteBlockedMessage = "该项目有待办，且没有其他项目可转移。请先处理这些待办或新建一个项目。";
            vm.IsDeleteConfirm = true;
            return;
        }
        // Only offer a move-target picker when the project actually has tasks to move.
        if (hasTasks)
        {
            vm.MoveTargets = Projects.Where(p => p.Id != id).ToList();
            vm.MoveTarget = vm.MoveTargets.FirstOrDefault();
        }
        else
        {
            vm.MoveTargets = new();
        }
        vm.IsDeleteConfirm = true;
    }

    public void CancelProjectAction() => ClearProjectActions();

    /// <summary>Execute the delete after the inline confirm (and target pick, if any).</summary>
    public void CommitDeleteProject(string id)
    {
        var p = _data.Projects.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        var tasks = _data.Tasks.Where(t => t.ProjectId == id).ToList();
        if (tasks.Count > 0)
        {
            var target = _projVm.TryGetValue(id, out var vm) ? vm.MoveTarget : null;
            var targetModel = target != null ? _data.Projects.FirstOrDefault(x => x.Id == target.Id) : null;
            if (targetModel == null)
            {
                // inline red hint instead of a popup
                if (_projVm.TryGetValue(id, out var v2)) v2.DeleteBlockedMessage = "请选择待办转移到的目标项目。";
                return;
            }
            foreach (var t in tasks) { t.ProjectId = targetModel.Id; t.UpdatedAt = DateTime.UtcNow; }
        }
        _data.Projects.Remove(p);
        PersistImmediately();
        RefreshProjects();
        RebuildGroups();
    }

    private void ClearProjectActions()
    {
        foreach (var vm in _projVm.Values)
        {
            vm.IsRenameMode = false;
            vm.IsDeleteConfirm = false;
            vm.EditError = "";
            vm.DeleteBlockedMessage = "";
        }
    }

    private bool IsDuplicateName(string name, string? excludeId) =>
        _data.Projects.Any(p => p.Id != excludeId && p.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
}
