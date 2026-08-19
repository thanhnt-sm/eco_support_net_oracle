/**
 * @name Unvalidated SQL calls
 * @description Finds FromSqlRaw/ExecuteSqlRaw calls not marked with [SkipContractCheck].
 * @kind problem
 * @problem.severity warning
 * @id dataguard/unvalidated-sql-call
 */
import csharp

from MethodCall mc
where
  mc.getTarget().getName() in ["FromSqlRaw", "FromSqlInterpolated", "ExecuteSqlRaw", "ExecuteSqlInterpolated", "ExecuteSqlRawAsync", "ExecuteSqlInterpolatedAsync"] and
  not mc.getTarget().getAnAttribute().getType().hasName("SkipContractCheckAttribute")
select mc, "SQL call '" + mc.getTarget().getName() + "' is not validated against the database schema."
