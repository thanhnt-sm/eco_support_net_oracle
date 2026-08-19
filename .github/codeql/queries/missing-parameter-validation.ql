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
  mc.getTarget().getName().regexpMatch("(?i)^(query|execute).*") and
  not mc.getTarget().getAnAttribute().getType().hasName("ExpectedSpParameterAttribute")
select mc, "Data access call '" + mc.getTarget().getName() + "' has no expected-parameter validation."
