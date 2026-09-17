/**
 * @name Missing parameter validation
 * @description Finds stored-procedure calls without parameter validation (no [ExpectedSpParameter]).
 * @kind problem
 * @problem.severity warning
 * @id dataguard/missing-parameter-validation
 */
import csharp

from MethodCall mc, Method m
where
  m = mc.getTarget() and
  // Only genuine SQL data-access entry points - NOT arbitrary Query*/Execute*
  // helpers (ExecuteReaderAsync, ExecuteScalarAsync, QueryData, ...).
  m.getName() in
    ["FromSqlRaw", "FromSqlInterpolated",
     "ExecuteSqlRaw", "ExecuteSqlInterpolated", "ExecuteSqlRawAsync", "ExecuteSqlInterpolatedAsync",
     "Query", "QueryAsync", "QueryFirst", "QueryFirstAsync",
     "QuerySingle", "QuerySingleAsync", "QueryMultiple", "QueryMultipleAsync",
     "Execute", "ExecuteAsync"] and
  // The callee must be declared by a genuine data-access API (Dapper or
  // EF Core). Name-only matching used to flag domain methods that merely
  // share a name, e.g. IRemoteAdvisoryClient.QueryAsync,
  // IBusinessOperationObserver.ExecuteAsync or MSBuild Task.Execute.
  // Declaring types verified against Dapper (SqlMapper) and EF Core 9
  // (RelationalDatabaseFacadeExtensions, RelationalQueryableExtensions).
  m.getDeclaringType().hasName(
    ["SqlMapper",
     "RelationalDatabaseFacadeExtensions", "RelationalQueryableExtensions",
     "EntityFrameworkQueryableExtensions", "DatabaseFacade", "DbSet", "DbContext"]) and
  not m.getAnAttribute().getType().hasName("ExpectedSpParameterAttribute")
select mc, "Data access call '" + m.getName() + "' has no expected-parameter validation."
