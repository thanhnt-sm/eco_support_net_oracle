---
status: completed
date: 2026-08-20
verdict: CAUTION
reviewer: MarketplaceRedTeam
---

# Red-team: DataGuard Marketplace product

## Verdict

**CAUTION — không package/publish public** cho tới khi toàn bộ P1 gates dưới đây được chứng minh. Kiến trúc thin-host + CLI authority là đúng; thiết kế ban đầu chưa đủ an toàn/operable cho enterprise vì CLI output, workspace trust, child-process lifecycle, version invariant và VSIX release evidence còn thiếu.

## P1 bắt buộc

| Rủi ro | Evidence | Quyết định bắt buộc |
|---|---|---|
| SARIF không đáng tin | CLI `--format` hiện không được wired; stdout lẫn progress/human output | Làm `--format`/`--output` thành contract thật. Host yêu cầu CLI ghi SARIF vào temp file private, parse xong xóa; không parse stdout. |
| Untrusted workspace execution | VS Code extension hiện spawn CLI cho workspace bất kỳ | Chặn toàn bộ execution khi `workspace.isTrusted` false; user phải trust workspace chủ động. |
| Dangling/parallel database processes | VS Code hiện không single-flight/cancel/timeout; Visual Studio design ban đầu chưa có lifecycle cụ thể | Một run/workspace/solution; cancel visible; timeout bounded; kill process tree do extension sở hữu; dispose không để child sống. |
| Version drift | package/manifest có version độc lập; release tag là source of truth | Derive/verify mọi VSIX manifest và `package.json` version từ release tag trước package/publish. |
| Visual Studio package chỉ “build” | Build VSIX không chứng minh command install/run được | CI/manual gate phải cài vào VS 2022 Experimental Instance, invoke command, validate success và missing-CLI error path. |
| Enterprise audit gap | Existing release signs only NuGet packages; VSIX chưa có evidence | Hai VSIX phải có checksum, SBOM, provenance/attestation và artifact retention; VSSDK/VsixPublisher phải provision/resolve explicit trên Windows. |

## P2 phải đưa vào implementation

1. Publisher ID không được placeholder hoặc suy ra từ GitHub username; verify owner trước release.
2. `VSCE_PAT`/`VS_MARKETPLACE_PAT` chỉ ở protected environment. Thiếu secret fail loud tại publish job, không block package artifact.
3. Không lưu connection string/config secret trong VS Code settings hoặc VS registry; chỉ pass config path do user chọn.
4. Không telemetry/network egress; online database validation chỉ do CLI explicit config và policy cho phép.
5. Limit output, redact CLI output before UI display when it could include connection config; audit evidence luôn redact.
6. Không tự chạy validate theo typing/save; database operations chỉ user command/explicit task để bảo vệ latency, connection pool và production load.

## Required acceptance gates

- [ ] CLI unit/integration tests cover private SARIF output, redaction, stable schema and nonzero policy exit.
- [ ] VS Code test: untrusted workspace no spawn; trusted workspace start/cancel/timeout/single-flight; diagnostic location maps from SARIF; temporary output deleted.
- [ ] Visual Studio test: command exists and invokes in VS 2022 Experimental Instance; cancel/timeout terminates child tree; missing CLI path actionable.
- [ ] CI Windows identifies a pinned VSSDK/VS install and produces deterministic VSIX.
- [ ] Both VSIX artifacts have checksum, SBOM and provenance attached to the same release tag.
- [ ] Protected publish jobs verify expected publisher and version/tag equality before calling `vsce`/`VsixPublisher.exe`.

## Conclusion

Proceed only after Phase 2 establishes a tested CLI output/evidence contract. UI work before that would lock both hosts onto an unreliable output interface.
