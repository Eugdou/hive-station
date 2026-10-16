namespace WorkingCenter.Models;

/// <summary>A project (FT, RF, …). Tasks belong to exactly one project.
/// POCO with no serialization attributes so the stage-5 SQLite store maps it cleanly.</summary>
public sealed class Project
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int ColorIndex { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
