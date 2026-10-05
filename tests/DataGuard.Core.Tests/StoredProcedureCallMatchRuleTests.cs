using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.StoredProcedures;
using DataGuard.Core.Rules.TypeCompatibility;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Phase 3.1: DG101/DG002/DG003 resolve stored-procedure calls against the catalog (red-team C1/H6).
/// </summary>
public class StoredProcedureCallMatchRuleTests
{
    private static readonly ParameterDirection In = ParameterDirection.Input;
    private static readonly ParameterDirection Out = ParameterDirection.Output;
    private static readonly ParameterDirection InOut = ParameterDirection.InputOutput;

    [Fact]
    public async Task ExactMatch_SqlServer_NoFindings()
    {
        var catalog = SqlServerOrders();
        var call = Call("EXEC dbo.usp_GetOrders @CustomerId = @cid, @Status = @status", Arg("@cid", "int"), Arg("@status", "string"));

        var violations = await RunAllAsync(SqlServerRules(), call, catalog);

        violations.Should().BeEmpty();
        Resolve(call, "sqlserver", catalog).Status.Should().Be(StoredProcedureResolutionStatus.Resolved);
    }

    [Fact]
    public void OverloadByArity_Oracle_PicksTheOverloadThatAcceptsTheArguments()
    {
        var one = Sp("HR", "P", "PKG", "oracle:HR.PKG.P#1", P("P_A", "NUMBER", 1));
        var two = Sp("HR", "P", "PKG", "oracle:HR.PKG.P#2", P("P_A", "NUMBER", 1), P("P_B", "VARCHAR2", 2));

        Resolve(Call("BEGIN PKG.P(:a, :b); END;"), "oracle", one, two).Procedure!.Id.Should().Be("oracle:HR.PKG.P#2");
        Resolve(Call("BEGIN PKG.P(:a); END;"), "oracle", one, two).Procedure!.Id.Should().Be("oracle:HR.PKG.P#1");
    }

    [Fact]
    public void ZeroArgumentOverload_Oracle_Resolves()
    {
        var none = Sp("HR", "REFRESH", "PKG", "oracle:HR.PKG.REFRESH#1");
        var one = Sp("HR", "REFRESH", "PKG", "oracle:HR.PKG.REFRESH#2", P("P_ID", "NUMBER", 1));

        Resolve(Call("BEGIN PKG.REFRESH; END;"), "oracle", none, one).Procedure!.Id.Should().Be("oracle:HR.PKG.REFRESH#1");
    }

    [Fact]
    public void NamedArguments_BindByName_InAnyOrder()
    {
        var sp = Sp("HR", "P", "PKG", "p", P("P_A", "NUMBER", 1), P("P_B", "VARCHAR2", 2));

        var resolution = Resolve(Call("BEGIN PKG.P(p_b => :b, p_a => :a); END;"), "oracle", sp);

        resolution.Status.Should().Be(StoredProcedureResolutionStatus.Resolved);
        resolution.Bindings.Select(b => (b.Argument.Value, b.Parameter.Name))
            .Should().BeEquivalentTo(new[] { (":b", "P_B"), (":a", "P_A") });
    }

    [Fact]
    public async Task DefaultedParameter_MayBeOmitted()
    {
        var sp = Sp("dbo", "usp_Search", "", "s", P("@Term", "nvarchar", 1), P("@Top", "int", 2, hasDefault: true));

        var violations = await RunAsync(new ParameterCountRule("sqlserver"), Call("EXEC dbo.usp_Search @Term = @t"), sp);

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingRequiredParameter_ReportsDg101Warning()
    {
        var violations = await RunAsync(new ParameterCountRule("sqlserver"), Call("EXEC dbo.usp_GetOrders @CustomerId = @cid"), SqlServerOrders());

        var violation = violations.Should().ContainSingle().Which;
        violation.RuleId.Should().Be("DG101");
        violation.Severity.Should().Be(DiagnosticSeverity.Warning);
        violation.Message.Should().Contain("missing required parameter(s) @Status");
        violation.Properties!["procedure"].Should().Be("usp_GetOrders");
        violation.Properties!["schema"].Should().Be("dbo");
        violation.Properties!["parameter"].Should().Be("@Status");
    }

    [Fact]
    public async Task ExtraUnknownArgument_ReportsDg101()
    {
        var call = Call("EXEC dbo.usp_GetOrders @CustomerId = @cid, @Status = @s, @Region = @r");

        var violations = await RunAsync(new ParameterCountRule("sqlserver"), call, SqlServerOrders());

        violations.Should().ContainSingle().Which.Message.Should().Contain("unknown argument(s) Region");
    }

    [Fact]
    public async Task UnknownProcedure_ReportsNearestCatalogName()
    {
        var violations = await RunAsync(new ParameterCountRule("sqlserver"), Call("EXEC dbo.usp_GetOrder @CustomerId = 1"), SqlServerOrders());

        var violation = violations.Should().ContainSingle().Which;
        violation.Message.Should().Be("Stored procedure 'dbo.usp_GetOrder' not found in catalog (nearest: dbo.usp_GetOrders)");
        violation.Properties!["nearest"].Should().Be("dbo.usp_GetOrders");
    }

    [Fact]
    public void PackageQualifiedOracleCall_ResolvesSchemaAndPackage()
    {
        var target = Sp("HR", "CREATE_ORDER", "ORDERS_PKG", "target", P("P_CUSTOMER_ID", "NUMBER", 1), P("P_ORDER_ID", "NUMBER", 2, Out));
        var other = Sp("HR", "CREATE_ORDER", "LEGACY_PKG", "other", P("P_CUSTOMER_ID", "NUMBER", 1));

        var resolution = Resolve(Call("BEGIN HR.ORDERS_PKG.CREATE_ORDER(:cid, :oid); END;"), "oracle", target, other);

        resolution.Status.Should().Be(StoredProcedureResolutionStatus.Resolved);
        resolution.Procedure!.Id.Should().Be("target");
    }

    [Fact]
    public void CaseFolding_PostgreSql_FoldsUnquotedToLower()
    {
        var sp = Sp("public", "get_orders", "", "pg", P("p_customer", "integer", 1));

        Resolve(Call("CALL Get_Orders($1)"), "postgresql", sp).Status.Should().Be(StoredProcedureResolutionStatus.Resolved);
        Resolve(Call("CALL \"Get_Orders\"($1)"), "postgresql", sp).Status.Should().Be(StoredProcedureResolutionStatus.NoCandidate);
    }

    [Fact]
    public void CaseFolding_Oracle_FoldsUnquotedToUpper()
    {
        var sp = Sp("HR", "GET_ORDERS", "", "ora", P("P_CUSTOMER", "NUMBER", 1));

        Resolve(Call("BEGIN get_orders(:c); END;"), "oracle", sp).Status.Should().Be(StoredProcedureResolutionStatus.Resolved);
        Resolve(Call("BEGIN \"get_orders\"(:c); END;"), "oracle", sp).Status.Should().Be(StoredProcedureResolutionStatus.NoCandidate);
    }

    [Fact]
    public void CaseFolding_SqlServer_IsCaseInsensitive()
    {
        Resolve(Call("EXEC DBO.USP_GETORDERS @customerid = 1, @STATUS = 'x'"), "sqlserver", SqlServerOrders())
            .Status.Should().Be(StoredProcedureResolutionStatus.Resolved);
    }

    [Fact]
    public void UnqualifiedCall_UsesDefaultSchema()
    {
        var dbo = Sp("dbo", "usp_Report", "", "dbo", P("@Year", "int", 1));
        var sales = Sp("sales", "usp_Report", "", "sales", P("@Year", "int", 1));
        var options = new StoredProcedureMatchOptions("sqlserver", DefaultSchema: "sales");

        var resolution = StoredProcedureCallResolver.Resolve(Call("EXEC usp_Report 2026"), new ContractDescriptor[] { dbo, sales }, options);

        resolution.Procedure!.Id.Should().Be("sales");
    }

    [Fact]
    public async Task UnqualifiedCall_AmbiguousAcrossSchemas_NoFindingAndNote()
    {
        var dbo = Sp("dbo", "usp_Report", "", "dbo", P("@Year", "int", 1));
        var sales = Sp("sales", "usp_Report", "", "sales", P("@Year", "int", 1), P("@Region", "nvarchar", 2));
        var call = Call("EXEC usp_Report @Year = 2026, @Bogus = 1", Arg("@x", "string"));

        var violations = await RunAllAsync(SqlServerRules(), call, dbo, sales);
        var resolution = Resolve(call, "sqlserver", dbo, sales);

        violations.Should().BeEmpty();
        resolution.Status.Should().Be(StoredProcedureResolutionStatus.Ambiguous);
        resolution.Properties["candidates"].Should().Be("dbo.usp_Report, sales.usp_Report");
        resolution.Properties["note"].Should().BeOfType<string>().Which.Should().Contain("ambiguous");
    }

    [Fact]
    public async Task UnknownClrOrDbType_DoesNotReportDg002()
    {
        var sp = Sp("dbo", "usp_Locate", "", "geo", P("@Where", "geography", 1), P("@Amount", "decimal", 2));
        var call = Call("EXEC dbo.usp_Locate @Where = @w, @Amount = @a", Arg("@w", "string"), Arg("@a", "MyCompany.Money"));

        var violations = await RunAsync(new ParameterTypeMatchRule("sqlserver", SqlServerTypeCompatibility.Instance), call, sp);

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task IncompatibleBoundType_ReportsDg002WithProperties()
    {
        var call = Call("EXEC dbo.usp_GetOrders @CustomerId = @cid, @Status = @s", Arg("@cid", "System.Guid"), Arg("@s", "string?"));

        var violations = await RunAsync(new ParameterTypeMatchRule("sqlserver", SqlServerTypeCompatibility.Instance), call, SqlServerOrders());

        var violation = violations.Should().ContainSingle().Which;
        violation.RuleId.Should().Be("DG002");
        violation.Severity.Should().Be(DiagnosticSeverity.Warning);
        violation.Properties!["parameter"].Should().Be("@CustomerId");
        violation.Properties!["clrType"].Should().Be("System.Guid");
        violation.Properties!["dbType"].Should().Be("int");
        violation.Properties!["procedure"].Should().Be("usp_GetOrders");
    }

    [Fact]
    public async Task OutputParameterPassedAsInput_ReportsDg003()
    {
        var violations = await RunAsync(new ParameterDirectionRule("sqlserver"), Call("EXEC dbo.usp_CreateOrder @CustomerId = 1, @OrderId = @id"), SqlServerCreateOrder());

        var violation = violations.Should().ContainSingle().Which;
        violation.RuleId.Should().Be("DG003");
        violation.Message.Should().Contain("'@OrderId'").And.Contain("out/ref required");
        violation.Properties!["parameter"].Should().Be("@OrderId");
    }

    [Fact]
    public async Task InputParameterPassedAsOutput_ReportsDg003()
    {
        var violations = await RunAsync(
            new ParameterDirectionRule("sqlserver"),
            Call("EXEC dbo.usp_CreateOrder @CustomerId = @c OUTPUT, @OrderId = @id OUTPUT"),
            SqlServerCreateOrder());

        violations.Should().ContainSingle().Which.Message.Should().Contain("'@CustomerId'").And.Contain("is Input but call site passes it as Output");
    }

    [Fact]
    public async Task OracleOutBindFromExtractor_DirectionChecked()
    {
        var sp = Sp("HR", "CREATE_ORDER", "ORDERS_PKG", "o", P("P_CUSTOMER_ID", "NUMBER", 1), P("P_ORDER_ID", "NUMBER", 2, Out));
        var ok = Call("BEGIN ORDERS_PKG.CREATE_ORDER(:cid, :oid); END;", Arg(":cid", "int", In), Arg(":oid", "long", Out));
        var bad = Call("BEGIN ORDERS_PKG.CREATE_ORDER(:cid, :oid); END;", Arg(":cid", "int", In), Arg(":oid", "long", In));

        (await RunAsync(new ParameterDirectionRule("oracle", OracleTypeCompatibility.Instance), ok, sp)).Should().BeEmpty();
        (await RunAsync(new ParameterDirectionRule("oracle", OracleTypeCompatibility.Instance), bad, sp)).Should().ContainSingle(v => v.RuleId == "DG003");
    }

    [Fact]
    public async Task LegacyHeuristic_OnlyWithoutCatalog()
    {
        var call = Call("EXEC dbo.usp_Ping");
        var ping = Sp("dbo", "usp_Ping", "", "ping");

        (await RunAsync(new ParameterCountRule(), call)).Should().ContainSingle(v => v.RuleId == "DG101" && v.Severity == DiagnosticSeverity.Error);
        (await RunAsync(new ParameterCountRule(), call, ping)).Should().BeEmpty();
    }

    [Fact]
    public async Task StrictProcedureContracts_ReportsErrors()
    {
        var call = Call("EXEC dbo.usp_GetOrders @CustomerId = @cid");

        var lenient = await RunAsync(new ParameterCountRule("sqlserver"), call, SqlServerOrders());
        var strict = await RunAsync(new ParameterCountRule("sqlserver", strictProcedureContracts: true), call, SqlServerOrders());

        lenient.Should().ContainSingle().Which.Severity.Should().Be(DiagnosticSeverity.Warning);
        strict.Should().ContainSingle().Which.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task NameOnlyCallWithoutExtractedArguments_IsNotReportedAsMissing()
    {
        var call = Call("EXEC usp_GetOrders") with { IsStoredProcedure = true, ProcedureName = "usp_GetOrders" };

        (await RunAsync(new ParameterCountRule("sqlserver"), call, SqlServerOrders())).Should().BeEmpty();
        Resolve(call, "sqlserver", SqlServerOrders()).Status.Should().Be(StoredProcedureResolutionStatus.Resolved);
    }

    [Fact]
    public async Task NameOnlyCallWithExtractedDapperParameters_IsCheckedByName()
    {
        // Dapper: conn.Execute("dbo.usp_CreateOrder", new { CustomerId = id, OrderId = 0 }, commandType: StoredProcedure)
        var call = Call("EXEC dbo.usp_CreateOrder", Arg("CustomerId", "string", In), Arg("OrderId", "int", In)) with
        {
            IsStoredProcedure = true,
            ProcedureName = "usp_CreateOrder",
            ProcedureSchema = "dbo",
        };

        var violations = await RunAllAsync(SqlServerRules(), call, SqlServerCreateOrder());

        violations.Select(v => v.RuleId).Should().BeEquivalentTo(new[] { "DG002", "DG003" });
    }

    [Fact]
    public async Task ExecuteKeywordAndReturnValueParameter_AreHandled()
    {
        var call = Call("EXECUTE dbo.usp_GetOrders", Arg("@RETURN_VALUE", "int", ParameterDirection.ReturnValue), Arg("@CustomerId", "int", In)) with
        {
            IsStoredProcedure = true,
        };

        var violation = (await RunAsync(new ParameterCountRule("sqlserver"), call, SqlServerOrders())).Should().ContainSingle().Which;
        violation.Message.Should().Contain("missing required parameter(s) @Status").And.NotContain("RETURN_VALUE");
    }

    [Fact]
    public async Task SystemOrUncataloguedTargets_AreOutOfScope()
    {
        var catalog = SqlServerOrders();

        (await RunAsync(new ParameterCountRule("sqlserver"), Call("EXEC sp_executesql N'SELECT 1'"), catalog)).Should().BeEmpty();
        (await RunAsync(new ParameterCountRule("sqlserver"), Call("EXEC audit.usp_Log @Message = 'x'"), catalog)).Should().BeEmpty();
        (await RunAsync(new ParameterCountRule("sqlserver"), Call("EXEC OtherDb.dbo.usp_Log 1"), catalog)).Should().BeEmpty();
        (await RunAsync(new ParameterCountRule("oracle"), Call("BEGIN DBMS_OUTPUT.PUT_LINE('x'); END;"), Sp("HR", "P", "", "p"))).Should().BeEmpty();
    }

    [Fact]
    public void Resolution_IsSharedAcrossRules()
    {
        var call = Call("EXEC dbo.usp_GetOrders @CustomerId = 1, @Status = 'x'");
        var all = new List<ContractDescriptor> { call };
        all.AddRange(SqlServerOrders());
        var options = new StoredProcedureMatchOptions("sqlserver");

        StoredProcedureCallResolver.Resolve(call, all, options)
            .Should().BeSameAs(StoredProcedureCallResolver.Resolve(call, all, options));
    }

    private static StoredProcedureDescriptor[] SqlServerOrders() => new[]
    {
        Sp("dbo", "usp_GetOrders", "", "dbo.usp_GetOrders", P("@CustomerId", "int", 1), P("@Status", "nvarchar", 2)),
    };

    private static StoredProcedureDescriptor[] SqlServerCreateOrder() => new[]
    {
        Sp("dbo", "usp_CreateOrder", "", "dbo.usp_CreateOrder", P("@CustomerId", "int", 1), P("@OrderId", "int", 2, InOut)),
    };

    private static IContractRule[] SqlServerRules() => new IContractRule[]
    {
        new ParameterCountRule("sqlserver", SqlServerTypeCompatibility.Instance),
        new ParameterTypeMatchRule("sqlserver", SqlServerTypeCompatibility.Instance),
        new ParameterDirectionRule("sqlserver", SqlServerTypeCompatibility.Instance),
    };

    private static StoredProcedureResolution Resolve(RawSqlDescriptor call, string provider, params StoredProcedureDescriptor[] catalog)
    {
        var table = provider switch
        {
            "oracle" => (ITypeCompatibility)OracleTypeCompatibility.Instance,
            "postgresql" => PostgreSqlTypeCompatibility.Instance,
            _ => SqlServerTypeCompatibility.Instance,
        };
        var all = new List<ContractDescriptor> { call };
        all.AddRange(catalog);
        return StoredProcedureCallResolver.Resolve(call, all, new StoredProcedureMatchOptions(provider, TypeCompatibility: table));
    }

    private static async Task<List<ContractViolation>> RunAsync(IContractRule rule, RawSqlDescriptor call, params StoredProcedureDescriptor[] catalog)
        => await RunAllAsync(new[] { rule }, call, catalog);

    private static async Task<List<ContractViolation>> RunAllAsync(IEnumerable<IContractRule> rules, RawSqlDescriptor call, params StoredProcedureDescriptor[] catalog)
    {
        var all = new List<ContractDescriptor> { call };
        all.AddRange(catalog);
        var violations = new List<ContractViolation>();
        foreach (var rule in rules)
        {
            foreach (var contract in all)
            {
                violations.AddRange(await rule.ValidateAsync(contract, all, CancellationToken.None));
            }
        }

        return violations;
    }

    private static RawSqlDescriptor Call(string sql, params ParameterDescriptor[] arguments)
        => new("call:" + sql.GetHashCode(), sql, arguments, new List<ColumnDescriptor>());

    private static ParameterDescriptor Arg(string name, string clrType, ParameterDirection? direction = null)
        => new(name, "unknown", ParameterDirection.Input, null, null, null, true, 0) { ClrType = clrType, CallSiteDirection = direction };

    private static ParameterDescriptor P(string name, string dataType, int ordinal, ParameterDirection? direction = null, bool hasDefault = false)
        => new(name, dataType, direction ?? ParameterDirection.Input, null, null, null, true, ordinal) { HasDefault = hasDefault };

    private static StoredProcedureDescriptor Sp(string schema, string name, string package, string id, params ParameterDescriptor[] parameters)
        => new(id, name, schema, package, parameters, new List<ColumnDescriptor>(), false);
}
