/**
 * @name Missing parameter validation
 * @description Finds stored-procedure calls without parameter validation (no [ExpectedSpParameter]).
 * @kind problem
 * @problem.severity warning
 * @id dataguard/missing-parameter-validation
 */
import csharp

from MethodCall mc
where
  // Only genuine SQL data-access entry points - NOT arbitrary Query*/Execute*
  // helpers (ExecuteReaderAsync, ExecuteScalarAsync, QueryData, ...).
  mc.getTarget().getName() in
    ["FromSqlRaw", "FromSqlInterpolated",
     "ExecuteSqlRaw", "ExecuteSqlInterpolated", "ExecuteSqlRawAsync", "ExecuteSqlInterpolatedAsync",
     "Query", "QueryAsync", "QueryFirst", "QueryFirstAsync",
     "QuerySingle", "QuerySingleAsync", "QueryMultiple", "QueryMultipleAsync",
     "Execute", "ExecuteAsync"] and
  not mc.getTarget().getAnAttribute().getType().hasName("ExpectedSpParameterAttribute")
select mc, "Data access call '" + mc.getTarget().getName() + "' has no expected-parameter validation."
