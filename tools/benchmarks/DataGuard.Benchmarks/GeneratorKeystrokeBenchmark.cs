using System.Text;
using BenchmarkDotNet.Attributes;
using DataGuard.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Keystroke latency of the DG001 generator: a 2,000-line file with 50 SQL calls is edited (one character inside a
/// method body) and the already-warm generator driver is re-run, which is what the IDE does on every keystroke.
/// <see cref="Keystroke"/> measures that incremental run; <see cref="ColdRun"/> a fresh driver over the same file.
/// </summary>
[MemoryDiagnoser]
public class GeneratorKeystrokeBenchmark
{
    internal const string CorpusContract = "CSharp:2000-lines/50-sql-calls(ExecuteSqlRaw,Dapper Query<T>,FromSqlRaw,ADO CommandText):keystroke";

    private const int LineCount = 2_000;
    private const int SqlCallCount = 50;

    private const string Stubs = """
        public class Db { public int ExecuteSqlRaw(string sql, params object[] args) => 0; }
        public class Set<T> { public object FromSqlRaw(string sql) => null; }
        public class Connection { public System.Collections.Generic.IEnumerable<T> Query<T>(string sql, object param = null) => null; }
        public class Command { public string CommandText { get; set; } public int ExecuteNonQuery() => 0; }
        public class Row { public int Id { get; set; } }
        """;

    private Compilation[] compilations = Array.Empty<Compilation>();
    private GeneratorDriver? driver;
    private int next;

    /// <summary>Builds two compilations that differ by one keystroke and warms the driver on the first.</summary>
    [GlobalSetup]
    public void SetUp()
    {
        var source = BuildSource();
        var tree = CSharpSyntaxTree.ParseText(source, path: "/bench/Repository.cs");
        var stubs = CSharpSyntaxTree.ParseText(Stubs, path: "/bench/Stubs.cs");
        var baseCompilation = CSharpCompilation.Create(
            "DataGuard.Benchmark.Keystroke",
            [stubs, tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // A keystroke inside a method body: "int x0 = 0;" -> "int x0 = 01;" and back.
        var marker = source.IndexOf("int x0 = 0;", StringComparison.Ordinal) + "int x0 = 0".Length;
        var edited = tree.WithChangedText(tree.GetText().WithChanges(new TextChange(new TextSpan(marker, 0), "1")));
        compilations = new[] { baseCompilation, baseCompilation.ReplaceSyntaxTree(tree, edited) };

        driver = CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator().AsSourceGenerator()).RunGenerators(compilations[0]);
        var diagnostics = driver.GetRunResult().Diagnostics.Count(diagnostic => diagnostic.Id == DiagnosticIds.UnvalidatedSqlCall);
        if (diagnostics != SqlCallCount)
        {
            throw new InvalidOperationException($"Expected {SqlCallCount} DG001 diagnostics, got {diagnostics}.");
        }
    }

    /// <summary>One incremental generator run after a single-character edit of the 2,000-line file.</summary>
    /// <returns>The updated driver.</returns>
    [Benchmark(Baseline = true)]
    public GeneratorDriver Keystroke()
    {
        next ^= 1;
        driver = (driver ?? throw new InvalidOperationException("Benchmark setup did not run.")).RunGenerators(compilations[next]);
        return driver;
    }

    /// <summary>A fresh driver over the same file (no generator cache), for comparison.</summary>
    /// <returns>The driver.</returns>
    [Benchmark]
    public GeneratorDriver ColdRun() =>
        CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator().AsSourceGenerator()).RunGenerators(compilations[0]);

    private static string BuildSource()
    {
        var builder = new StringBuilder("public class Repository\n{\n");
        var lines = 2;
        var method = 0;
        while (lines < LineCount - 1)
        {
            // Every method has ~38 lines: a few with a SQL call, the rest plain C# (LINQ, loops, strings).
            builder.Append("    public int M").Append(method).Append("(Db db, Set<Row> rows, Connection cn, Command cmd, System.Collections.Generic.List<int> xs)\n    {\n");
            builder.Append("        int x").Append(method).Append(" = 0;\n");
            lines += 3;
            if (method < SqlCallCount)
            {
                builder.Append((method % 4) switch
                {
                    0 => "        db.ExecuteSqlRaw(\"DELETE FROM Orders WHERE Id = {0}\", x" + method + ");\n",
                    1 => "        var q" + method + " = cn.Query<Row>(\"SELECT Id FROM Orders WHERE Id = @id\", new { id = x" + method + " });\n",
                    2 => "        var f" + method + " = rows.FromSqlRaw(\"SELECT Id FROM Orders\");\n",
                    _ => "        cmd.CommandText = \"UPDATE Orders SET Total = 0\";\n        cmd.ExecuteNonQuery();\n",
                });
                lines += method % 4 == 3 ? 2 : 1;
            }

            for (var body = 0; body < 32 && lines < LineCount - 3; body++, lines++)
            {
                builder.Append((body % 4) switch
                {
                    0 => "        x" + method + " += xs.Where(v => v > " + body + ").Select(v => v * 2).Count();\n",
                    1 => "        if (x" + method + " > " + body + ") { x" + method + "--; }\n",
                    2 => "        var s" + body + " = \"label " + body + "\" + x" + method + ".ToString();\n",
                    _ => "        // select the next batch of rows " + body + "\n",
                });
            }

            builder.Append("        return x").Append(method).Append(";\n    }\n");
            lines += 2;
            method++;
        }

        return builder.Append("}\n").ToString();
    }
}
