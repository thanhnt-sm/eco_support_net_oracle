using FluentAssertions;
using Xunit;
using DataGuard.Core.Sources;

namespace DataGuard.Core.Tests;

public class StoredProcPrefixTests
{
    [Theory]
    [InlineData("PROC_UPDATE_CUSTOMER")]
    [InlineData("FNC_GET_TOTAL")]
    [InlineData("P_ARCHIVE")]
    public void IsSqlString_OraclePrefixes_ReturnTrue(string procName)
    {
        var result = ProjectCSharpSqlSource.IsSqlString(procName);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("sp_GetUser")]
    [InlineData("usp_UpdateOrder")]
    public void IsSqlString_ExistingPrefixes_Regression(string procName)
    {
        var result = ProjectCSharpSqlSource.IsSqlString(procName);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("PKG_CUSTOMER.GET_DETAILS")]
    [InlineData("MYSCHEMA.PROC")]
    public void IsSqlString_OracleDotNotation_ReturnTrue(string procName)
    {
        var result = ProjectCSharpSqlSource.IsSqlString(procName);
        result.Should().BeTrue();
    }
}
