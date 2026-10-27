using DataGuard.Core.Abstractions;
using DataGuard.Core.Sources;
using DataGuard.Oracle.Adapter;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class OracleDg009FallbackGuardTests
{
    public static IEnumerable<object?[]> FacetCases()
    {
        foreach (var key in new[] { "IsUnicode", "Unicode" })
        {
            foreach (var value in new object?[] { null, true, false, "true", "false", "FaLsE" })
            {
                foreach (var clr in new[] { "string", "System.String" })
                {
                    yield return new object?[] { key, value, clr, value is not false && value is not "false" && value is not "FaLsE" };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(FacetCases))]
    public async Task UnicodeFacets_RestrictFallbackInDetectorAndRule(string key, object? value, string clr, bool expected)
    {
        var property = Property(clr: clr) with
        {
            Annotations = value is null ? null : new Dictionary<string, object?> { [key] = value },
        };
        await AssertFallback(property, expected);
    }

    [Theory]
    [InlineData("Unrelated")]
    [InlineData("IsUnicode")]
    public async Task MissingOrNullUnicodeAnnotation_KeepsUnicodeDefault(string key)
    {
        await AssertFallback(
            Property() with
            {
                Annotations = new Dictionary<string, object?> { [key] = null },
            },
            true);
    }

    [Theory]
    [InlineData("CLOB")]
    [InlineData("NCLOB")]
    [InlineData(" clob ")]
    [InlineData(" nClOb\t")]
    public async Task ExplicitLobMapping_SuppressesFallbackOnly(string mapping)
    {
        await AssertFallback(Property(columnType: mapping), false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("NVARCHAR2(2000)")]
    [InlineData("nvarchar2")]
    [InlineData("VARCHAR2(40)")]
    public async Task NonLobMappings_RetainExistingFallbackBehavior(string? mapping)
    {
        await AssertFallback(Property(columnType: mapping), true);
    }

    [Theory]
    [InlineData("byte[]")]
    [InlineData("int")]
    [InlineData("object")]
    public async Task NonStringProperties_DoNotTriggerFallback(string clr)
    {
        await AssertFallback(Property(clr: clr), false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(4000)]
    public async Task ExplicitMaxLength_DoesNotTriggerFallback(int maxLength)
    {
        await AssertFallback(Property() with { MaxLength = maxLength }, false);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(".IsUnicode()", true)]
    [InlineData(".IsUnicode(true)", true)]
    [InlineData(".IsUnicode(false)", false)]
    [InlineData(".HasColumnType(\"CLOB\")", false)]
    [InlineData(".HasColumnType(\" nclob \")", false)]
    [InlineData(".HasColumnType(\"NVARCHAR2(2000)\")", true)]
    [InlineData(".HasMaxLength(100)", false)]
    public async Task SnapshotParser_ToRule_PreservesFallbackFacets(string fluent, bool expected)
    {
        var parsed = ModelSnapshotCSharpParser.Parse($$"""
            class Snapshot { void Build(ModelBuilder modelBuilder) {
                modelBuilder.Entity<Customer>(b => {
                    b.ToTable("CUSTOMERS");
                    b.Property<string>("Notes").HasColumnName("NOTES"){{fluent}};
                });
            } }
            """);
        parsed.Diagnostics.Should().BeEmpty();
        var entity = parsed.Entities.Should().ContainSingle().Subject;
        var schema = Schema("NCLOB");
        var detector = new LengthMismatchDetector().Detect(entity, schema.Tables.Single().Columns, LengthSemantics.Char).ToList();
        var rule = (await new InferredSizeFallbackRule().ValidateAsync(entity, new ContractDescriptor[] { schema })).ToList();
        detector.Count(v => v.RuleId == "DG009").Should().Be(expected ? 1 : 0);
        rule.Should().HaveCount(expected ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dg007AndDg008_StillReportLengthViolations(bool unicode)
    {
        var entity = Entity(Property(columnType: "NCLOB") with
        {
            MaxLength = 20,
            Annotations = new Dictionary<string, object?> { ["IsUnicode"] = unicode },
        });
        var column = new ColumnDescriptor("NOTES", "VARCHAR2", 5, null, null, true, "B", 5);
        var schema = new DatabaseSchemaDescriptor("schema:bounded", new[] { new DatabaseTableDescriptor("CUSTOMERS", new[] { column }) }, "BYTE");
        var detector = new LengthMismatchDetector().Detect(entity, new[] { column }, LengthSemantics.Byte).ToList();
        detector.Select(v => v.RuleId).Should().BeEquivalentTo("DG007", "DG008");
        (await new LengthExceedsColumnRule().ValidateAsync(entity, new ContractDescriptor[] { schema })).Should().ContainSingle();
        (await new ByteLengthOverflowRiskRule().ValidateAsync(entity, new ContractDescriptor[] { schema })).Should().ContainSingle();
        (await new InferredSizeFallbackRule().ValidateAsync(entity, new ContractDescriptor[] { schema })).Should().BeEmpty();
    }

    private static PropertyDescriptor Property(string clr = "string", string? columnType = null) =>
        new("Notes", clr, "NOTES", columnType, true, null, false, false, null);

    private static EntityDescriptor Entity(PropertyDescriptor property) => new("entity:customer", "Customer", "Customer", "CUSTOMERS", new[] { property });

    private static DatabaseSchemaDescriptor Schema(string lob) => new("schema:lob", new[]
    {
        new DatabaseTableDescriptor("CUSTOMERS", new[] { new ColumnDescriptor("NOTES", lob, null, null, null, true, null) }),
    }, "CHAR");

    private static async Task AssertFallback(PropertyDescriptor property, bool expected)
    {
        foreach (var lob in new[] { "CLOB", "NCLOB" })
        {
            var entity = Entity(property);
            var schema = Schema(lob);
            var detector = new LengthMismatchDetector().Detect(entity, schema.Tables.Single().Columns, LengthSemantics.Char).ToList();
            var rule = (await new InferredSizeFallbackRule().ValidateAsync(entity, new ContractDescriptor[] { schema })).ToList();
            detector.Count(v => v.RuleId == "DG009").Should().Be(expected ? 1 : 0);
            rule.Should().HaveCount(expected ? 1 : 0);
            rule.Should().NotContain(v => v.RuleId != "DG009");
            if (expected)
            {
                rule.Single().Severity.Should().Be(DiagnosticSeverity.Warning);
                rule.Single().Properties!["inferredType"].Should().Be("NVARCHAR2(2000)");
            }
        }
    }
}
