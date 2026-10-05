using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace DataGuard.GoldenCorpus.Tests;

/// <summary>
/// Golden Corpus regression tests for DataGuard. Every JSON file under <c>golden-corpus/</c> is one case; the rule set is
/// the CLI's own <see cref="ProviderRuleCatalog"/> inventory (Ready rules only), so a rule registered for a provider in
/// production is exercised here without a hand-maintained list. Categories:
/// - H1: Phantom Identifiers (invented tables/columns)
/// - H2: Column/Table Mismatch
/// - H3: Dialect Confusion
/// - Length_Mismatch: char/byte semantics, NVARCHAR2(2000) fallback
/// - Vietnamese_Data: Unicode byte vs char semantics
/// - Negative: valid SQL that must produce no Error/Warning (false-positive guard)
/// - SqlServer / MySql / PostgreSql: provider-specific cases.
/// </summary>
public class GoldenCorpusTests
{
    /// <summary>Minimum number of cases the corpus must hold; shrinking the corpus fails the build.</summary>
    public const int MinimumCaseCount = 24;

    /// <summary>Minimum number of negative (no-finding) cases.</summary>
    public const int MinimumNegativeCaseCount = 6;

    /// <summary>Category directories that must exist and hold at least one case.</summary>
    public static readonly IReadOnlyList<string> RequiredCategories = new[]
    {
        "H1_Phantom_Identifier",
        "H2_Column_Table_Mismatch",
        "H3_Dialect_Confusion",
        "Length_Mismatch",
        "Vietnamese_Data",
        "Negative",
        "SqlServer",
        "MySql",
        "PostgreSql",
    };

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private readonly ITestOutputHelper _output;

    public GoldenCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Gets the corpus root copied next to the test assembly.</summary>
    public static string CorpusRoot => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "golden-corpus");

    /// <summary>One theory row per case file (relative path), so a malformed file fails its own row.</summary>
    public static IEnumerable<object[]> GetTestCases()
    {
        if (!Directory.Exists(CorpusRoot))
        {
            throw new DirectoryNotFoundException($"Golden corpus directory not found: {CorpusRoot}");
        }

        return Directory.GetFiles(CorpusRoot, "*.json", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(CorpusRoot, file).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new object[] { path });
    }

    [Theory]
    [MemberData(nameof(GetTestCases))]
    public async Task GoldenCorpusTestCase_RunsExpectedDiagnostics(string caseFile)
    {
        var testCase = LoadCase(caseFile);
        _output.WriteLine($"Running {testCase.TestCase} ({testCase.Category}) provider={testCase.Input.Provider}");
        _output.WriteLine($"Description: {testCase.Description}");

        var categoryDirectory = caseFile.Split('/')[0];
        testCase.Category.Should().Be(categoryDirectory, "a case's category must match the directory it lives in");
        ValidateProvenance(testCase.Provenance, caseFile);

        var violations = await GoldenCorpusRunner.RunAsync(testCase.Input);
        foreach (var v in violations)
        {
            _output.WriteLine($"  {v.RuleId} [{v.Severity}]: {v.Message}");
        }

        var actualSummary = string.Join("; ", violations.Select(v => $"{v.RuleId} [{v.Severity}]: {v.Message}"));

        // 1. Every expected diagnostic (any severity) is present with its message fragment.
        foreach (var expected in testCase.ExpectedDiagnostics)
        {
            violations.Should().Contain(
                v => v.RuleId == expected.RuleId &&
                     v.Severity.ToString().Equals(expected.Severity, StringComparison.OrdinalIgnoreCase) &&
                     v.Message.Contains(expected.MessageContains, StringComparison.OrdinalIgnoreCase),
                $"expected {expected.RuleId} [{expected.Severity}] containing '{expected.MessageContains}'. Actual: {actualSummary}");
        }

        // 2. Error and Warning are compared as a full multiset by (RuleId, Severity): no missing and no extra finding.
        //    Info stays lenient (advisory rules such as DG006 may change wording/coverage without breaking the corpus).
        static bool IsGated(string severity) =>
            severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
            severity.Equals("Warning", StringComparison.OrdinalIgnoreCase);

        var expectedGated = testCase.ExpectedDiagnostics
            .Where(e => IsGated(e.Severity))
            .Select(e => $"{e.RuleId} [{Normalize(e.Severity)}]")
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        var actualGated = violations
            .Where(v => v.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(v => $"{v.RuleId} [{v.Severity}]")
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        actualGated.Should().Equal(
            expectedGated,
            $"the Error/Warning findings must match the expected set exactly. Actual: {actualSummary}");

        if (testCase.Notes != null)
        {
            _output.WriteLine($"Notes: {testCase.Notes}");
        }
    }

    [Fact]
    public void Corpus_MeetsMinimumSizeAndCategoryCoverage()
    {
        var files = GetTestCases().Select(row => (string)row[0]).ToList();
        files.Should().HaveCountGreaterThanOrEqualTo(MinimumCaseCount, "the golden corpus must not shrink below the agreed floor");

        var byCategory = files
            .GroupBy(file => file.Split('/')[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var category in RequiredCategories)
        {
            byCategory.Should().ContainKey(category, $"category directory '{category}' must exist");
            byCategory[category].Should().BeGreaterThanOrEqualTo(1, $"category '{category}' must hold at least one case");
        }

        foreach (var directory in Directory.GetDirectories(CorpusRoot))
        {
            var name = Path.GetFileName(directory);
            byCategory.Should().ContainKey(name, $"category directory '{name}' must not be empty");
        }

        byCategory["Negative"].Should().BeGreaterThanOrEqualTo(MinimumNegativeCaseCount);
    }

    [Fact]
    public void Corpus_NegativeCasesExpectNoFindings_AndCoverEveryProvider()
    {
        var negatives = GetTestCases()
            .Select(row => (string)row[0])
            .Where(file => file.StartsWith("Negative/", StringComparison.Ordinal))
            .Select(LoadCase)
            .ToList();

        negatives.Should().OnlyContain(c => c.ExpectedDiagnostics.Count == 0, "negative cases assert the absence of findings");
        negatives.Select(c => GoldenCorpusRunner.NormalizeProvider(c.Input.Provider)).Distinct()
            .Should().Contain(new[] { "oracle", "sqlserver" });
    }

    [Fact]
    public void Corpus_CoversAllFourProviders()
    {
        var providers = GetTestCases()
            .Select(row => LoadCase((string)row[0]))
            .Select(c => GoldenCorpusRunner.NormalizeProvider(c.Input.Provider))
            .Distinct()
            .ToList();

        providers.Should().Contain(new[] { "oracle", "sqlserver", "mysql", "postgresql" });
    }

    [Fact]
    public void LoadCase_MalformedJson_Throws()
    {
        var act = () => ParseCase("{ \"testCase\": \"X\", ", "inline.json");
        act.Should().Throw<InvalidDataException>().WithMessage("*inline.json*");
    }

    [Fact]
    public void LoadCase_MissingRequiredFields_Throws()
    {
        var act = () => ParseCase("{ \"testCase\": \"X\" }", "inline.json");
        act.Should().Throw<InvalidDataException>().WithMessage("*inline.json*");
    }

    internal static GoldenCorpusTestCase LoadCase(string relativePath)
    {
        var fullPath = Path.Combine(CorpusRoot, relativePath);
        return ParseCase(File.ReadAllText(fullPath), relativePath);
    }

    internal static GoldenCorpusTestCase ParseCase(string json, string source)
    {
        GoldenCorpusTestCase? testCase;
        try
        {
            testCase = JsonSerializer.Deserialize<GoldenCorpusTestCase>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Golden corpus case '{source}' is not valid JSON: {ex.Message}", ex);
        }

        if (testCase is null ||
            string.IsNullOrWhiteSpace(testCase.TestCase) ||
            string.IsNullOrWhiteSpace(testCase.Category) ||
            testCase.Input is null ||
            testCase.ExpectedDiagnostics is null ||
            testCase.Provenance is null)
        {
            throw new InvalidDataException(
                $"Golden corpus case '{source}' is missing a required field (testCase, category, input, expectedDiagnostics, provenance).");
        }

        foreach (var expected in testCase.ExpectedDiagnostics)
        {
            if (string.IsNullOrWhiteSpace(expected.RuleId) || string.IsNullOrWhiteSpace(expected.Severity) ||
                !Enum.TryParse<DiagnosticSeverity>(expected.Severity, ignoreCase: true, out _))
            {
                throw new InvalidDataException($"Golden corpus case '{source}' has an expected diagnostic without a valid ruleId/severity.");
            }
        }

        return testCase;
    }

    private static void ValidateProvenance(GoldenCorpusProvenance provenance, string source)
    {
        provenance.Source.Should().BeOneOf(new[] { "manual", "llm" }, $"provenance.source of '{source}'");
        DateOnly.TryParse(provenance.Date, System.Globalization.CultureInfo.InvariantCulture, out _)
            .Should().BeTrue($"provenance.date of '{source}' must be an ISO date (yyyy-MM-dd)");
        if (provenance.Source == "llm")
        {
            provenance.Model.Should().NotBeNullOrWhiteSpace($"an llm-sourced case ('{source}') must record the model");
        }
    }

    private static string Normalize(string severity) =>
        Enum.Parse<DiagnosticSeverity>(severity, ignoreCase: true).ToString();
}

/// <summary>Builds descriptors from a corpus case and runs the provider's Ready rules from <see cref="ProviderRuleCatalog"/>.</summary>
internal static class GoldenCorpusRunner
{
    public static string NormalizeProvider(string? provider)
    {
        var p = (provider ?? "oracle").Trim().ToLowerInvariant();
        return p switch
        {
            "postgres" or "pgsql" => "postgresql",
            "mssql" => "sqlserver",
            _ => p,
        };
    }

    public static IReadOnlyList<IContractRule> RulesFor(string provider) =>
        ProviderRuleCatalog.Get(NormalizeProvider(provider))
            .Where(registration => registration.Availability == RuleAvailability.Ready)
            .Select(registration => registration.Rule)
            .ToList();

    public static async Task<List<ContractViolation>> RunAsync(GoldenCorpusInput input)
    {
        var provider = NormalizeProvider(input.Provider);
        var contracts = BuildContracts(input, provider);
        var violations = new List<ContractViolation>();
        foreach (var rule in RulesFor(provider))
        {
            foreach (var contract in contracts)
            {
                violations.AddRange(await rule.ValidateAsync(contract, contracts, CancellationToken.None));
            }
        }

        return violations;
    }

    private static List<ContractDescriptor> BuildContracts(GoldenCorpusInput input, string provider)
    {
        var contracts = new List<ContractDescriptor>();

        if (input.Entity != null)
        {
            contracts.Add(new EntityDescriptor(
                Id: $"entity:{input.Entity.Name}",
                Name: input.Entity.Name,
                ClrTypeName: input.Entity.Name,
                TableName: input.Entity.TableName ?? GetTableNameForEntity(input.Entity.Name, input.DatabaseSchema),
                Properties: input.Entity.Properties.Select(p => new PropertyDescriptor(
                    Name: p.Name,
                    ClrTypeName: p.Type,
                    ColumnName: p.ColumnName ?? ToUpperSnakeCase(p.Name),
                    ColumnType: GetColumnType(provider, p.Type, p.MaxLength, p.IsUnicode),
                    IsNullable: p.IsNullable ?? p.Type.EndsWith('?'),
                    MaxLength: p.MaxLength,
                    IsPrimaryKey: p.IsPrimaryKey,
                    IsForeignKey: p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && !p.IsPrimaryKey)).ToList(),
                Location: null));
        }

        if (!string.IsNullOrEmpty(input.Sql))
        {
            contracts.Add(new RawSqlDescriptor(
                Id: "raw-sql:test",
                SqlText: input.Sql,
                Parameters: new List<ParameterDescriptor>(),
                ResultColumns: new List<ColumnDescriptor>(),
                Location: null,
                ConnectionProviderHint: provider));
        }

        var schemaTables = input.DatabaseSchema.Tables
            .Select(t => new DatabaseTableDescriptor(
                t.Name,
                t.Columns.Select(c => new ColumnDescriptor(
                    c.Name,
                    c.Type,
                    c.CharLength,
                    null,
                    null,
                    c.Nullable,
                    c.CharUsed,
                    c.CharLength)).ToList()))
            .ToList();

        contracts.Add(new DatabaseSchemaDescriptor(
            Id: "schema:ground-truth",
            Tables: schemaTables,
            LengthSemantics: input.LengthSemantics));

        return contracts;
    }

    private static string GetTableNameForEntity(string entityName, DatabaseSchema schema)
    {
        // Convention: UPPER_SNAKE_CASE plural (ORDERS); falls back to the first catalog table.
        var tableName = ToUpperSnakeCase(entityName) + "S";
        return schema.Tables.Any(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            ? tableName
            : schema.Tables.FirstOrDefault()?.Name ?? tableName;
    }

    private static string GetColumnType(string provider, string clrType, int? maxLength, bool isUnicode)
    {
        clrType = clrType.TrimEnd('?');
        return provider switch
        {
            "sqlserver" => clrType switch
            {
                "string" when isUnicode => maxLength.HasValue ? $"NVARCHAR({maxLength})" : "NVARCHAR(MAX)",
                "string" => maxLength.HasValue ? $"VARCHAR({maxLength})" : "VARCHAR(MAX)",
                "int" => "INT",
                "long" => "BIGINT",
                "decimal" => "DECIMAL(18,2)",
                "DateTime" => "DATETIME2",
                "bool" => "BIT",
                "Guid" => "UNIQUEIDENTIFIER",
                _ => "NVARCHAR(MAX)",
            },
            "mysql" => clrType switch
            {
                "string" => maxLength.HasValue ? $"VARCHAR({maxLength})" : "LONGTEXT",
                "int" => "INT",
                "long" => "BIGINT",
                "decimal" => "DECIMAL(18,2)",
                "DateTime" => "DATETIME(6)",
                "bool" => "TINYINT(1)",
                "Guid" => "CHAR(36)",
                _ => "LONGTEXT",
            },
            "postgresql" => clrType switch
            {
                "string" => maxLength.HasValue ? $"character varying({maxLength})" : "text",
                "int" => "integer",
                "long" => "bigint",
                "decimal" => "numeric",
                "DateTime" => "timestamp with time zone",
                "bool" => "boolean",
                "Guid" => "uuid",
                _ => "text",
            },
            _ => clrType switch
            {
                "string" when isUnicode => maxLength.HasValue ? $"NVARCHAR2({maxLength})" : "NVARCHAR2(2000)",
                "string" => maxLength.HasValue ? $"VARCHAR2({maxLength})" : "VARCHAR2(2000)",
                "int" => "NUMBER(10)",
                "long" => "NUMBER(19)",
                "decimal" => "NUMBER(18,2)",
                "DateTime" => "TIMESTAMP",
                "bool" => "NUMBER(1)",
                "Guid" => "RAW(16)",
                _ => "VARCHAR2(2000)",
            },
        };
    }

    private static string ToUpperSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = new System.Text.StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            if (i > 0 && char.IsUpper(input[i]))
            {
                result.Append('_');
            }

            result.Append(char.ToUpperInvariant(input[i]));
        }

        return result.ToString();
    }
}

/// <summary>Golden corpus test case model.</summary>
public class GoldenCorpusTestCase
{
    public string TestCase { get; set; } = "";

    public string Category { get; set; } = "";

    public string Description { get; set; } = "";

    public GoldenCorpusInput Input { get; set; } = new();

    public List<ExpectedDiagnostic> ExpectedDiagnostics { get; set; } = null!;

    public GoldenCorpusProvenance Provenance { get; set; } = null!;

    public string? Notes { get; set; }
}

/// <summary>Where a case came from: hand-written (<c>manual</c>) or harvested from model output (<c>llm</c>).</summary>
public class GoldenCorpusProvenance
{
    public string Source { get; set; } = "";

    public string? Model { get; set; }

    public string Date { get; set; } = "";
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

    public string? TableName { get; set; }

    public List<GoldenCorpusProperty> Properties { get; set; } = new();
}

public class GoldenCorpusProperty
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    public string? ColumnName { get; set; }

    public int? MaxLength { get; set; }

    public bool IsPrimaryKey { get; set; }

    /// <summary>Gets or sets explicit nullability; defaults to the CLR type (<c>int?</c> nullable, <c>string</c> non-nullable under NRT).</summary>
    public bool? IsNullable { get; set; }

    public bool IsUnicode { get; set; }
}

public class DatabaseColumn
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    [JsonPropertyName("char_length")]
    public int? CharLength { get; set; }

    [JsonPropertyName("char_used")]
    public string? CharUsed { get; set; }

    public bool Nullable { get; set; }
}

public class DatabaseSchema
{
    public List<DatabaseTable> Tables { get; set; } = new();
}

[JsonConverter(typeof(DatabaseTableConverter))]
public class DatabaseTable
{
    public string Name { get; set; } = "";

    public List<DatabaseColumn> Columns { get; set; } = new();
}

public class DatabaseTableConverter : JsonConverter<DatabaseTable>
{
    public override DatabaseTable Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A database table must be a JSON object.");
        }

        var table = new DatabaseTable();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return table;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a property name in a database table object.");
            }

            var prop = reader.GetString();
            reader.Read();
            switch (prop)
            {
                case "name":
                    table.Name = reader.GetString() ?? "";
                    break;
                case "columns":
                    table.Columns = ReadColumns(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unterminated database table object.");
    }

    private static List<DatabaseColumn> ReadColumns(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("'columns' must be an array.");
        }

        var columns = new List<DatabaseColumn>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                columns.Add(new DatabaseColumn { Name = reader.GetString() ?? "" });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                var col = JsonSerializer.Deserialize<DatabaseColumn>(ref reader, options);
                if (col != null)
                {
                    columns.Add(col);
                }
            }
            else
            {
                throw new JsonException("A column must be a name string or a column object.");
            }
        }

        return columns;
    }

    public override void Write(Utf8JsonWriter writer, DatabaseTable value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        writer.WriteStartArray("columns");
        foreach (var c in value.Columns)
        {
            JsonSerializer.Serialize(writer, c, options);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

public class ExpectedDiagnostic
{
    public string RuleId { get; set; } = "";

    public string MessageContains { get; set; } = "";

    public string Severity { get; set; } = "";
}
