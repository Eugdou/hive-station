namespace WorkingCenter.Models;

/// <summary>A todo task. Named TodoTask (not Task) to avoid clashing with
/// System.Threading.Tasks.Task, which is globally imported via ImplicitUsings.
/// Detail / SortOrder / CompletedAt are present now so stage 4 (details + stable
/// sort) needs no model change — stage 3 leaves Detail empty and uses SortOrder
/// as a stable insertion index.</summary>
public sealed class TodoTask
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";   // "" = 未分类
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsCompleted { get; set; }
    public bool IsUrgent { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
