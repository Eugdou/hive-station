using System.Text.Json;
using WorkingCenter.Core;
using WorkingCenter.Models;

namespace WorkingCenter.Data;

/// <summary>Maps an imported JSON backup into AppData. Accepts BOTH:
///  - native desktop backup: { note, projects, tasks, selectedProjectId, exportedAt }
///  - legacy web export:      { notes (string), todos: [{id,text,done,project,urgent}], exportedAt }
/// Using JsonDocument so unknown/extra fields are ignored and either shape parses.</summary>
internal static class DataMigrator
{
    public static bool TryImport(string json, out AppData data, out string error)
    {
        data = null!;
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (TryGet(root, "tasks", out _) || TryGet(root, "projects", out _))
            {
                data = FromNative(root);
            }
            else if (TryGet(root, "todos", out _))
            {
                data = FromLegacy(root);
            }
            else
            {
                error = "文件格式不正确：缺少 tasks/projects 或 todos 字段";
                return false;
            }

            if (data.Projects.Count == 0)
            {
                // Ensure there's always at least the default projects to assign to.
                data.Projects = AppData.CreateDefault().Projects;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "导入失败：" + ex.Message;
            return false;
        }
    }

    private static AppData FromNative(JsonElement root)
    {
        var data = new AppData();
        var now = DateTime.UtcNow;

        if (TryGet(root, "projects", out var projs))
            foreach (var p in projs.EnumerateArray())
                data.Projects.Add(new Project
                {
                    Id = Str(p, "id"),
                    Name = Str(p, "name"),
                    ColorIndex = Int(p, "colorIndex"),
                    SortOrder = Int(p, "sortOrder"),
                    CreatedAt = Date(p, "createdAt") ?? now,
                    UpdatedAt = Date(p, "updatedAt") ?? now,
                });

        if (TryGet(root, "tasks", out var tasks))
            foreach (var t in tasks.EnumerateArray())
                data.Tasks.Add(new TodoTask
                {
                    Id = Str(t, "id"),
                    ProjectId = Str(t, "projectId"),
                    Title = Str(t, "title"),
                    Detail = Str(t, "detail"),
                    IsCompleted = Bool(t, "isCompleted"),
                    IsUrgent = Bool(t, "isUrgent"),
                    SortOrder = Int(t, "sortOrder"),
                    CreatedAt = Date(t, "createdAt") ?? now,
                    UpdatedAt = Date(t, "updatedAt") ?? now,
                    CompletedAt = Date(t, "completedAt"),
                });

        if (TryGet(root, "reminders", out var rems))
            foreach (var r in rems.EnumerateArray())
            {
                var rec = ReadRecurrence(r);
                var reminder = new Reminder
                {
                    Id = Str(r, "id"),
                    Title = Str(r, "title"),
                    Recurrence = rec,
                    Time = Str(r, "time") is "" ? "09:00" : Str(r, "time"),
                    OnceDate = Str(r, "onceDate"),
                    Day = Int(r, "day") is 0 ? 1 : Int(r, "day"),
                    Enabled = !HasExplicitFalse(r, "enabled"),
                    NextTriggerAt = Date(r, "nextTriggerAt") ?? default,
                    CreatedAt = Date(r, "createdAt") ?? now,
                    UpdatedAt = Date(r, "updatedAt") ?? now,
                };
                ValidateReminder(reminder);
                data.Reminders.Add(reminder);
            }

        if (TryGet(root, "note", out var note))
            data.Note = new QuickNote
            {
                Id = "main",
                Content = Str(note, "content"),
                CreatedAt = Date(note, "createdAt") ?? now,
                UpdatedAt = Date(note, "updatedAt") ?? now,
            };
        else
            data.Note = new QuickNote { Id = "main", Content = "", CreatedAt = now, UpdatedAt = now };

        data.SelectedProjectId = TryGet(root, "selectedProjectId", out var sp) && sp.ValueKind == JsonValueKind.String
            ? sp.GetString() : null;
        return data;
    }

    private static AppData FromLegacy(JsonElement root)
    {
        var data = new AppData();
        var now = DateTime.UtcNow;

        // notes (string) -> single quick note
        string notes = TryGet(root, "notes", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? "" : "";
        data.Note = new QuickNote { Id = "main", Content = notes, CreatedAt = now, UpdatedAt = now };

        // todos: [{id, text, done, project, urgent}] -> create projects from keys
        var projByKey = new Dictionary<string, Project>();
        int sortIdx = 0;
        int projOrder = 0;

        if (TryGet(root, "todos", out var todos) && todos.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in todos.EnumerateArray())
            {
                string projectKey = Str(t, "project");
                if (!projByKey.TryGetValue(projectKey, out var proj))
                {
                    proj = new Project
                    {
                        Id = projectKey,
                        Name = string.IsNullOrEmpty(projectKey) ? "未分类" : projectKey,
                        ColorIndex = ProjectPalette.LegacyColorIndex(projectKey),
                        SortOrder = projOrder++,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    projByKey[projectKey] = proj;
                    data.Projects.Add(proj);
                }

                data.Tasks.Add(new TodoTask
                {
                    Id = Str(t, "id"),
                    ProjectId = projectKey,
                    Title = Str(t, "text"),
                    Detail = "",
                    IsCompleted = Bool(t, "done"),
                    IsUrgent = Bool(t, "urgent"),
                    SortOrder = sortIdx++,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CompletedAt = Bool(t, "done") ? now : null,
                });
            }
        }

        return data;
    }

    private static ReminderRecurrence ReadRecurrence(JsonElement reminder)
    {
        if (!TryGet(reminder, "recurrence", out var value)) return ReminderRecurrence.Daily;
        if (value.ValueKind == JsonValueKind.String
            && Enum.TryParse<ReminderRecurrence>(value.GetString(), true, out var named)
            && Enum.IsDefined(named)) return named;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int numeric)
            && Enum.IsDefined(typeof(ReminderRecurrence), numeric)) return (ReminderRecurrence)numeric;
        throw new FormatException("提醒周期无效，应为单次、每日、每周或每月。");
    }

    private static void ValidateReminder(Reminder reminder)
    {
        if (!ReminderRules.TryNormalizeTime(reminder.Time, out var time, out _))
            throw new FormatException($"提醒“{reminder.Title}”的时间无效。");
        reminder.Time = time;
        if (reminder.Recurrence == ReminderRecurrence.Once)
        {
            if (!ReminderRules.TryNormalizeDate(reminder.OnceDate, out var date, out _))
                throw new FormatException($"提醒“{reminder.Title}”的日期无效。");
            reminder.OnceDate = date;
        }
        if ((reminder.Recurrence == ReminderRecurrence.Weekly && (reminder.Day is < 1 or > 7))
            || (reminder.Recurrence == ReminderRecurrence.Monthly && (reminder.Day is < 1 or > 31)))
            throw new FormatException($"提醒“{reminder.Title}”的周几或几号无效。");
    }

    // --- tiny JSON helpers (CASE-INSENSITIVE so we accept both the native
    //     PascalCase export 'Id','Name','Title' and the legacy lowercase web
    //     export 'id','text','project') -------------------------------------
    private static bool TryGet(JsonElement e, string name, out JsonElement p)
    {
        if (e.ValueKind != JsonValueKind.Object) { p = default; return false; }
        if (e.TryGetProperty(name, out p)) return true;          // exact (legacy lowercase)
        // case-insensitive fallback (native PascalCase)
        foreach (var prop in e.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            { p = prop.Value; return true; }
        p = default;
        return false;
    }
    private static string Str(JsonElement e, string name) =>
        TryGet(e, name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static int Int(JsonElement e, string name) =>
        TryGet(e, name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
    private static bool Bool(JsonElement e, string name) =>
        TryGet(e, name, out var p) && p.ValueKind == JsonValueKind.True;
    private static bool HasExplicitFalse(JsonElement e, string name) =>
        TryGet(e, name, out var p) && p.ValueKind == JsonValueKind.False;
    private static DateTime? Date(JsonElement e, string name) =>
        TryGet(e, name, out var p) && p.ValueKind == JsonValueKind.String && DateTime.TryParse(p.GetString(), out var d) ? d : null;
}
