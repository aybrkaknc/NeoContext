using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Core.Abstractions;

/// <summary>
/// Implemented by providers whose entries can be edited (command, icon, attributes).
/// Like every provider it returns a <see cref="Result"/> and never shows UI.
/// </summary>
public interface IEntryEditor
{
    Result Edit(ContextMenuEntry entry, EntryEdit edit);
}
