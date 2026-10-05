using System;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Oracle.Adapter;
using FluentAssertions;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Explicit Oracle Free provider gate enabled by
/// DATAGUARD_REQUIRE_LIVE_RELATIONAL=1.
/// </summary>
public class OracleIntegrationTests : IAsyncLifetime
{
    private const string RequireLiveRelationalVariable = "DATAGUARD_REQUIRE_LIVE_RELATIONAL";
    private OracleContainer? _container;

    public async Task InitializeAsync()
    {
        if (!RequiresLiveRelational())
        {
            return;
        }

        _container = new OracleBuilder("gvenzl/oracle-free:23-slim-faststart")
            .WithDatabase("FREEPDB1")
            .WithUsername("dataguard")
            .WithPassword("DataGuard_Test_1!")
            .Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task AllArgumentsReader_ReadsProcedureParametersFromLiveOracle()
    {
        if (_container == null)
        {
            return; // xUnit 2.9 has no supported dynamic skip API.
        }

        await using (var connection = new OracleConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE OR REPLACE PROCEDURE GET_CUSTOMER(
                    P_ID IN NUMBER,
                    P_NAME OUT VARCHAR2)
                AS
                BEGIN
                    P_NAME := 'Ada';
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var reader = new AllArgumentsReader(_container.GetConnectionString(), new DataGuard.Core.Models.OracleConfiguration());
        var parameters = await reader.GetParametersAsync("DATAGUARD", string.Empty, "GET_CUSTOMER");

        parameters.Should().Contain(parameter => parameter.Name == "P_ID" && parameter.Direction == ParameterDirection.Input);
        parameters.Should().Contain(parameter => parameter.Name == "P_NAME" && parameter.Direction == ParameterDirection.Output);
    }

    [Fact]
    public async Task Catalog_ReadsPackageOverloadsZeroArgumentSubprogramsAndStandaloneProcedures()
    {
        if (_container == null)
        {
            return; // xUnit 2.9 has no supported dynamic skip API.
        }

        var connectionString = _container.GetConnectionString();
        await ExecuteDdlAsync(
            connectionString,
            """
            CREATE OR REPLACE PACKAGE CUST_PKG AS
              PROCEDURE GET_ONE(P_ID IN NUMBER, P_NAME OUT VARCHAR2);
              PROCEDURE GET_ONE(P_ID IN NUMBER, P_CODE IN VARCHAR2, P_NAME OUT VARCHAR2);
              FUNCTION COUNT_ALL RETURN NUMBER;
              FUNCTION COUNT_ALL(P_MIN IN NUMBER) RETURN NUMBER;
              PROCEDURE NOARGS;
              PROCEDURE WITH_DEF(P_A IN NUMBER, P_B IN VARCHAR2 DEFAULT 'x');
              PROCEDURE GET_CUR(P_ID IN NUMBER, P_CUR OUT SYS_REFCURSOR);
            END CUST_PKG;
            """,
            """
            CREATE OR REPLACE PACKAGE BODY CUST_PKG AS
              PROCEDURE GET_ONE(P_ID IN NUMBER, P_NAME OUT VARCHAR2) IS BEGIN P_NAME := 'a'; END;
              PROCEDURE GET_ONE(P_ID IN NUMBER, P_CODE IN VARCHAR2, P_NAME OUT VARCHAR2) IS BEGIN P_NAME := 'b'; END;
              FUNCTION COUNT_ALL RETURN NUMBER IS BEGIN RETURN 0; END;
              FUNCTION COUNT_ALL(P_MIN IN NUMBER) RETURN NUMBER IS BEGIN RETURN 1; END;
              PROCEDURE NOARGS IS BEGIN NULL; END;
              PROCEDURE WITH_DEF(P_A IN NUMBER, P_B IN VARCHAR2 DEFAULT 'x') IS BEGIN NULL; END;
              PROCEDURE GET_CUR(P_ID IN NUMBER, P_CUR OUT SYS_REFCURSOR) IS
              BEGIN
                OPEN P_CUR FOR SELECT P_ID AS ID, CAST('x' AS VARCHAR2(30)) AS NAME FROM DUAL;
              END;
            END CUST_PKG;
            """,
            "CREATE OR REPLACE PROCEDURE STANDALONE_P(P_X IN NUMBER) AS BEGIN NULL; END;",
            "CREATE TABLE CUSTOMERS (ID NUMBER(10) NOT NULL, NAME VARCHAR2(300 BYTE), NOTE NVARCHAR2(100))");

        var reader = new AllArgumentsReader(connectionString, new DataGuard.Core.Models.OracleConfiguration());

        // Previously ORA-00904 "PACKAGE_NAME": invalid identifier (ALL_PROCEDURES has no such column).
        var packageNames = await reader.GetProcedureNamesAsync("dataguard", "cust_pkg");
        packageNames.Should().BeEquivalentTo("COUNT_ALL", "GET_CUR", "GET_ONE", "NOARGS", "WITH_DEF");
        (await reader.GetProcedureNamesAsync("DATAGUARD")).Should().Contain("STANDALONE_P").And.NotContain("CUST_PKG");

        var packageOnly = await reader.GetProceduresAsync("DATAGUARD", "CUST_PKG");
        packageOnly.Should().OnlyContain(entry => entry.PackageName == "CUST_PKG");

        var all = await reader.GetProceduresAsync("dataguard");
        all.Where(entry => entry.Name == "GET_ONE").Select(entry => entry.Parameters.Count).Should().BeEquivalentTo(new[] { 2, 3 });
        var countAll = all.Where(entry => entry.Name == "COUNT_ALL").ToList();
        countAll.Should().HaveCount(2);
        countAll.Should().ContainSingle(entry => entry.Parameters.Count == 0 && entry.ReturnType == "NUMBER");
        all.Should().ContainSingle(entry => entry.Name == "NOARGS" && entry.Parameters.Count == 0);
        all.Single(entry => entry.Name == "WITH_DEF").Parameters.Single(p => p.Name == "P_B").HasDefault.Should().BeTrue();
        var standalone = all.Should().ContainSingle(entry => entry.Name == "STANDALONE_P").Subject;
        standalone.PackageName.Should().BeNull();
        standalone.Id.Should().Be("oracle:DATAGUARD._.STANDALONE_P#1");
        all.Where(entry => entry.PackageName == "CUST_PKG").Should().OnlyContain(entry => entry.Id.StartsWith("oracle:DATAGUARD.CUST_PKG.", StringComparison.Ordinal));

        (await reader.GetOverloadsAsync("DATAGUARD", "CUST_PKG", "COUNT_ALL")).Should().HaveCount(2);
        (await reader.GetParametersAsync("DATAGUARD", string.Empty, "STANDALONE_P")).Should().ContainSingle(p => p.Name == "P_X");

        var described = await new RefCursorDescriber(connectionString).DescribeRefCursorAsync(
            "DATAGUARD",
            "CUST_PKG",
            "GET_CUR",
            new System.Collections.Generic.Dictionary<string, object> { ["P_ID"] = DBNull.Value },
            "P_CUR");
        described.Select(column => column.Name).Should().Equal("ID", "NAME");

        var contracts = await OracleCatalogBuilder.BuildAsync(
            connectionString,
            "DATAGUARD",
            new DataGuard.Core.Models.OracleConfiguration { DescribeRefCursors = true });
        var procedures = contracts.OfType<StoredProcedureDescriptor>().ToList();
        procedures.Where(p => p.Name == "GET_ONE").Should().HaveCount(2).And.OnlyContain(p => p.PackageName == "CUST_PKG");
        procedures.Where(p => p.Name == "COUNT_ALL").Should().HaveCount(2);
        procedures.Should().Contain(p => p.Name == "STANDALONE_P" && p.PackageName == string.Empty);
        var getCur = procedures.Single(p => p.Name == "GET_CUR");
        getCur.ReturnsRefCursor.Should().BeTrue();
        getCur.ResultColumns.Select(column => column.Name).Should().Equal("ID", "NAME");

        var schema = contracts.OfType<OracleDatabaseSchemaDescriptor>().Should().ContainSingle().Subject;
        schema.DatabaseCharset.Should().NotBeNullOrEmpty();
        schema.NationalCharset.Should().NotBeNullOrEmpty();
        schema.MaxStringSize.Should().BeOneOf("STANDARD", "EXTENDED");
        var customers = schema.Tables.Should().ContainSingle(table => table.Name == "CUSTOMERS").Subject;
        customers.Schema.Should().Be("DATAGUARD");
        customers.Columns.Single(column => column.Name == "NAME").Charset.Should().Be(schema.DatabaseCharset);
        customers.Columns.Single(column => column.Name == "NOTE").Charset.Should().Be(schema.NationalCharset);

        var nls = await new NlsSessionReader(connectionString).GetNlsParametersAsync();
        nls.CharacterSet.Should().NotBe("UNKNOWN");
    }

    private static async Task ExecuteDdlAsync(string connectionString, params string[] statements)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();
        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static bool RequiresLiveRelational()
        => string.Equals(Environment.GetEnvironmentVariable(RequireLiveRelationalVariable), "1", StringComparison.Ordinal);
}
