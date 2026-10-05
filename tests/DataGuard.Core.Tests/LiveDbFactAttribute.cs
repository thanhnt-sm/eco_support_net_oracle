using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace DataGuard.Core.Tests;

/// <summary>Which live database family a <see cref="LiveDbFactAttribute"/> test needs.</summary>
public enum LiveDbTarget
{
    /// <summary>Oracle, MySQL and PostgreSQL Testcontainers; enabled by <c>DATAGUARD_REQUIRE_LIVE_RELATIONAL=1</c>.</summary>
    Relational,

    /// <summary>SQL Server Testcontainers; enabled by <c>DATAGUARD_REQUIRE_LIVE_SQLSERVER=1</c>.</summary>
    SqlServer,
}

/// <summary>
/// Opt-in gate for tests that need a real database container. Without the environment variable the test is reported
/// as <b>Skipped</b> (never silently passed); with it, the test runs and its fixture must fail loudly when the
/// container cannot start. Every gated test carries the trait <c>Category=LiveDb</c>, so CI can run them in a
/// dedicated job (<c>--filter Category=LiveDb</c>) and exclude them elsewhere (<c>--filter "Category!=LiveDb"</c>).
/// </summary>
public static class LiveDbGate
{
    /// <summary>Environment variable that enables the Oracle/MySQL/PostgreSQL live tests.</summary>
    public const string RelationalVariable = "DATAGUARD_REQUIRE_LIVE_RELATIONAL";

    /// <summary>Environment variable that enables the SQL Server live tests.</summary>
    public const string SqlServerVariable = "DATAGUARD_REQUIRE_LIVE_SQLSERVER";

    /// <summary>Legacy alias for <see cref="SqlServerVariable"/>, still honored for existing scripts.</summary>
    public const string LegacySqlServerVariable = "DATAGUARD_RUN_SQLSERVER_INTEGRATION";

    /// <summary>The trait name applied to every live test.</summary>
    public const string TraitName = "Category";

    /// <summary>The trait value applied to every live test.</summary>
    public const string TraitValue = "LiveDb";

    /// <summary>Returns whether live tests for <paramref name="target"/> are enabled in this process.</summary>
    public static bool IsEnabled(LiveDbTarget target) => target switch
    {
        LiveDbTarget.Relational => IsSet(RelationalVariable),
        LiveDbTarget.SqlServer => IsSet(SqlServerVariable) || IsSet(LegacySqlServerVariable),
        _ => false,
    };

    /// <summary>Returns the skip reason for <paramref name="target"/>, or null when the live tests are enabled.</summary>
    public static string? SkipReason(LiveDbTarget target) => IsEnabled(target)
        ? null
        : $"Live database test: set {(target == LiveDbTarget.SqlServer ? SqlServerVariable : RelationalVariable)}=1 (Docker required) to run it.";

    /// <summary>
    /// Wraps a container start failure. Only called when the gate is enabled, so an unavailable container is a test
    /// failure, not a pass.
    /// </summary>
    public static InvalidOperationException ContainerStartFailed(LiveDbTarget target, string image, Exception inner) =>
        new($"{(target == LiveDbTarget.SqlServer ? SqlServerVariable : RelationalVariable)}=1 requires a live database, but the Testcontainers fixture for '{image}' could not start: {inner.Message}", inner);

    private static bool IsSet(string variable) =>
        string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal);
}

/// <summary>A <see cref="FactAttribute"/> that is skipped unless the live database gate for its target is enabled.</summary>
[TraitDiscoverer(LiveDbTraitDiscoverer.TypeName, LiveDbTraitDiscoverer.AssemblyName)]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveDbFactAttribute : FactAttribute, ITraitAttribute
{
    /// <summary>Initializes a new instance of the <see cref="LiveDbFactAttribute"/> class.</summary>
    /// <param name="target">The database family the test needs.</param>
    public LiveDbFactAttribute(LiveDbTarget target)
    {
        Target = target;
        Skip = LiveDbGate.SkipReason(target)!;
    }

    /// <summary>Gets the database family the test needs.</summary>
    public LiveDbTarget Target { get; }
}

/// <summary>A <see cref="TheoryAttribute"/> that is skipped unless the live database gate for its target is enabled.</summary>
[TraitDiscoverer(LiveDbTraitDiscoverer.TypeName, LiveDbTraitDiscoverer.AssemblyName)]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveDbTheoryAttribute : TheoryAttribute, ITraitAttribute
{
    /// <summary>Initializes a new instance of the <see cref="LiveDbTheoryAttribute"/> class.</summary>
    /// <param name="target">The database family the test needs.</param>
    public LiveDbTheoryAttribute(LiveDbTarget target)
    {
        Target = target;
        Skip = LiveDbGate.SkipReason(target)!;
    }

    /// <summary>Gets the database family the test needs.</summary>
    public LiveDbTarget Target { get; }
}

/// <summary>Emits <c>Category=LiveDb</c> (and <c>LiveDbTarget=&lt;target&gt;</c>) for the live test attributes.</summary>
public sealed class LiveDbTraitDiscoverer : ITraitDiscoverer
{
    /// <summary>Fully qualified type name used by <see cref="TraitDiscovererAttribute"/>.</summary>
    public const string TypeName = "DataGuard.Core.Tests.LiveDbTraitDiscoverer";

    /// <summary>Assembly name used by <see cref="TraitDiscovererAttribute"/>.</summary>
    public const string AssemblyName = "DataGuard.Core.Tests";

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        yield return new KeyValuePair<string, string>(LiveDbGate.TraitName, LiveDbGate.TraitValue);
        var argument = traitAttribute.GetConstructorArguments()?.FirstOrDefault();
        var target = argument switch
        {
            LiveDbTarget value => value,
            int raw => (LiveDbTarget)raw,
            _ => LiveDbTarget.Relational,
        };
        yield return new KeyValuePair<string, string>("LiveDbTarget", target.ToString());
    }
}
