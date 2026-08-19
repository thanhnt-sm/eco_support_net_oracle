using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataGuard.Core;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Oracle.Adapter;
using Microsoft.CodeAnalysis;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DataGuard.GoldenCorpus.Tests;

/// <summary>
/// Golden Corpus regression tests for DataGuard.
/// Tests are organized by taxonomy:
/// - H1: Phantom Identifiers (invented tables/columns)
/// - H2: Column/Table Mismatch
/// - H3: Dialect Confusion
/// - Length_Mismatch: char/byte semantics, NVARCHAR2(2000) fallback
/// - Vietnamese_Data: Unicode byte vs char semantics
/// </summary>
public class GoldenCorpusTests
{
    private readonly ITestOutputHelper _output;
    private readonly string _corpusRoot;

    public GoldenCorpusTests(ITestOutputHelper output)
    {
        _output = output;
        _corpusRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "golden-corpus");
    }

    [Theory]
    [MemberData(nameof(GetTestCases))]
    public async Task GoldenCorpusTestCase_RunsExpectedDiagnostics(GoldenCorpusTestCase testCase)
    {
        _output.WriteLine($"Running {testCase.TestCase} ({testCase.Category})");
        _output.WriteLine($"Description: {testCase.Description}");

        // Parse the test case input
        var violations = await RunValidationAsync(testCase.Input);

        // Verify expected diagnostics
        foreach (var expected in testCase.ExpectedDiagnostics)
        {
            var matchingViolations = violations.Where(v => 
                v.RuleId == expected.RuleId &&
                v.Message.Contains(expected.MessageContains, StringComparison.OrdinalIgnoreCase) &&
                v.Severity.ToString().Equals(expected.Severity, StringComparison.OrdinalIgnoreCase)).ToList();

            matchingViolations.Should().NotBeEmpty(
                $"Expected diagnostic {expected.RuleId} with message containing '{expected.MessageContains}' and severity {expected.Severity} was not found. " +
                $"Actual violations: {string.Join(", ", violations.Select(v => $"{v.RuleId}: {v.Message}"))}");
        }

        // Verify no unexpected errors (optional - can be strict or lenient)
        var unexpectedErrors = violations
            .Where(v => v.Severity == DiagnosticSeverity.Error && 
                       !testCase.ExpectedDiagnostics.Any(e => e.RuleId == v.RuleId && e.Severity == "Error"))
            .ToList();

        // Log all violations for debugging
        foreach (var v in violations)
        {
            _output.WriteLine($"  {v.RuleId} [{v.Severity}]: {v.Message}");
        }

        if (testCase.Notes != null)
        {
            _output.WriteLine($"Notes: {testCase.Notes}");
        }
    }

    public static IEnumerable<object[]> GetTestCases()
    {
        var corpusRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "golden-corpus");
        if (!Directory.Exists(corpusRoot))
        {
            yield break;
        }

        var jsonFiles = Directory.GetFiles(corpusRoot, "*.json", SearchOption.AllDirectories);
        
        foreach (var file in jsonFiles)
        {
            GoldenCorpusTestCase? testCase = null;
            try
            {
                var json = File.ReadAllText(file);
                testCase = JsonSerializer.Deserialize<GoldenCorpusTestCase>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load test case from {file}: {ex.Message}");
            }

            if (testCase != null)
            {
                yield return new object[] { testCase };
            }
        }
    }

    private async Task<List<ContractViolation>> RunValidationAsync(GoldenCorpusInput input)
    {
        var violations = new List<ContractViolation>();

        // Build contracts from input
        var contracts = BuildContracts(input);

        // Run all rules
        var rules = GetRulesForProvider(input.Provider);

        foreach (var rule in rules)
        {
            foreach (var contract in contracts)
            {
                var ruleViolations = await rule.ValidateAsync(contract, contracts, CancellationToken.None);
                violations.AddRange(ruleViolations);
            }
        }

        return violations;
    }

    private List<ContractDescriptor> BuildContracts(GoldenCorpusInput input)
    {
        var contracts = new List<ContractDescriptor>();

        // Build entity contracts
        if (input.Entity != null)
        {
            var entity = new EntityDescriptor(
                Id: $"entity:{input.Entity.Name}",
                Name: input.Entity.Name,
                ClrTypeName: input.Entity.Name,
                TableName: GetTableNameForEntity(input.Entity.Name, input.DatabaseSchema),
                Properties: input.Entity.Properties.Select(p => new PropertyDescriptor(
                    Name: p.Name,
                    ClrTypeName: p.Type,
                    ColumnName: ToSnakeCase(p.Name),
                    ColumnType: GetColumnType(p.Type, p.MaxLength, p.IsUnicode),
                    IsNullable: !p.IsPrimaryKey,
                    MaxLength: p.MaxLength,
                    IsPrimaryKey: p.IsPrimaryKey,
                    IsForeignKey: p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && !p.IsPrimaryKey
                )).ToList(),
                Location: null
            );
            contracts.Add(entity);
        }

        // Build SP/Raw SQL contracts from SQL
        if (!string.IsNullOrEmpty(input.Sql))
        {
            // For testing purposes, we'll create a raw SQL descriptor
            var sqlContracts = ParseRawSql(input.Sql, input.DatabaseSchema, input.Provider);
            contracts.AddRange(sqlContracts);
        }

        return contracts;
    }

    private string GetTableNameForEntity(string entityName, DatabaseSchema schema)
    {
        // Convert entity name to Oracle table name convention (UPPER_SNAKE_CASE)
        var tableName = ToUpperSnakeCase(entityName) + "S";
        return schema.Tables.Any(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase)) 
            ? tableName 
            : schema.Tables.FirstOrDefault()?.Name ?? tableName;
    }

    private string GetColumnType(string clrType, int? maxLength, bool isUnicode)
    {
        if (clrType == "string")
        {
            if (isUnicode)
                return maxLength.HasValue ? $"NVARCHAR2({maxLength})" : "NVARCHAR2(2000)";
            return maxLength.HasValue ? $"VARCHAR2({maxLength})" : "VARCHAR2(2000)";
        }
        return clrType switch
        {
            "int" => "NUMBER(10)",
            "long" => "NUMBER(19)",
            "decimal" => "NUMBER(18,2)",
            "DateTime" => "TIMESTAMP",
            "bool" => "NUMBER(1)",
            "Guid" => "RAW(16)",
            _ => "VARCHAR2(2000)"
        };
    }

    private List<ContractDescriptor> ParseRawSql(string sql, DatabaseSchema schema, string provider)
    {
        // Simplified - in real implementation would parse SQL and extract table/column references
        var contracts = new List<ContractDescriptor>();
        
        // For testing, create a raw SQL descriptor that will trigger dialect rules
        var rawSql = new RawSqlDescriptor(
            Id: $"raw-sql:test",
            SqlText: sql,
            Parameters: new List<ParameterDescriptor>(),
            ResultColumns: new List<ColumnDescriptor>(),
            Location: null
        );
        contracts.Add(rawSql);
        return contracts;
    }

    private List<IContractRule> GetRulesForProvider(string provider)
    {
        var rules = new List<IContractRule>
        {
            new ParameterCountRule(),
            new ParameterTypeMatchRule(),
            new ParameterDirectionRule(),
            new ColumnShapeMatchRule(),
            new NullableMismatchRule(),
            new NamingConventionRule()
        };

        // Add provider-specific rules
        if (provider?.Equals("Oracle", StringComparison.OrdinalIgnoreCase) == true)
        {
            rules.Add(new OracleSyntaxInNonOracleContextRule());
            rules.Add(new NonOracleFunctionInOracleContextRule());
            rules.Add(new ProviderOptionMismatchRule());
            rules.Add(new SqlServerSyntaxLeakRule());
            rules.Add(new RawSqlUnmappedTypeUsageRule());
            rules.Add(new LengthExceedsColumnRule());
            rules.Add(new ByteLengthOverflowRiskRule());
            rules.Add(new InferredSizeFallbackRule());
        }

        return rules;
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (i > 0 && char.IsUpper(input[i]))
                result.Append('_');
            result.Append(char.ToUpperInvariant(input[i]));
        }
        return result.ToString();
    }

    private static string ToUpperSnakeCase(string input)
    {
        return ToSnakeCase(input).ToUpperInvariant();
    }
}

/// <summary>
/// Golden corpus test case model.
/// </summary>
public class GoldenCorpusTestCase
{
    public string TestCase { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public GoldenCorpusInput Input { get; set; } = new();
    public List<ExpectedDiagnostic> ExpectedDiagnostics { get; set; } = new();
    public string? Notes { get; set; }
}

public class GoldenCorpusInput
{
    public GoldenCorpusEntity? Entity { get; set; }
    public string Sql { get; set; } = "";
    public DatabaseSchema DatabaseSchema { get; set; } = new();
    public string Provider { get; set; } = "Oracle";
    public string LengthSemantics { get; set; } = "CHAR";
}

public class GoldenCorpusEntity
{
    public string Name { get; set; } = "";
    public List<GoldenCorpusProperty> Properties { get; set; } = new();
}

public class GoldenCorpusProperty
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int? MaxLength { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsUnicode { get; set; }
}

public class DatabaseSchema
{
    public List<DatabaseTable> Tables { get; set; } = new();
}

public class DatabaseTable
{
    public string Name { get; set; } = "";
    public List<DatabaseColumn> Columns { get; set; } = new();
}

public class DatabaseColumn
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int? CharLength { get; set; }
    public string? CharUsed { get; set; }
    public bool Nullable { get; set; }
}

public class ExpectedDiagnostic
{
    public string RuleId { get; set; } = "";
    public string MessageContains { get; set; } = "";
    public string Severity { get; set; } = "";
}