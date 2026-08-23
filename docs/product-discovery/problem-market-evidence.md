# Problem-market evidence

Ngày truy cập: 2026-08-23. Phạm vi chỉ gồm vấn đề có thể kiểm chứng bằng assessment read-only; không bao gồm auto-remediation hay claim compliance không có evidence.

| Persona / legacy target | Pain point và impact evidence | Existing solution | Evidence-bounded gap | Sources |
|---|---|---|---|---|
| Backend .NET maintainer; .NET Framework / SDK-style solution có EF và stored procedure | Đồng bộ application/database là vấn đề được Redgate khảo sát; report 2024 có 3.849 người trả lời và nêu application/database synchronization 28%. | SqlPackage tạo deploy/drift report từ DACPAC và database schema. | DataGuard đã có seam CLI/rule cho Entity↔SP/SQL; assessment chỉ nên báo drift/compatibility evidence, không triển khai database. | [Redgate 2024](https://www.red-gate.com/solutions/state-of-database-landscape/2024/); [SqlPackage publish parameters](https://learn.microsoft.com/sql/tools/sqlpackage/sqlpackage-publish) |
| Full-stack maintainer; API .NET legacy và client phụ thuộc API contract | Thay đổi HTTP/API contract gây ảnh hưởng client; OpenAPI là format mô tả HTTP API để tool/client tiêu thụ. | OpenAPI descriptions và existing API tooling. | Chỉ report published contract/config evidence; không suy đoán compatibility hay rewrite client. | [Microsoft ASP.NET Core OpenAPI](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi) |
| Frontend/CI maintainer; solution có SDK pinning, package lock và mixed project styles | Build reproducibility và package/tool mismatch làm CI drift; restore lock files là built-in deterministic dependency mechanism. | NuGet lock file, SDK pinning và CI build matrix. | Report exact lock/SDK/CI-file evidence and missing metadata; no file edits. | [NuGet lock files](https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies) |
| Enterprise/banking maintainer; legacy solution with secrets/config and evidence obligations | NIST SSDF yêu cầu tích hợp secure software development practices vào SDLC và tạo objective evidence. | Secure SDLC controls, secret managers, code scanners. | Deterministic config key+value detection must redact values and never turn a local finding into a compliance certification. | [NIST SP 800-218 SSDF](https://csrc.nist.gov/pubs/sp/800/218/final) |

## Product boundary

Mọi selected opportunity phải map đồng thời tới source seam trong `code-capabilities.md` và source evidence ở đây. Không có network lookup mặc định; advisory/vulnerability data là opt-in và partial failure phải hiển thị error.