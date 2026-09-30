# Additional permissions under GNU GPL version 3, section 7

> **Not reviewed by a lawyer.** This text follows the Free Software Foundation's template for
> GPL-incompatible libraries ([GPL FAQ, "GPLIncompatibleLibs"](https://www.gnu.org/licenses/gpl-faq.html#GPLIncompatibleLibs)).
> The DataGuard owner accepted the risk of publishing it without legal review. It is not legal advice.
> If you rely on it for a product or a redistribution, have your own counsel read it.

DataGuard releases from **v0.4.0** are licensed under **GPL-3.0-only** (see [`LICENSE`](../../LICENSE)) or,
separately, under a commercial licence (contact `<contact email placeholder>`). Releases up to and including
**v0.3.0** stay under the MIT licence permanently ([`MIT-v0.1.0-v0.3.0.txt`](MIT-v0.1.0-v0.3.0.txt)).

Copyright (c) 2026 Than Nguyen and DataGuard contributors.

## The additional permission

This is a permission granted by the copyright holders of DataGuard under section 7 of the GNU General
Public License, version 3. It applies to the code they own, in addition to the terms of `LICENSE`.

If you modify this Program, or any covered work, by linking or combining it with

1. **Oracle Data Provider for .NET, managed driver** (`Oracle.ManagedDataAccess.Core`, in the version
   distributed with DataGuard or a later version), containing parts covered by the terms of the
   *Oracle Free Distribution, Hosting, and Use Terms and Conditions*;
2. **Microsoft.Data.SqlClient.SNI.runtime** (the SQL Server networking library, in the version distributed
   with DataGuard or a later version), containing parts covered by the *Microsoft Software License Terms*
   for that library;
3. **Microsoft Visual Studio** (including its extensibility SDK and host process) or **Visual Studio Code**
   (including its extension API and host process), containing parts covered by the licence terms under which
   Microsoft supplies them; or
4. any other database driver, or any integrated development environment or host program that DataGuard
   interacts with at run time, that is supplied under licence terms incompatible with the GNU GPL, where you
   have not modified that driver or host program,

the licensors of this Program grant you additional permission to convey the resulting work.

## What this does not do

- It does not relicense, and gives you no rights in, the libraries and programs named above. They remain
  under their own terms, reproduced in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md). You must comply
  with those terms yourself, including Oracle's condition that a copy of its licence accompany every
  distribution and Microsoft's pass-through and indemnity requirements for the SNI library.
- It does not change the licence of DataGuard's own code: everything else in GPL-3.0 still applies, and
  your work that contains DataGuard code remains a covered work.
- Following the FSF template, no clause requires the source of the named libraries to be part of the
  Corresponding Source, because their owners do not permit distributing that source.
- `DataGuard.Contracts` is licensed under the MIT licence and does not need this permission.
- As section 7 provides, if you modify DataGuard you may remove this additional permission from your
  version of the code. It is your choice whether to keep it.
- No warranty of any kind, and no statement about whether a particular combination is lawful in your
  jurisdiction.
