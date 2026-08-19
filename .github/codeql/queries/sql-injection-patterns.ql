/**
 * @name SQL injection patterns
 * @description Finds string concatenation/interpolation used to build SQL.
 * @kind problem
 * @problem.severity error
 * @id dataguard/sql-injection-pattern
 */
import csharp

from AddExpr ae
where
  exists(StringLiteral sl |
    sl.getValue().regexpMatch("(?i)select|insert|update|delete|exec") and
    (ae.getLeftOperand() = sl or ae.getRightOperand() = sl))
select ae, "SQL built via string concatenation is vulnerable to injection."
