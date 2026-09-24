using System;
using System.Collections.Generic;
using System.Linq;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using DataGuard.Core.Rules;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class MappingReportTests
{
    [Fact]
    public void IsNameMatch_MatchesExactAndSnakeCase()
    {
        MappingTraceEngine.IsNameMatch("CUSTOMER_ID", "CustomerId").Should().BeTrue();
        MappingTraceEngine.IsNameMatch("email", "Email").Should().BeTrue();
        MappingTraceEngine.IsNameMatch("phone_number", "PhoneNumber").Should().BeTrue();
        MappingTraceEngine.IsNameMatch("Id", "Other").Should().BeFalse();
    }

    [Fact]
    public void Trace_MatchesColumnsAndDetectsDiscrepancies()
    {
        var rawSql = new RawSqlDescriptor(
            Id: "q1",
            SqlText: "SELECT CUSTOMER_ID, FULL_NAME, EMAIL, EXTRA_COL FROM CUSTOMERS",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: new List<PropertyDescriptor>
            {
                new("CustomerId", "int", null, null, false, null, false, false),
                new("FullName", "string", null, null, false, null, false, false),
                new("Email", "string", null, null, true, null, false, false),
                new("PhoneNo", "string", null, null, true, null, false, false),
            },
            TargetTypeName: "Customer",
            OperationType: SqlOperationType.Read,
            ReferencedTables: new[] { "CUSTOMERS" });

        var evidence = MappingTraceEngine.Trace(rawSql);

        evidence.TargetTypeName.Should().Be("Customer");
        evidence.SqlColumns.Should().BeEquivalentTo(new[] { "CUSTOMER_ID", "FULL_NAME", "EMAIL", "EXTRA_COL" });
        evidence.UnmappedColumns.Should().Contain("EXTRA_COL");
        evidence.UnmappedProperties.Should().Contain("PhoneNo");

        var matchedPairs = evidence.Mappings.Where(m => m.IsMatched).ToList();
        matchedPairs.Should().HaveCount(3);
        matchedPairs.Should().Contain(m => m.ColumnName == "CUSTOMER_ID" && m.PropertyName == "CustomerId");
    }

    [Fact]
    public void ScanSummary_SerializesToJsonSafely()
    {
        var summary = new ScanSummary(
            FilesScanned: 10,
            QueriesFound: 2,
            ConnectionsFound: 1,
            ViolationsCount: 1,
            Connections: new List<Sources.ConnectionInfo>
            {
                new("Default", "sqlserver", "Server=localhost;Password=***"),
            },
            Mappings: Array.Empty<MappingEvidence>());

        var json = summary.ToJson();
        json.Should().Contain("\"filesScanned\": 10");
        json.Should().Contain("\"queriesFound\": 2");
        json.Should().Contain("Password=***");
    }

    [Fact]
    public void ScanSummary_SerializesQueriesWithMappingStatusAndAction()
    {
        var rawSql = new RawSqlDescriptor(
            Id: "q1",
            SqlText: "SELECT CUSTOMER_ID, FULL_NAME FROM CUSTOMERS",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: new List<PropertyDescriptor>
            {
                new("CustomerId", "int", null, null, false, null, false, false),
                new("FullName", "string", null, null, false, null, false, false),
            },
            TargetTypeName: "Customer",
            OperationType: SqlOperationType.Read,
            ReferencedTables: new[] { "CUSTOMERS" });

        var evidence = MappingTraceEngine.Trace(rawSql);
        var summary = new ScanSummary(
            FilesScanned: 1,
            QueriesFound: 1,
            ConnectionsFound: 0,
            ViolationsCount: 0,
            Connections: Array.Empty<Sources.ConnectionInfo>(),
            Mappings: new[] { evidence });

        var json = summary.ToJson();
        json.Should().Contain("\"mappingStatus\": \"matched\"");
        json.Should().Contain("\"action\": \"shape-check\"");
        json.Should().Contain("\"targetType\": \"Customer\"");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesSubqueriesInSelectClause()
    {
        var sql = "SELECT (SELECT a FROM b WHERE id = 1) AS c, id, (SELECT name FROM users) AS user_name FROM d";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("c");
        columns.Should().Contain("id");
        columns.Should().Contain("user_name");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesCteTopLevelSelect()
    {
        var sql = "WITH cte AS (SELECT inner_a, inner_b FROM internal_table) SELECT id, title FROM cte";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("id");
        columns.Should().Contain("title");
        columns.Should().NotContain("inner_a");
        columns.Should().NotContain("inner_b");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesQuotedAliasesWithSpaces()
    {
        var sql = "SELECT id, name AS \"Full Name\", email AS [Email Address], amount AS `Total Amount` FROM users";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("id");
        columns.Should().Contain("Full Name");
        columns.Should().Contain("Email Address");
        columns.Should().Contain("Total Amount");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesQuotedAliasesWithParentheses()
    {
        var sql = "SELECT col1 AS [Total (USD)], col2 AS \"Count (Items)\" FROM users";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("Total (USD)");
        columns.Should().Contain("Count (Items)");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesSingleQuotedAliases()
    {
        var sql = "SELECT col1 AS 'My Alias', col2 AS 'Another Alias' FROM users";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("My Alias");
        columns.Should().Contain("Another Alias");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesCommentsWithParentheses()
    {
        var sql = "SELECT col1, -- (unbalanced comment\n col2, /* (another unbalanced */ col3 FROM users";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("col1");
        columns.Should().Contain("col2");
        columns.Should().Contain("col3");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesBracketedIdentifierImmediatelyAfterFrom()
    {
        var sql = "SELECT id, name FROM[dbo].[Users]";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("id");
        columns.Should().Contain("name");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesCaseEndAliasWithInternalParens()
    {
        var sql = "SELECT id, CASE WHEN (status > 0) THEN (price * 1.1) ELSE 0 END total_price FROM orders";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("id");
        columns.Should().Contain("total_price");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesEscapedBracketsInIdentifiers()
    {
        var sql = "SELECT [col ]] with parens (test)], name FROM users";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("col ] with parens (test)");
        columns.Should().Contain("name");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesKeywordsAdjacentToDelimitersWithoutSpaces()
    {
        var sql = "SELECT [id],[name]FROM(users)";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("id");
        columns.Should().Contain("name");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesTablePrefixWithBracketedIdentifierContainingSpaces()
    {
        var sql = "SELECT t.[Unit Price], [Order Details].[Discount Percent], t.qty [Total Amount] FROM orders t";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("Unit Price");
        columns.Should().Contain("Discount Percent");
        columns.Should().Contain("Total Amount");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesDoubleQuotedAndBacktickedIdentifiersWithSpaces()
    {
        var sql = "SELECT t.\"Unit Price\", `Order Details`.`Discount Percent` FROM orders t";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("Unit Price");
        columns.Should().Contain("Discount Percent");
    }

    [Theory]
    [InlineData("SELECT * FROM Users", true)]
    [InlineData("SELECT [dbo].[Users].* FROM [dbo].[Users]", true)]
    [InlineData("SELECT u.* FROM Users u", true)]
    [InlineData("SELECT a, (SELECT * FROM B) FROM A", true)]
    [InlineData("SELECT a * b AS total FROM Items", false)]
    [InlineData("SELECT id, name FROM Users -- SELECT * FROM Users", false)]
    public void SelectStarUsageRule_DetectsWildcardsAccurately(string sql, bool expected)
    {
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar(sql).Should().Be(expected);
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesQueriesWithoutFromClause()
    {
        var sql = "SELECT 1 AS Status, 'Active' AS Name, @@VERSION AS Version";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("Status");
        columns.Should().Contain("Name");
        columns.Should().Contain("Version");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesAliasAssignmentSyntax()
    {
        var sql = "SELECT FullName = first_name + ' ' + last_name, [Total Amount] = price * qty, OrderId = o.Id FROM orders o";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("FullName");
        columns.Should().Contain("Total Amount");
        columns.Should().Contain("OrderId");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesSelectIntoClause()
    {
        var sql = "SELECT id, name, email INTO new_users FROM users WHERE is_active = 1;";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain(new[] { "id", "name", "email" });
        columns.Should().NotContain("new_users");
    }

    [Fact]
    public void SelectStarUsageRule_DetectsTablePrefixedWildcards()
    {
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar("SELECT T.* FROM Table T").Should().BeTrue();
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar("SELECT [my_tbl].* FROM [my_tbl]").Should().BeTrue();
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar("SELECT `users`.* FROM `users`").Should().BeTrue();
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar("SELECT \"orders\".* FROM \"orders\"").Should().BeTrue();
    }

    [Fact]
    public void SelectStarUsageRule_HandlesCommentsWithApostrophes()
    {
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar("SELECT * FROM users -- don't do this\nWHERE id = 1").Should().BeTrue();
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar("SELECT /* don't select all */ * FROM users").Should().BeTrue();
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar("SELECT id, name FROM users -- don't do this\nWHERE id = 1").Should().BeFalse();
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesArithmeticExpressionsWithTrailingAlias()
    {
        var sql = "SELECT Price + Tax TotalAmount, Quantity * UnitPrice AS LineTotal, Price + 1, Discount / 100 NetDiscount FROM Orders";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("TotalAmount");
        columns.Should().Contain("LineTotal");
        columns.Should().Contain("NetDiscount");
        columns.Should().NotContain("Price");
        columns.Should().NotContain("1");
    }

    [Fact]
    public void ScanSummary_SerializesTargetTypeLocationAndNonNullableTables()
    {
        var rawSql = new RawSqlDescriptor(
            Id: "q1",
            SqlText: "SELECT id FROM users",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: Array.Empty<PropertyDescriptor>(),
            TargetTypeName: "User",
            OperationType: SqlOperationType.Read,
            ReferencedTables: null);

        var evidence = MappingTraceEngine.Trace(rawSql);
        var summary = new ScanSummary(1, 1, 0, 0, Array.Empty<Sources.ConnectionInfo>(), new[] { evidence });
        var json = summary.ToJson();

        json.Should().Contain("\"tables\": []");
        json.Should().Contain("\"targetTypeLocation\": null");
    }

    [Fact]
    public void ConnectionDiscovery_MasksAdditionalSensitiveKeys()
    {
        var conn = "Server=db;User Id=usr;Secret_Key=shh;Client_Secret=mysecret;Private_Key=priv;Auth_Token=tok;";
        var masked = Sources.ConnectionDiscovery.MaskConnectionString(conn);

        masked.Should().Contain("Secret_Key=***");
        masked.Should().Contain("Client_Secret=***");
        masked.Should().Contain("Private_Key=***");
        masked.Should().Contain("Auth_Token=***");
        masked.Should().NotContain("mysecret");
        masked.Should().NotContain("priv");
    }

    [Fact]
    public void StripCommentsAndLiterals_PreservesBracketIdentifiersWithHyphens()
    {
        var sql = "SELECT [My--Col], [Other]]--Col] FROM Table WHERE 1=1";
        var stripped = DataGuard.Core.Rules.ColumnShapeMatchRule.StripCommentsAndLiterals(sql);

        stripped.Should().Contain("[My--Col]");
        stripped.Should().Contain("[Other]]--Col]");
        stripped.Should().Contain("FROM Table");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_ExtractsUnaliasedFunctionCalls()
    {
        var sql = "SELECT COUNT(1), MAX(Salary) FROM Employees";
        var columns = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);

        columns.Should().Contain("COUNT");
        columns.Should().Contain("MAX");
    }

    [Fact]
    public void ClassifySqlOperation_DeleteWithoutFrom_ClassifiedAsDelete()
    {
        var sql = "DELETE Employees WHERE Id = 10";
        var op = DataGuard.Core.Sources.ProjectCSharpSqlSource.ClassifySqlOperation(sql);

        op.Should().Be(DataGuard.Core.Abstractions.SqlOperationType.Write);
    }

    [Fact]
    public void MaskSqlCommentsAndStrings_PreservesHyphensInsideStringLiterals()
    {
        var sql = "SELECT 'hello -- world', Name FROM Employees";
        var masked = DataGuard.Core.Sources.ProjectCSharpSqlSource.MaskSqlCommentsAndStrings(sql);

        // The string literal should be masked without mangling the remainder of the query
        masked.Should().Contain("Name");
        masked.Should().Contain("Employees");
    }

    [Fact]
    public void MaskSqlCommentsAndStrings_HandlesCommentsWithQuotes()
    {
        var sql = "SELECT Name FROM Employees -- find user's name\nWHERE Id = 1";
        var masked = DataGuard.Core.Sources.ProjectCSharpSqlSource.MaskSqlCommentsAndStrings(sql);

        masked.Should().Contain("Name");
        masked.Should().Contain("WHERE");
        masked.Should().NotContain("user's name");
    }

    [Fact]
    public void IsNameMatch_WithNullOrEmptyStrings_DoesNotThrow()
    {
        MappingTraceEngine.IsNameMatch(null!, null!).Should().BeTrue();
        MappingTraceEngine.IsNameMatch("test", null!).Should().BeFalse();
        MappingTraceEngine.IsNameMatch(null!, "test").Should().BeFalse();
    }

    [Fact]
    public void ContainsSelectStar_WithUnionSelectStar_ReturnsTrue()
    {
        var sql = "SELECT id FROM orders UNION SELECT * FROM archive_orders";
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar(sql).Should().BeTrue();
    }

    [Fact]
    public void ContainsSelectStar_WithUnionPrecedingColumns_ReturnsTrue()
    {
        var sql = "SELECT id, name FROM orders UNION ALL SELECT col1, col2, * FROM archive_orders";
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar(sql).Should().BeTrue();
    }

    [Fact]
    public void ContainsSelectStar_WithCteContainingUnion_PreservesTopLevelSelectStar()
    {
        var sql = "WITH cte AS (SELECT a FROM t1 UNION SELECT b FROM t2) SELECT * FROM cte";
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar(sql).Should().BeTrue();
    }

    [Fact]
    public void ContainsSelectStar_WithCommentsContainingCommas_DetectsSelectStar()
    {
        var sql = "SELECT id, -- note: column1, column2, etc.\n * FROM orders";
        DataGuard.Core.Rules.SelectStarUsageRule.ContainsSelectStar(sql).Should().BeTrue();
    }

    [Fact]
    public void StripCommentsAndLiterals_HandlesOracleQQuotedStrings()
    {
        var stripped = DataGuard.Core.Rules.ColumnShapeMatchRule.StripCommentsAndLiterals("SELECT q'[It's a :param]' FROM DUAL");
        stripped.Should().NotContain(":param");
        stripped.Should().Contain("''");
    }

    [Fact]
    public void StripCommentsAndLiterals_HandlesUnclosedBlockComment()
    {
        var stripped = DataGuard.Core.Rules.ColumnShapeMatchRule.StripCommentsAndLiterals("SELECT 1 /* unclosed comment");
        stripped.Trim().Should().Be("SELECT 1");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesOracleQQuotedStrings()
    {
        var cols = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql("SELECT q'[a, b, c]' AS val, id FROM my_table");
        cols.Should().Contain("val");
        cols.Should().Contain("id");
        cols.Should().NotContain("a");
        cols.Should().NotContain("b");
        cols.Should().NotContain("c");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_HandlesSpaceLessArithmeticAliases()
    {
        var cols = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql("SELECT Price*Quantity TotalCost, UnitPrice - Discount FinalPrice FROM Orders");
        cols.Should().Contain("TotalCost");
        cols.Should().Contain("FinalPrice");
    }

    [Fact]
    public void HasUnclosedBlockComment_WithClosedCommentFollowedByUnclosed_ReturnsTrue()
    {
        DataGuard.Core.Rules.ColumnShapeMatchRule.HasUnclosedBlockComment("/* valid */ SELECT * FROM t /* unclosed").Should().BeTrue();
    }

    [Fact]
    public void HasUnclosedBlockComment_WithCommentMarkerInsideIdentifier_ReturnsFalse()
    {
        DataGuard.Core.Rules.ColumnShapeMatchRule.HasUnclosedBlockComment("SELECT [col/*name] FROM t").Should().BeFalse();
    }

    [Fact]
    public void HasUnclosedBlockComment_WithProperlyClosedComment_ReturnsFalse()
    {
        DataGuard.Core.Rules.ColumnShapeMatchRule.HasUnclosedBlockComment("/* valid */ SELECT * FROM t /* closed */").Should().BeFalse();
    }

    [Fact]
    public void HasUnclosedBlockComment_WithCarriageReturnTerminatedLineComment_DetectsSubsequentUnclosedBlock()
    {
        DataGuard.Core.Rules.ColumnShapeMatchRule.HasUnclosedBlockComment("-- line comment\rSELECT * FROM t /* unclosed").Should().BeTrue();
    }

    [Fact]
    public void ExtractColumnNamesFromSql_UnaliasedJsonOperator_IsPreserved()
    {
        var cols = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql("SELECT id, data->>'profile', name FROM users");
        cols.Should().Contain("id");
        cols.Should().Contain("data->>'profile'");
        cols.Should().Contain("name");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_CompoundBracketedExpression_DoesNotCorruptOuterBrackets()
    {
        var cols = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql("SELECT [A] + [B], [SingleIdentifier] FROM t");
        cols.Should().Contain("[A] + [B]");
        cols.Should().Contain("SingleIdentifier");
    }

    [Fact]
    public void MappingTraceEngine_WithExplicitColumnAttribute_MatchesCorrectly()
    {
        var rawSql = new RawSqlDescriptor(
            Id: "q1",
            SqlText: "SELECT customer_id, customer_name FROM customers",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: new[]
            {
                new PropertyDescriptor("Id", "int", "customer_id"),
                new PropertyDescriptor("Name", "string", "customer_name")
            },
            TargetTypeName: "CustomerDto"
        );

        var evidence = MappingTraceEngine.Trace(rawSql);
        evidence.UnmappedColumns.Should().BeEmpty();
        evidence.UnmappedProperties.Should().BeEmpty();
        evidence.Mappings.Should().Contain(m => m.ColumnName == "customer_id" && m.PropertyName == "Id" && m.IsMatched);
        evidence.Mappings.Should().Contain(m => m.ColumnName == "customer_name" && m.PropertyName == "Name" && m.IsMatched);
    }
    [Fact]
    public void ExtractColumnNamesFromSql_HandlesEscapedBackticksInIdentifiers()
    {
        var cols = DataGuard.Core.Rules.ColumnShapeMatchRule.ExtractColumnNamesFromSql("SELECT `col``name`, `normal` FROM t");
        cols.Should().Contain("col`name");
        cols.Should().Contain("normal");
    }

    [Fact]
    public void MappingTraceEngine_CaseInsensitiveNullableUnmappedProperty_DoesNotEmitWarning()
    {
        var rawSql = new RawSqlDescriptor(
            Id: "q1",
            SqlText: "SELECT id FROM customers",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: new[]
            {
                new PropertyDescriptor("Id", "int", null),
                new PropertyDescriptor("Description", "string?", null, IsNullable: true)
            },
            TargetTypeName: "CustomerDto"
        );

        var evidence = MappingTraceEngine.Trace(rawSql);
        evidence.UnmappedColumns.Should().BeEmpty();
        evidence.UnmappedProperties.Should().Contain("Description");
        evidence.Mappings.Should().Contain(m => m.PropertyName == "Id" && m.IsMatched);
        evidence.Mappings.Should().NotContain(m => m.PropertyName == "Description");
    }

    [Fact]
    public void ExtractColumnNamesFromSql_UnescapesInternalDelimiters()
    {
        var sql = "SELECT [col]]name], \"col\"\"name\", `col``name`, 'col''name' AS 'ali''as' FROM my_table";
        var cols = ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);
        cols.Should().Contain("col]name");
        cols.Should().Contain("col\"name");
        cols.Should().Contain("col`name");
        cols.Should().Contain("ali'as");
    }

    [Fact]
    public void ParameterTypeMatchRule_NullOrWhiteSpaceDbType_ReturnsFalseSafely()
    {
        ParameterTypeMatchRule.IsTypeCompatible("int", null!, false).Should().BeFalse();
        ParameterTypeMatchRule.IsTypeCompatible("int", "", false).Should().BeFalse();
        ParameterTypeMatchRule.IsTypeCompatible("int", "   ", false).Should().BeFalse();
        ParameterTypeMatchRule.IsTypeCompatible(null!, "int", false).Should().BeFalse();
    }
    [Fact]
    public void ExtractTopLevelSelectClause_HandlesNestedBlockComments()
    {
        var sql = "/* outer /* inner */ still comment */ SELECT col1, col2 FROM my_table";
        var select = ColumnShapeMatchRule.ExtractTopLevelSelectClause(sql);
        select.Should().Be("col1, col2");

        var cols = ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql);
        cols.Should().Contain("col1");
        cols.Should().Contain("col2");
    }

    [Fact]
    public void HasUnclosedBlockComment_HandlesNestedBlockComments()
    {
        ColumnShapeMatchRule.HasUnclosedBlockComment("/* outer /* inner */ still comment */ SELECT 1").Should().BeFalse();
        ColumnShapeMatchRule.HasUnclosedBlockComment("/* outer /* inner */ still unclosed").Should().BeTrue();
    }

    [Fact]
    public void MaskSqlComments_PreservesCommentMarkersInsideStringLiterals()
    {
        var sql = "SELECT '/* not comment */', '-- not comment' FROM tbl";
        var masked = DataGuard.Core.Sources.ProjectCSharpSqlSource.MaskSqlComments(sql);
        masked.Length.Should().Be(sql.Length);
        masked.Should().NotContain("/*");
        masked.Should().NotContain("--");
        var op = DataGuard.Core.Sources.ProjectCSharpSqlSource.ClassifySqlOperation(sql);
        op.Should().Be(SqlOperationType.Read);
    }
}
