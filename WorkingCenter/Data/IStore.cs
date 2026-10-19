using WorkingCenter.Models;

namespace WorkingCenter.Data;

/// <summary>Snapshot persistence. The view model holds an in-memory AppData,
/// mutates it, and calls Save at the moments it decides (immediate for
/// projects/tasks, debounced for notes). Stage 5 swaps the JSON impl for a
/// SQLite impl behind this same interface — one construction-site change.</summary>
internal interface IStore
{
    /// <summary>Returns the persisted snapshot, or a seeded default on first run.</summary>
    AppData Load();

    /// <summary>Atomic full-snapshot persist. Implementations must not corrupt the
    /// previous good file on a partial write.</summary>
    void Save(AppData snapshot);
}
