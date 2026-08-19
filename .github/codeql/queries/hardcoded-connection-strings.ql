/**
 * @name Hardcoded connection strings
 * @description Finds hardcoded connection strings (password/secret in source).
 * @kind problem
 * @problem.severity error
 * @id dataguard/hardcoded-connection-string
 */
import csharp

from StringLiteral sl
where
  sl.getValue().regexpMatch("(?i)(server|data source|initial catalog|password|pwd|user id)=[^;]+") and
  not sl.getValue().regexpMatch("(?i)localhost|(localdb)")
select sl, "Hardcoded connection string with credentials detected."
