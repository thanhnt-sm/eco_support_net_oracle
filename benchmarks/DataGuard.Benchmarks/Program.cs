using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using DataGuard.Analyzers;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataGuard.Benchmarks;

public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class ConcurrentValidationBenchmarks
{
    private IReadOnlyList<ContractDescriptor> _contracts = Array.Empty<ContractDescriptor>();
    private IReadOnlyList<IContractRule> _rules = Array.Empty<IContractRule>();
    private ConcurrentValidationEngine _engine = null!;

    [Params(100, 1000)]
    public int ContractCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _contracts = Enumerable.Range(0, ContractCount)
            .Select(i => (ContractDescriptor)new RawSqlDescriptor(
                $"raw:{i}",
                "EXEC dbo.GetCustomer @Id",
                new[] { new ParameterDescriptor("Id", "int", ParameterDirection.Input, null, null, null, false, 1) },
                Array.Empty<ColumnDescriptor>()))
            .ToList();
        _rules = new IContractRule[] { new ParameterCountRule(), new ParameterTypeMatchRule() };
        _engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 4);
    }

    [Benchmark]
    public Task<IReadOnlyList<ContractViolation>> Validate() => _engine.ValidateAsync(_contracts, _rules);
}

[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class IncrementalGeneratorBenchmarks
{
    private GeneratorDriver _driver = null!;
    private Compilation _compilation = null!;

    [GlobalSetup]
    public void Setup()
    {
        var source = """
            using Microsoft.EntityFrameworkCore;
            public class C
            {
                public void M(DbContext ctx) {
                    ctx.Set<object>().FromSqlRaw("SELECT 1");
                    ctx.Database.ExecuteSqlRaw("UPDATE T SET X = 1");
                }
            }
            """;
        _compilation = CSharpCompilation.Create(
            "bench",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        _driver = CSharpGeneratorDriver.Create(new UnvalidatedSqlCallGenerator());
    }

    [Benchmark]
    public GeneratorDriver RunGenerator() => _driver.RunGenerators(_compilation);
}
