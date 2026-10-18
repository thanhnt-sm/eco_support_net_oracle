# Credential boundary reconciliation — 2026-10-16

## Publication reconciliation

The credential patch now applies cleanly to main
`edf7314b6779a002f321d27305f9d2959d3281cb` (including PR66). The user has
subsequently authorized publication of the four separate patches. The parent
agent handles commits and pushes; PR creation and merge remain manual. This
review performs no remote actions or commits. Marketplace publication remains
paused; no live credentials, endpoints or security-setting changes are included.

Focused synthetic revalidation evidence for this base belongs under
`/workspace/.tmp/risk-maintenance/publication/`. The first attempt could not start:
the preserved activation script references a missing `/tmp/dataguard-tools`
toolchain (`credential-review.log`, exit 127). After the parent restored and
checksum-verified the official pinned toolchain, focused synthetic verification
on this publication base passed: 48 tests, zero failures or skips
(`credential-review-restored.log` and `credential-review/credential-review.trx`).
Temporary and configuration directories were isolated. Independent review found
no security blocker; production source and endpoint tests match the reviewed
reconciliation. The required full `DataGuard.sln` Release build also passed with
zero errors and one MSB3026 file-copy retry warning for the Visual Studio assembly
(`credential-full-build.log`). The successful build was not repeated merely to
remove the transient warning. The parent's unchanged
push hook handles the remaining full gates. The completed full-gate results
below belong to the original reconciliation base.

Tiếng Việt: Bản vá áp dụng sạch trên main `edf7314` đã gồm PR66. Người dùng đã
cho phép công bố bốn bản vá riêng; agent cha phụ trách commit/push, còn tạo PR và
merge làm thủ công. Reviewer không thao tác remote hay commit. Marketplace vẫn
tạm dừng. Lượt đầu thiếu toolchain trong `/tmp`; sau khi agent cha khôi phục và
xác minh checksum, 48 kiểm thử credential đạt, không lỗi/bỏ qua trên base mới.
Không phát hiện blocker bảo mật. Build Release toàn `DataGuard.sln` đạt, không
error, có một warning MSB3026 khi thử lại thao tác copy assembly Visual Studio.
Push hook nguyên bản của agent cha chạy các gate còn lại; số liệu
full-gate bên dưới vẫn là bằng chứng của đợt đối chiếu ban đầu.

## Original scope and baseline

Initial local-only reconciliation used main `5927edd852a71177bf1b068da66972153e3a9af3` (PR64 and PR65
merged). Verified SDK9.0.319 and Node22.23.3 before edits. Prior worktrees, patches
and raw evidence remained untouched. At that stage, no commit, push, PR, upload,
API retry, credential or security-setting change was authorized. Marketplace pause remains.

Credential patch applies cleanly; both production files match their original
patch-base blobs. SQL Server describe and Unknown patches pass apply checks but
remain unapplied and separate; their runtime validation was not rerun here.

## P1 acceptance and source mapping

| Source | Tests | Acceptance |
| --- | --- | --- |
| `src/DataGuard.Core/Security/SecretStores/ISecretStore.cs` | `SecretStoreEndpointTests.cs` | Trusted HTTPS root VAULT_ADDR must match configured host/port before reading VAULT_TOKEN; Azure single vault-label root; canonical successes and exact request path; missing token, 404 and cancellation |
| `src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs` | `ZeroTrustCredentialProviderTests.cs` | Preserve fallback/order, safe visible warnings, CLI logger excludes synthetic exception secret |
| `docs/guides/configuration.md` and `.vi.md` | Docs gate and independent review | Explain mandatory VAULT_ADDR, endpoint restrictions and caller-owned HttpClient redirect responsibility |

Fixtures use only fake HTTP handlers and injected environment accessors, including
the IMDS-shaped URI. No real token, endpoint or database is accessed. Built-in
redirect disabling is structurally verified, not exercised by a runtime redirect
regression. Hosts injecting HttpClient must disable redirects themselves. Endpoint
checks do not establish hostname ownership.

Compatibility tightening is intentional: previously VaultAddress plus VAULT_TOKEN
sufficed; trusted VAULT_ADDR is now mandatory. Root addresses reject userinfo,
path, query and fragment. Default443 and matching custom Vault HTTPS ports remain
supported. Public constructors and lookup order are unchanged. No dependencies,
workflows, gates or security settings are modified.

## Evidence

Evidence: `/workspace/.tmp/risk-maintenance/credential-reconcile/`.
Tests-only on original main `5927edd`:18 failed,20 passed (38total), retained in `red.log`.
The original fix was then applied. Independent reviewer found no blocker or
reconciliation conflict, recommending exact-URI and compatibility tests. Added
synthetic cases address missing token,404 and cancellation. An initial added
cancellation test incorrectly assumed HttpClient would not invoke its handler;
that failure is retained in `green.log`. The corrected fake observes cancellation,
and the assertion checks propagation to that handler rather than this assumption.
Corrected focused run:42 passed,0 failed,0 skipped (`green-corrected.log`).
Unchanged pre-push and pre-commit scripts passed (invoked locally, no Git write).
.NET:2240 passed,5 Windows-only skips. Measured production line coverage:
17073/20995 (81.32%), preserved raw and normalized reports in `results/` and
`normalized/`. This is measured suite scope, not whole-product coverage.
Full DataGuard.sln Release build:0 warnings,0 errors. Build/analyzers, actionlint,
workflow policy, dependency audit, formatting, topology and docs checks passed.
Python:142 passed,1 existing skip (143total). Node:117 passed,64.73% lines and
86.00% branches on current main's expanded PR64 suite; this is not an effect of
the credential patch. No assertions, hooks or thresholds were weakened.
Final patch hashes are recorded in the local handoff.

`preserved-patches.json` contains unchanged original hashes and SQL/Unknown apply
checks. No Windows/editor-host, hosted CI, live secret-store or production DB
validation is claimed. Default SKIP_ACT=1 skips act and verified-secret scanning;
stock hook wording does not establish separate verification.

## Tiếng Việt

Đối chiếu riêng bản vá credential trên main5927edd, không có xung đột. Giữ nguyên
bản vá và bằng chứng cũ; SQL Server và Unknown chỉ kiểm tra áp dụng, chưa chạy
lại kiểm thử trong đợt ban đầu. Khi đó không commit, push, PR, tải lên, thử API hoặc thay đổi
credential/bảo mật. Trước sửa:18 thất bại,20 đạt.

P1: cấu hình repo không được tự quyết định nơi gửi VAULT_TOKEN; phải khớp origin
HTTPS tin cậy từ VAULT_ADDR trước khi đọc token. Kiểm thử dùng handler và biến môi
trường giả. Bổ sung kiểm tra URI, thiếu token,404 và truyền cancellation. Redirect
chỉ xác minh qua cấu trúc mã. Tài liệu EN/VI nêu rõ thay đổi tương thích và trách
nhiệm host khi tự truyền HttpClient. Kết quả cuối và hash được ghi trong bàn giao.

Kết quả:42 kiểm thử credential đạt; .NET2240 đạt/5 bỏ qua chỉ Windows; Python142 đạt/1 bỏ qua; Node117 đạt. Build toàn solution không warning/error; cả hai hook cục bộ đạt. Độ bao phủ dòng .NET17073/20995 chỉ là phạm vi bộ kiểm thử đã đo.
