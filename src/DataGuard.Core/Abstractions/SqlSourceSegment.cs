using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DataGuard.Core.Abstractions;

/// <summary>A span of extracted SQL and its originating source. Synthesized values map to their whole expression.</summary>
/// <param name="Offset">Zero-based SQL offset.</param>
/// <param name="Length">Number of SQL characters.</param>
/// <param name="Source">Originating C# location.</param>
/// <param name="IsExact">Whether source and SQL characters correspond one-to-one.</param>
public sealed record SqlSourceSegment(int Offset, int Length, Location Source, bool IsExact)
{
    /// <summary>Resolves an SQL offset to its source character or containing expression.</summary>
    public Location? Resolve(int offset)
    {
        if (offset < Offset || offset >= Offset + Length)
        {
            return null;
        }

        return IsExact && Source.SourceTree is { } tree
            ? Location.Create(tree, new TextSpan(Source.SourceSpan.Start + offset - Offset, 1))
            : Source;
    }
}
