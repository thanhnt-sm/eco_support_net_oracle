using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Credential/environment tests share process-wide state. Exclude other collections as well as serializing members.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SequentialCollection
{
    public const string Name = "Sequential";
}
