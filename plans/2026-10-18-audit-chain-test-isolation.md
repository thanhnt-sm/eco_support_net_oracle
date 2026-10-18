# Audit-chain CI regression: isolated local correction

Baseline: `7e90bf0c2502e85dfa293bfff64e0f2639322260` (fetched origin/main, PR68 merge). Separate local branch `local/audit-chain-regression-20261018`. No publication or changes to the Marketplace worktree.

## Evidence and priority

P1: `AuditChainTests.MixedWriters_CredentialManagerAndDirectLogger_KeepOneValidChain` reads process environment without joining the credential tests' `Sequential` collection. Other members temporarily set `DATAGUARD_CONNECTION_STRING`. The collection had no nonparallel definition. `CredentialManager.GetConnectionStringAsync` intentionally prefers the environment; after storing the configured credential, a different override legitimately causes a sixth rotation event. Timestamps do not participate in the rotation comparison. Independent security review confirmed the source path.

Deterministic baseline reproduction: run the named test with synthetic `DATAGUARD_CONNECTION_STRING=Server=synthetic-race;Database=Test`; it fails with exactly six records and rotation immediately before final access. This reproduces the reported signature, but does not identify which concurrent test wrote the variable in the hosted failure.

## Bounded patch and acceptance

1. Define the existing Sequential collection as nonparallel; add AuditChainTests to it. Save/clear/restore ambient connection and audit-key variables without logging them.
2. Preserve the original five-record and HMAC-chain assertions. Add a synthetic real-override case requiring six ordered events, exactly one rotation, salted fingerprints and no plaintext. Add a structural regression for collection membership/definition, failing before the isolation correction.
3. Run focused red/green, full cross-platform Release build/tests with local coverage, and docs checks. Windows/editor/hosted checks are unavailable locally. Production credential priority and rotation behavior stay unchanged; no new dependency or public API.

P2 backlog: broader ambient-environment fixtures currently clear variables rather than universally restoring inherited state. Do not broaden this urgent fix into a suite rewrite.

## Tóm tắt tiếng Việt

Lỗi P1 do kiểm thử audit đọc biến môi trường dùng chung khi kiểm thử credential khác đang sửa biến đó. CredentialManager phát hiện thay đổi đúng theo thiết kế; không phải lỗi đồng hồ hay chuỗi hash. Bản sửa cô lập collection, lưu/xóa tạm/khôi phục biến của fixture, giữ nguyên yêu cầu năm bản ghi và thêm ca giả lập xác nhận rotation thật vẫn được ghi. Kiểm tra nguồn, test và tài liệu EN/VI được ánh xạ ở trên; kết quả chạy chi tiết nằm trong báo cáo bằng chứng cục bộ.

## Verified local outcome

- Baseline synthetic override: exact reported failure (5 expected, 6 actual). Structural isolation regression: failed before the correction.
- Focused tests with synthetic inherited connection/audit-key overrides: 19 passed.
- `dotnet build DataGuard.sln --configuration Release`: succeeded, zero warnings/errors on Linux.
- Cross-platform Release suite (`Category!=LiveDb`): 2,242 passed, zero failed, five existing Windows junction skips. Core: 2,007 passed plus five skipped.
- Five coverage reports validated: 17,069/20,995 measured production lines (81.30%); this is not whole-product coverage.
- Docs synchronization, changed-file whitespace format, diff checks: passed. Independent security review: no blocking findings.
- Initial sandboxed coverage run failed because CLI instrumentation could not create a named mutex (EROFS); retained separately. The authorized rerun outside that filesystem restriction passed without source/assertion changes.
- No Windows/editor-host/live-DB/hosted-CI execution, uploads, publication or merges. Node/workflow gates are not rerun for this test-only correction; the separate Marketplace work remains preserved and unfinished. Nightly53 is not validated by these results.
