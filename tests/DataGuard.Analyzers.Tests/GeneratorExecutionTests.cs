using DataGuard.Analyzers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataGuard.Analyzers.Tests;

/// <summary>
/// Executes the incremental generator against real C# source and asserts the
/// DG001 "SQL call not validated" diagnostic is produced for raw SQL calls.
/// </summary>
public class GeneratorExecutionTests
{
    [Fact]
    public void Generator_EmitsUnvalidatedSqlCall_ForExecuteSqlRawCall()
    {
        const string source = """
            namespace TestApp;
            public class Ctx
            {
                public object ExecuteSqlRaw(string sql) => null;
            }
            public class Usage
            {
                public void Run(Ctx ctx)
                {
                    var x = ctx.ExecuteSqlRaw("SELECT * FROM ORDERS WHERE id = 1");
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestApp",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator().AsSourceGenerator());
        // GeneratorDriver is immutable: the updated driver is returned, and only
        // the returned instance carries the run results.
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var runResult = driver.GetRunResult();
        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == DiagnosticIds.UnvalidatedSqlCall)
            .ToList();

        diagnostics.Should().NotBeEmpty("the generator must flag unvalidated raw SQL calls with DG001");
    }

    [Fact]
    public void Generator_DoesNotEmit_ForNonSqlInvocation()
    {
        const string source = """
            namespace TestApp;
            public class Ctx
            {
                public object GetData(int id) => null;
            }
            public class Usage
            {
                public void Run(Ctx ctx)
                {
                    var x = ctx.GetData(1);
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestApp",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator().AsSourceGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var diagnostics = driver.GetRunResult().Diagnostics
            .Where(d => d.Id == DiagnosticIds.UnvalidatedSqlCall)
            .ToList();

        diagnostics.Should().BeEmpty();
    }
}
