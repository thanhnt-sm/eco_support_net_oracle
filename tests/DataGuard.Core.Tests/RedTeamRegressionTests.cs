// Copyright (c) DataGuard contributors. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Sources;
using DataGuard.Oracle.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// TDD regression safety net — documents CURRENT behavior before the red-team
/// plan revisions are applied. Tests MUST pass both before and after Step 3.I.
/// </summary>
public class RedTeamRegressionTests : IDisposable
{
    private readonly string _tempDir;

    public RedTeamRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dataguard-redteam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Non-fatal cleanup
        }
    }

    // ── RawSqlDescriptor defaults (3.I.1) ────────────────────────────────────
    [Fact]
    public void RawSqlDescriptor_DefaultIsStoredProcedure_IsFalse()
    {
        var desc = new RawSqlDescriptor("id1", "SELECT 1", new List<ParameterDescriptor>(), new List<ColumnDescriptor>());
        desc.IsStoredProcedure.Should().BeFalse();
    }

    [Fact]
    public void RawSqlDescriptor_DefaultProcedureName_IsNull()
    {
        var desc = new RawSqlDescriptor("id1", "SELECT 1", new List<ParameterDescriptor>(), new List<ColumnDescriptor>());
        desc.ProcedureName.Should().BeNull();
    }

    [Fact]
    public void RawSqlDescriptor_CanRoundtripIsStoredProcedureAndProcedureName()
    {
        var desc = new RawSqlDescriptor("id1", "EXEC GET_CUSTOMER", new List<ParameterDescriptor>(), new List<ColumnDescriptor>())
            with
        { IsStoredProcedure = true, ProcedureName = "GET_CUSTOMER" };
        desc.IsStoredProcedure.Should().BeTrue();
        desc.ProcedureName.Should().Be("GET_CUSTOMER");
    }

    [Fact]
    public void RawSqlDescriptor_PositionalConstruction_DefaultsIsStoredProcedureFalse()
    {
        var empty = new List<ParameterDescriptor>();
        var cols = new List<ColumnDescriptor>();

        // Validates backward compat — representative existing call-site shapes.
        var d1 = new RawSqlDescriptor("a", "SELECT 1", empty, cols);
        var d2 = new RawSqlDescriptor("b", "SELECT 2", empty, cols, Location: null);
        var d3 = new RawSqlDescriptor("c", "SELECT 3", empty, cols, null, null, "MyType");
        var d6 = new RawSqlDescriptor(
            "f", "SELECT 6", empty, cols, null, null, null, SqlOperationType.Unknown, null, "sqlserver");

        d1.IsStoredProcedure.Should().BeFalse();
        d2.IsStoredProcedure.Should().BeFalse();
        d3.IsStoredProcedure.Should().BeFalse();
        d6.IsStoredProcedure.Should().BeFalse();
    }

    // ── DG101 ParameterCountRule (3.I.3) ─────────────────────────────────────
    [Fact]
    public async Task DG101_ExecWithIsStoredProcedureTrue_DoesNotFire()
    {
        var rule = new ParameterCountRule();
        var desc = new RawSqlDescriptor("x", "EXEC GET_CUSTOMER_BY_ID", new List<ParameterDescriptor>(), new List<ColumnDescriptor>())
            with
        { IsStoredProcedure = true };
        var violations = await rule.ValidateAsync(desc, new List<ContractDescriptor>(), CancellationToken.None);
        violations.Should().BeEmpty("IsStoredProcedure=true must suppress DG101");
    }

    [Fact]
    public async Task DG101_ExecWithInlineParams_DoesNotFire()
    {
        var rule = new ParameterCountRule();
        var desc = new RawSqlDescriptor(
            "x", "EXEC GetOrders @UserId=5, @Status='active'", new List<ParameterDescriptor>(), new List<ColumnDescriptor>());
        var violations = await rule.ValidateAsync(desc, new List<ContractDescriptor>(), CancellationToken.None);
        violations.Should().BeEmpty("EXEC with inline @params must not fire DG101");
    }

    [Fact]
    public async Task DG101_ExecNoParams_IsStoredProcedureFalse_Fires()
    {
        var rule = new ParameterCountRule();
        var desc = new RawSqlDescriptor(
            "x", "EXEC GET_CUSTOMER_BY_ID", new List<ParameterDescriptor>(), new List<ColumnDescriptor>());
        var violations = await rule.ValidateAsync(desc, new List<ContractDescriptor>(), CancellationToken.None);
        violations.Should().ContainSingle(v => v.RuleId == "DG101");
    }

    // ── OracleDialectChecker property keys (3.I.4) ───────────────────────────
    [Fact]
    public void OracleDialect_DG010_Keyword_HasKeywordKey()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckOracleSyntaxInNonOracleContext("SELECT SYSDATE FROM DUAL", false);
        violations.Should().Contain(v =>
            v.RuleId == "DG010"
            && v.Properties != null
            && v.Properties.ContainsKey("keyword")
            && v.Properties["keyword"]!.ToString()!.Contains("SYSDATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OracleDialect_DG010_Operator_HasOperatorKey()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckOracleSyntaxInNonOracleContext("SELECT a (+) b FROM t", false);
        violations.Should().Contain(v =>
            v.RuleId == "DG010"
            && v.Properties != null
            && v.Properties.ContainsKey("operator"));
    }

    [Fact]
    public void OracleDialect_DG010_Keyword_HasMigrationKey()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckOracleSyntaxInNonOracleContext("SELECT SYSDATE FROM DUAL", false);
        violations.Should().Contain(
            v => v.RuleId == "DG010" && v.Properties != null && v.Properties.ContainsKey("migration"),
            "DG010 must include migration hint after 3.I.4");
    }

    [Fact]
    public void OracleDialect_DG010_ConnectByNewline_Detected()
    {
        var checker = new OracleDialectChecker();
        const string Sql = "SELECT * FROM t START WITH id = 1 CONNECT\nBY PRIOR id = parent_id";
        var violations = checker.CheckOracleSyntaxInNonOracleContext(Sql, false);
        violations.Should().Contain(
            v => v.RuleId == "DG010" && v.Properties != null && v.Properties.ContainsKey("keyword"),
            "CONNECT newline BY must be detected after \\s+ fix");
    }

    [Fact]
    public void OracleDialect_DG013_ExecDbo_FiresInOracleContext()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckSqlServerSyntaxLeak("EXEC dbo.GetUsers @Id=1", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG013");
    }

    [Fact]
    public void OracleDialect_DG013_SimpleExecNoDot_DoesNotFire()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckSqlServerSyntaxLeak("EXEC GET_CUSTOMER_BY_ID", isOracleContext: true);
        violations.Should().BeEmpty("bare EXEC without schema qualifier must not fire DG013");
    }

    // ── ProjectCSharpSqlSource SP detection (3.I.2) ──────────────────────────
    [Fact]
    public async Task SqlSource_CommandType_ConstructorPattern_IsDetected()
    {
        const string Code = """
            using System.Data;
            using System.Data.SqlClient;

            class Repository
            {
                void CallSp(SqlConnection conn)
                {
                    var cmd = new SqlCommand("GET_CUSTOMER_BY_ID", conn);
                    cmd.CommandType = CommandType.StoredProcedure;
                }
            }
            """;
        var contracts = await ExtractContractsAsync(Code);
        contracts.OfType<RawSqlDescriptor>().Should().ContainSingle(
            d => d.IsStoredProcedure == true && d.ProcedureName == "GET_CUSTOMER_BY_ID");
    }

    [Fact]
    public async Task SqlSource_DapperCommandTypeArg_IsDetected()
    {
        const string Code = """
            using System.Data;

            class Repository
            {
                void CallSp(System.Data.IDbConnection conn)
                {
                    Execute(conn, "MY_PROC", commandType: CommandType.StoredProcedure);
                }

                static void Execute(System.Data.IDbConnection cnn, string sql, object param = null, CommandType? commandType = null) { }
            }
            """;
        var contracts = await ExtractContractsAsync(Code);
        contracts.OfType<RawSqlDescriptor>().Should().ContainSingle(
            d => d.IsStoredProcedure == true && d.ProcedureName == "MY_PROC");
    }

    [Fact]
    public async Task SqlSource_PlainSelect_ExtractedWithIsStoredProcedureFalse()
    {
        const string Code = """
            class Repo
            {
                void Q(System.Data.IDbConnection conn)
                {
                    Execute(conn, "SELECT Id FROM Customers WHERE Status = @status");
                }

                static void Execute(System.Data.IDbConnection cnn, string sql) { }
            }
            """;
        var contracts = await ExtractContractsAsync(Code);
        contracts.OfType<RawSqlDescriptor>().Should().Contain(
            d => d.SqlText.Contains("SELECT") && !d.IsStoredProcedure);
    }

    [Fact]
    public async Task SqlSource_DapperPositionalCommandType_DetectsStoredProcedure()
    {
        const string Code = """
            using System.Data;

            class Repository
            {
                void CallSp(System.Data.IDbConnection conn)
                {
                    Execute(conn, "GET_USER_RECORDS", null, null, null, CommandType.StoredProcedure);
                }

                static void Execute(System.Data.IDbConnection cnn, string sql, object param = null, IDbTransaction transaction = null, int? commandTimeout = null, CommandType? commandType = null) { }
            }
            """;
        var contracts = await ExtractContractsAsync(Code);
        contracts.OfType<RawSqlDescriptor>().Should().ContainSingle(
            d => d.IsStoredProcedure == true && d.ProcedureName == "GET_USER_RECORDS");
    }

    [Fact]
    public async Task SqlSource_DbCommandInitializer_DetectsStoredProcedure()
    {
        const string Code = """
            using System.Data;
            using System.Data.SqlClient;

            class Repository
            {
                void CallSp(SqlConnection conn)
                {
                    var cmd = new SqlCommand
                    {
                        CommandText = "GET_CUSTOMER_BY_ID",
                        CommandType = CommandType.StoredProcedure
                    };
                }
            }
            """;
        var contracts = await ExtractContractsAsync(Code);
        contracts.OfType<RawSqlDescriptor>().Should().ContainSingle(
            d => d.IsStoredProcedure == true && d.ProcedureName == "GET_CUSTOMER_BY_ID");
    }

    [Fact]
    public async Task SqlSource_TargetTypedNewCommandInitializer_DetectsStoredProcedure()
    {
        const string Code = """
            using System.Data;
            using System.Data.SqlClient;

            class Repository
            {
                void CallSp(SqlConnection conn)
                {
                    SqlCommand cmd = new()
                    {
                        CommandText = "GET_CUSTOMER_TARGET_TYPED",
                        CommandType = CommandType.StoredProcedure
                    };
                }
            }
            """;
        var contracts = await ExtractContractsAsync(Code);
        contracts.OfType<RawSqlDescriptor>().Should().ContainSingle(
            d => d.IsStoredProcedure == true && d.ProcedureName == "GET_CUSTOMER_TARGET_TYPED");
    }

    [Fact]
    public async Task SqlSource_ArbitraryIdentifierEndingWithStoredProcedure_IsNotDetectedAsStoredProcedure()
    {
        const string Code = """
            class Repository
            {
                void Call(System.Data.IDbConnection conn)
                {
                    var myStoredProcedure = true;
                    Execute(conn, "SELECT Id FROM Users", myStoredProcedure);
                }

                static void Execute(System.Data.IDbConnection cnn, string sql, bool flag) { }
            }
            """;
        var contracts = await ExtractContractsAsync(Code);
        contracts.OfType<RawSqlDescriptor>().Should().NotContain(d => d.IsStoredProcedure);
    }

    private async Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(string csharpCode)
    {
        var file = Path.Combine(_tempDir, $"Test_{Guid.NewGuid():N}.cs");
        await File.WriteAllTextAsync(file, csharpCode);
        return await new ProjectCSharpSqlSource(_tempDir).ExtractContractsAsync();
    }
}
