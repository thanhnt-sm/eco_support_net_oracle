/**
 * @name Hardcoded connection strings
 * @description Finds string literals in production source that embed a credential
 *              (password / pwd / passwd / user id / uid = value), alone or inside a
 *              connection string. Masked or templated values (***, {0}, ${VAR}, <value>,
 *              %VAR%) and loopback dev targets (localhost, (localdb)) are ignored. Files
 *              under tests/ are out of scope: the credential-manager suite necessarily
 *              carries fake credentials as fixtures; TruffleHog still scans them.
 * @kind problem
 * @problem.severity error
 * @id dataguard/hardcoded-connection-string
 * @tags security
 *       external/cwe/cwe-798
 */
import csharp

// CodeQL's regexpMatch is anchored (whole string); every pattern below is therefore
// wrapped in (?is).* ... .* so a credential pair is found anywhere in the literal.
bindingset[value]
predicate hasCredentialPair(string value) {
  value.regexpMatch("(?is).*\\b(password|pwd|passwd|user id|uid)\\s*=\\s*[^;\\s].*")
}

bindingset[value]
predicate credentialIsPlaceholder(string value) {
  value.regexpMatch("(?is).*\\b(password|pwd|passwd|user id|uid)\\s*=\\s*(\\*+|\\{[^}]*\\}|\\$\\{[^}]*\\}|<[^>]*>|%[^%]*%)\\s*(;.*)?")
}

bindingset[value]
predicate loopbackTarget(string value) { value.regexpMatch("(?is).*(localhost|\\(localdb\\)).*") }

predicate inTestCode(StringLiteral sl) { sl.getFile().getRelativePath().matches("tests/%") }

from StringLiteral sl, string value
where
  value = sl.getValue() and
  hasCredentialPair(value) and
  not credentialIsPlaceholder(value) and
  not loopbackTarget(value) and
  not inTestCode(sl)
select sl, "Hardcoded connection string with credentials detected."
