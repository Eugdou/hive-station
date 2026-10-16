namespace WorkingCenter.Models;

/// <summary>The single global quick-notes content. Modeled as an entity (Id="main")
/// to match the stage-5 schema even though the UI is just one textarea.</summary>
public sealed class QuickNote
{
    public string Id { get; set; } = "main";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
