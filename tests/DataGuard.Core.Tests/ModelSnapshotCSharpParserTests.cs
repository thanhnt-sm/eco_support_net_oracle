using DataGuard.Core.Sources;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class ModelSnapshotCSharpParserTests
{
    [Fact]
    public void Parse_ExtractsBoundedFluentEntityWithoutExecutingSource()
    {
        var result = ModelSnapshotCSharpParser.Parse("""
            class Snapshot { void Build(ModelBuilder modelBuilder) {
              modelBuilder.Entity<Customer>(b => { b.ToTable("CUSTOMERS"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasColumnName("FULL_NAME").HasMaxLength(120).IsRequired(); });
            }}
            """);

        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.TableName.Should().Be("CUSTOMERS");
        entity.Properties.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "Name", ColumnName = "FULL_NAME", MaxLength = (int?)120, IsNullable = false });
        result.Diagnostics.Should().BeEmpty();
    }

    private static IReadOnlyDictionary<string, DataGuard.Core.Abstractions.PropertyDescriptor> ParseProperties(string body)
    {
        var result = ModelSnapshotCSharpParser.Parse($$"""
            class Snapshot { void Build(ModelBuilder modelBuilder) {
              modelBuilder.Entity<Customer>(b => { {{body}} });
            } }
            """);
        result.Diagnostics.Should().BeEmpty();
        return result.Entities.Should().ContainSingle().Subject.Properties.ToDictionary(property => property.Name, StringComparer.Ordinal);
    }

    [Fact]
    public void Parse_GenericPropertyEmitsCanonicalClrTypeName()
    {
        var properties = ParseProperties("""
            b.Property<string>("Name").HasMaxLength(50);
            b.Property<System.Int32>("Id");
            b.Property<global::System.DateTime>("CreatedAt");
            b.Property<byte[]>("RowVersion");
            b.Property<Money>("Amount");
            """);

        properties["Name"].ClrTypeName.Should().Be("string");
        properties["Name"].MaxLength.Should().Be(50);
        properties["Id"].ClrTypeName.Should().Be("int");
        properties["CreatedAt"].ClrTypeName.Should().Be("DateTime");
        properties["RowVersion"].ClrTypeName.Should().Be("byte[]");
        properties["Amount"].ClrTypeName.Should().Be("Money", "an unknown CLR type keeps its source spelling");
    }

    [Fact]
    public void Parse_NullabilityFollowsTypeSyntaxAndIsRequired()
    {
        var properties = ParseProperties("""
            b.Property<int>("Id");
            b.Property<int?>("ParentId");
            b.Property<Nullable<decimal>>("Discount");
            b.Property<string>("Name").IsRequired();
            b.Property<string>("Notes");
            b.Property<string?>("Nickname");
            b.Property<string>("Code").IsRequired(false);
            b.Property(x => x.Legacy);
            b.Property(x => x.Mandatory).IsRequired();
            """);

        properties["Id"].IsNullable.Should().BeFalse("a non-nullable value type is required");
        properties["ParentId"].Should().BeEquivalentTo(new { ClrTypeName = "int?", IsNullable = true });
        properties["Discount"].Should().BeEquivalentTo(new { ClrTypeName = "decimal?", IsNullable = true });
        properties["Name"].IsNullable.Should().BeFalse();
        properties["Notes"].IsNullable.Should().BeTrue("a reference type without IsRequired is optional in EF snapshots");
        properties["Nickname"].Should().BeEquivalentTo(new { ClrTypeName = "string", IsNullable = true });
        properties["Code"].IsNullable.Should().BeTrue("IsRequired(false) keeps the column optional");
        properties["Legacy"].Should().BeEquivalentTo(new { ClrTypeName = "object", IsNullable = true });
        properties["Mandatory"].IsNullable.Should().BeFalse();
    }

    [Fact]
    public void Parse_IsUnicodeEmitsAnnotation()
    {
        var properties = ParseProperties("""
            b.Property<string>("Ascii").HasMaxLength(20).IsUnicode(false);
            b.Property<string>("Wide").IsUnicode();
            b.Property<string>("Default").HasMaxLength(20);
            """);

        properties["Ascii"].Annotations.Should().ContainKey("IsUnicode").WhoseValue.Should().Be(false);
        properties["Wide"].Annotations.Should().ContainKey("IsUnicode").WhoseValue.Should().Be(true);
        properties["Default"].Annotations.Should().BeNull("an unconfigured facet means EF's default (Unicode)");
    }

    [Fact]
    public void Parse_LambdaPropertyFallsBackToStringOnlyForStringFacets()
    {
        var properties = ParseProperties("""
            b.Property(x => x.Name).HasMaxLength(40);
            b.Property(x => x.Code).IsUnicode(false);
            b.Property(x => x.Total).HasColumnType("decimal(18,2)");
            """);

        properties["Name"].ClrTypeName.Should().Be("string");
        properties["Code"].ClrTypeName.Should().Be("string");
        properties["Total"].Should().BeEquivalentTo(new { ClrTypeName = "object", ColumnType = "decimal(18,2)" });
    }

    [Fact]
    public void Parse_StringKeysAndChainedFacetsDoNotLeakAcrossProperties()
    {
        var properties = ParseProperties("""
            b.Property<int>("Id").HasColumnName("ID");
            b.Property<string>("Name").HasColumnName("FULL_NAME").HasColumnType("varchar(80)");
            b.HasKey("Id");
            """);

        properties["Id"].Should().BeEquivalentTo(new { ColumnName = "ID", ColumnType = (string?)null, IsPrimaryKey = true });
        properties["Name"].Should().BeEquivalentTo(new { ColumnName = "FULL_NAME", ColumnType = "varchar(80)", IsPrimaryKey = false });
    }

    [Fact]
    public void Parse_SyntaxErrorReturnsDiagnosticInsteadOfEmptySuccess()
    {
        var result = ModelSnapshotCSharpParser.Parse("class Snapshot {");

        result.Entities.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "DG1303");
    }
}
