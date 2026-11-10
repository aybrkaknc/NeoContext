using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Core.Abstractions;

/// <summary>Implemented by providers whose entries have an order that can be changed.</summary>
public interface IEntryMover
{
    /// <summary>Moves the entry one place up (negative) or down (positive). Moving past either end changes nothing and succeeds.</summary>
    Result Move(ContextMenuEntry entry, int direction);
}
