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

    [Fact]
    public void Parse_SyntaxErrorReturnsDiagnosticInsteadOfEmptySuccess()
    {
        var result = ModelSnapshotCSharpParser.Parse("class Snapshot {");

        result.Entities.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "DG1303");
    }
}
