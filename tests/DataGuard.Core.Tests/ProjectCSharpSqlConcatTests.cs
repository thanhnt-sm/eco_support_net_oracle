using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Sources;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class ProjectCSharpSqlConcatTests
{
    [Theory]
    [InlineData("string.Concat(\"SELECT Id, \", \"Name FROM Orders\")")]
    [InlineData("System.String.Concat(\"SELECT \", \"Id, \", \"Name FROM Orders\")")]
    [InlineData("S.Concat(\"SELECT \", \"Id, \", \"Name\", \" FROM Orders\")")]
    [InlineData("string.Concat(str1: \"Name FROM Orders\", str0: \"SELECT Id, \")")]
    [InlineData("string.Concat(\"SELECT \", string.Concat(\"Id, \", \"Name FROM Orders\"))")]
    [InlineData("string.Concat(null, \"SELECT Id, Name FROM Orders\")")]
    [InlineData("string.Concat(\"\", \"SELECT Id, Name FROM Orders\", (string)null)")]
    [InlineData("string.Concat(Columns, \" FROM Orders\")")]
    [InlineData("string.Concat(\"SELECT \" + \"Id, \", \"Name FROM Orders\")")]
    [InlineData("string.Concat(@\"SELECT Id, \", \"\"\"Name FROM Orders\"\"\")")]
    public void Resolve_ConstantStringConcat_ReturnsExactText(string expression)
    {
        Resolve(expression).Should().Be("SELECT Id, Name FROM Orders");
    }

    [Theory]
    [InlineData("string.Concat(\"SELECT Id FROM \", table)")]
    [InlineData("string.Concat(\"SELECT Id FROM \", local)")]
    [InlineData("string.Concat(\"SELECT Id FROM \", GetTable())")]
    [InlineData("Fake.Concat(\"SELECT Id, \", \"Name FROM Orders\")")]
    [InlineData("string.Concat((object)\"SELECT Id, \", (object)\"Name FROM Orders\")")]
    [InlineData("string.Concat(new[] { \"SELECT Id, \", \"Name FROM Orders\" })")]
    [InlineData("string.Concat(\"SELECT \", \"Id\", \", \", \"Name\", \" FROM Orders\")")]
    [InlineData("string.Format(\"SELECT {0} FROM Orders\", \"Id, Name\")")]
    [InlineData("new System.Text.StringBuilder().Append(\"SELECT Id FROM Orders\").ToString()")]
    public void Resolve_UnsupportedOrDynamicComposition_RemainsUnknown(string expression)
    {
        Resolve(expression).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extract_ConcatQuery_PreservesConsumerAndSourceMap(bool escaped)
    {
        var directory = Directory.CreateTempSubdirectory("dg-concat");
        try
        {
            var second = escaped ? "\"Na\\u006de FROM Orders\"" : "\"Name FROM Orders\"";
            var code = """
                public class Order { public int Id { get; set; } public string Name { get; set; } }
                public class Connection { public void Query<T>(string sql) {} }
                public class Repo
                {
                    public void Load(Connection conn)
                    {
                        conn.Query<Order>(string.Concat(str1: SECOND, str0: "SELECT Id, "));
                    }
                }
                """.Replace("SECOND", second, StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Repo.cs"), code);
            var source = new ProjectCSharpSqlSource(directory.FullName);

            var contracts = (await source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().ToList();

            var sql = contracts.Should().ContainSingle().Which;
            sql.SqlText.Should().Be("SELECT Id, Name FROM Orders");
            sql.TargetTypeName.Should().Be("Order");
            sql.ExpectedProperties.Select(p => p.Name).Should().BeEquivalentTo("Id", "Name");
            ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql.SqlText).Should().BeEquivalentTo("Id", "Name");
            source.Diagnostics.Should().BeEmpty();
            var segment = sql.SourceMap.Should().ContainSingle(s => s.Offset == 11).Which;
            segment.IsExact.Should().Be(!escaped);
            var location = segment.Resolve(11)!;
            var mapped = code.Substring(location.SourceSpan.Start, location.SourceSpan.Length);
            mapped.Should().Be(escaped ? second : "N");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Resolve_ConcatBeyondBound_RemainsUnknown()
    {
        var expression = "string.Concat(\"" + new string('x', ProjectCSharpSqlSource.MaxSqlLiteralLength) + "\", \"x\")";
        Resolve(expression).Should().BeNull();
    }

    [Fact]
    public void Resolve_ConcatDepthLimit_RemainsUnknown()
    {
        var expression = "\"SELECT Id FROM Orders\"";
        for (var index = 0; index < 12; index++)
        {
            expression = "string.Concat(\"\", " + expression + ")";
        }

        Resolve(expression).Should().BeNull();
    }

    [Fact]
    public void Resolve_ConcatNullAndEmpty_ReturnsKnownEmptyText()
    {
        Resolve("string.Concat((string)null, \"\", (string)null)").Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_DynamicConcat_DoesNotInventSqlOrMissingColumns()
    {
        var directory = Directory.CreateTempSubdirectory("dg-concat-unknown");
        try
        {
            const string code = """
                public class Order { public int Id { get; set; } public string Name { get; set; } }
                public class Connection { public void Query<T>(string sql) {} }
                public class Repo
                {
                    public void Load(Connection conn, string columns)
                    {
                        conn.Query<Order>(string.Concat("SELECT ", columns, " FROM Orders"));
                    }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Repo.cs"), code);
            var source = new ProjectCSharpSqlSource(directory.FullName);

            (await source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().Should().BeEmpty();
            source.Diagnostics.Should().ContainSingle().Which.Kind.Should().Be(
                AcquisitionDiagnosticKind.UnresolvedSqlValue,
                "a recognized SQL slot with unsupported composition is incomplete, not a validated empty scan");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Extract_EfConcat_ReportsMissingMappedColumnOnlyWhenAbsent(bool includeName)
    {
        var directory = Directory.CreateTempSubdirectory("dg-concat-ef");
        try
        {
            var code = """
                public class Order { public int Id { get; set; } public string Name { get; set; } }
                public class DbSet<T> { public void FromSqlRaw(string sql) {} }
                public class Repo
                {
                    public void Load(DbSet<Order> orders)
                    {
                        orders.FromSqlRaw(string.Concat("SELECT Id", "COLUMNS FROM Orders"));
                    }
                }
                """.Replace("COLUMNS", includeName ? ", Name" : "", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Repo.cs"), code);
            var source = new ProjectCSharpSqlSource(directory.FullName);
            var contracts = (await source.ExtractContractsAsync()).OfType<RawSqlDescriptor>().ToList();
            var sql = contracts.Should().ContainSingle().Which;

            var violations = await new ColumnShapeMatchRule().ValidateAsync(sql, contracts);

            if (includeName)
            {
                violations.Should().BeEmpty();
            }
            else
            {
                var violation = violations.Should().ContainSingle().Which;
                violation.RuleId.Should().Be("DG004");
                violation.Properties!["columns"].Should().Be("Name");
                violation.Location.Should().Be(sql.Location);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string? Resolve(string expression)
    {
        var code = """
            using S = System.String;
            class Fake { public static string Concat(string a, string b) => throw new System.Exception(); }
            class Repo
            {
                const string Columns = "SELECT Id, Name";
                string GetTable() => throw new System.Exception();
                void Use(string sql) {}
                void Run(string table)
                {
                    var local = "Orders";
                    Use(EXPRESSION);
                }
            }
            """.Replace("EXPRESSION", expression, StringComparison.Ordinal);
        var tree = CSharpSyntaxTree.ParseText(code);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "ConcatFixture", new[] { tree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var call = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.Expression.ToString() == "Use");
        return ProjectCSharpSqlSource.TryResolveString(call.ArgumentList.Arguments[0].Expression, compilation.GetSemanticModel(tree));
    }
}
