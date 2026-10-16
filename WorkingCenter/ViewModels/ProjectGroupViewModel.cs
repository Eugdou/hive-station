using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace WorkingCenter.ViewModels;

/// <summary>One group in the grouped overview: a project header (colour dot +
/// name + "done/total 完成") and the ordered task VMs in it. Built fresh on
/// each rebuild, but it references the STABLE TodoTaskViewModel instances so
/// inline-edit survives a re-group.</summary>
internal sealed class ProjectGroupViewModel : ViewModelBase
{
    public string HeaderLabel { get; }
    public Brush DotBrush { get; }
    public System.Collections.ObjectModel.ObservableCollection<TodoTaskViewModel> Items { get; } = new();

    private int _doneCount;
    public int DoneCount
    {
        get => _doneCount;
        set { SetProperty(ref _doneCount, value); Raise(nameof(CountLabel)); }
    }

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        set { SetProperty(ref _totalCount, value); Raise(nameof(CountLabel)); }
    }

    public string CountLabel => $"{DoneCount}/{TotalCount} 完成";

    public ProjectGroupViewModel(string headerLabel, Brush dotBrush)
    {
        HeaderLabel = headerLabel;
        DotBrush = dotBrush;
    }
}
