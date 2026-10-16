namespace WorkingCenter.Models;

/// <summary>The full persisted snapshot. The store loads/saves this whole object,
/// so stage 5 can swap JSON for SQLite behind the same IStore interface with no
/// view-model changes. SchemaVersion lets the store migrate older files.</summary>
public sealed class AppData
{
    /// <summary>Bumped whenever the schema changes; the store runs migration
    /// functions to bring older snapshots up to the current version.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public const int CurrentSchemaVersion = 2;

    public List<Project> Projects { get; set; } = new();
    public List<TodoTask> Tasks { get; set; } = new();
    public QuickNote Note { get; set; } = new();
    public string? SelectedProjectId { get; set; }

    public static AppData CreateDefault()
    {
        var now = DateTime.UtcNow;
        var projects = new List<Project>();
        int i = 0;
        foreach (var (key, name, ci) in ProjectPalette.Defaults)
        {
            projects.Add(new Project
            {
                Id = key, Name = name, ColorIndex = ci, SortOrder = i++,
                CreatedAt = now, UpdatedAt = now,
            });
        }
        return new AppData
        {
            SchemaVersion = CurrentSchemaVersion,
            Projects = projects,
            Note = new QuickNote { Id = "main", Content = "", CreatedAt = now, UpdatedAt = now },
            SelectedProjectId = projects[0].Id,
        };
    }
}
