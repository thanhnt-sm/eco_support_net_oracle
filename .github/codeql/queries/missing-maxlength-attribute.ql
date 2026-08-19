/**
 * @name Missing MaxLength attribute
 * @description Finds string properties without [MaxLength]/[StringLength] (NVARCHAR2(2000) fallback risk).
 * @kind problem
 * @problem.severity warning
 * @id dataguard/missing-maxlength
 */
import csharp

from Property p
where
  p.getType().getName() = "string" and
  not exists(Attribute a | a.getType().getName() in ["MaxLengthAttribute", "StringLengthAttribute"])
select p, "String property '" + p.getName() + "' has no MaxLength attribute; EF Core Oracle infers NVARCHAR2(2000)."
