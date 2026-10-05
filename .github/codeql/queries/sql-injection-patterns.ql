/**
 * @name SQL injection patterns
 * @description Finds SQL text assembled from a non-constant value by string concatenation
 *              or interpolation: a literal fragment that starts with a SQL verb (SELECT,
 *              INSERT, UPDATE, DELETE, MERGE, EXEC) and carries a structural keyword
 *              (FROM, INTO, WHERE, VALUES, JOIN, SET) is combined with a non-literal
 *              operand or an interpolation hole. Literal-only concatenation is constant and
 *              ignored; files under tests/ are out of scope (hostile payloads are built there
 *              on purpose). A reviewed site is acknowledged with the standard CodeQL
 *              suppression comment on the same line or the line above:
 *              // codeql[dataguard/sql-injection-pattern]: <why it is safe>
 * @kind problem
 * @problem.severity error
 * @id dataguard/sql-injection-pattern
 * @tags security
 *       external/cwe/cwe-089
 */
import csharp

// regexpMatch is anchored, so the structural-keyword pattern is wrapped in (?is).* ... .*
bindingset[value]
predicate sqlFragment(string value) {
  value.regexpMatch("(?is)\\s*\\(?\\s*(select|insert|update|delete|merge|exec|execute)\\s.*") and
  value.regexpMatch("(?is).*\\b(from|into|where|values|join|set)\\b.*")
}

predicate inTestCode(Expr e) { e.getFile().getRelativePath().matches("tests/%") }

// A suppression comment block ends on the alert's line (trailing comment) or on the line just above it;
// the marker may sit on any line of that block, so a multi-line justification is allowed.
predicate suppressed(Expr e) {
  exists(CommentBlock b |
    b.getLocation().getFile() = e.getFile() and
    b.getALine().matches("%codeql[dataguard/sql-injection-pattern]%") and
    b.getLocation().getEndLine() in [e.getLocation().getStartLine() - 1, e.getLocation().getStartLine()]
  )
}

predicate concatenatesSql(AddExpr ae) {
  exists(StringLiteral sl |
    sqlFragment(sl.getValue()) and
    (ae.getLeftOperand() = sl or ae.getRightOperand() = sl)
  ) and
  not (ae.getLeftOperand() instanceof StringLiteral and ae.getRightOperand() instanceof StringLiteral)
}

predicate interpolatesSql(InterpolatedStringExpr ise) {
  sqlFragment(ise.getAText().getValue()) and exists(ise.getAnInsert())
}

from Expr e
where
  (concatenatesSql(e) or interpolatesSql(e)) and
  not inTestCode(e) and
  not suppressed(e)
select e, "SQL built via string concatenation or interpolation is vulnerable to injection."
