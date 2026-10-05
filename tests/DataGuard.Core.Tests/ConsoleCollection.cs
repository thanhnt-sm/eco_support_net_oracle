using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Test classes that redirect the process-wide <see cref="System.Console"/> streams (<c>Console.SetOut</c> /
/// <c>Console.SetError</c>) join this collection. Parallelization is disabled so no other test writes into, or reads
/// from, a redirected stream while such a test runs.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    /// <summary>The collection name used in <c>[Collection(ConsoleCollection.Name)]</c>.</summary>
    public const string Name = "Console";
}
