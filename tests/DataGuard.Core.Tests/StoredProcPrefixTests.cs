using FluentAssertions;
using Xunit;
using DataGuard.Core.Sources;

namespace DataGuard.Core.Tests;

/// <summary>
/// Bare procedure names are recognized by <c>IsProcedureName</c> (used for <c>CommandType.StoredProcedure</c> command
/// texts) and are no longer treated as SQL statements by <c>IsSqlString</c> (red-team H9: prefix/dot heuristics turned
/// arbitrary identifiers into SQL).
/// </summary>
public class StoredProcPrefixTests
{
    [Theory]
    [InlineData("PROC_UPDATE_CUSTOMER")]
    [InlineData("FNC_GET_TOTAL")]
    [InlineData("P_ARCHIVE")]
    [InlineData("sp_GetUser")]
    [InlineData("usp_UpdateOrder")]
    [InlineData("PKG_CUSTOMER.GET_DETAILS")]
    [InlineData("MYSCHEMA.PROC")]
    [InlineData("HR.PKG_EMP.GET_EMPLOYEE")]
    [InlineData("[dbo].[Get Customer]")]
    public void IsProcedureName_PlainAndQualifiedNames_ReturnTrue(string procName)
    {
        ProjectCSharpSqlSource.IsProcedureName(procName).Should().BeTrue();
    }

    [Theory]
    [InlineData("PROC_UPDATE_CUSTOMER")]
    [InlineData("sp_GetUser")]
    [InlineData("PKG_CUSTOMER.GET_DETAILS")]
    public void IsSqlString_BareProcedureNames_AreNotStatements(string procName)
    {
        ProjectCSharpSqlSource.IsSqlString(procName).Should().BeFalse();
    }

    [Theory]
    [InlineData("EXEC dbo.usp_x @a")]
    [InlineData("SELECT * FROM x")]
    [InlineData("Please update your profile")]
    public void IsProcedureName_StatementsAndSentences_ReturnFalse(string text)
    {
        ProjectCSharpSqlSource.IsProcedureName(text).Should().BeFalse();
    }
}
