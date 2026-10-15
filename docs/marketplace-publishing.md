# DataGuard — Marketplace Publishing Runbook

## Publication paused — 2026-10-15

The product is not ready for marketplace distribution. `marketplace.yml` now runs only
`test`, `vscode-package`, and `visual-studio-package` on push, pull request, and manual dispatch.
Both publication jobs and the `publish` input are removed, so PRs have no skipped publish checks.
Build/test/package gates, VSIX/checksum/SBOM artifacts, fork upload guards, and non-PR
attestations remain enabled. `tag` still selects the version to package.

`dg-release` defaults to no marketplace publication and rejects `--publish-marketplaces`
or configuration `PUBLISH_MARKETPLACES=true` before tag creation/push/API calls, even in dry-run.
Use `--no-marketplaces` to override an older configuration. Other release channels are unchanged.
No repository secrets, environment approvals, permissions, or branch rules were changed.
Existing tags/branches retain their historical workflow definitions; do not dispatch an old
publishing workflow during this pause. This is a source change, not credential revocation.

## Re-enable only after product readiness approval

1. In a reviewed PR, recover the two publication job definitions from
   `0fb5f3f91078ba0df508f41cc822d6192e1be748:.github/workflows/marketplace.yml`.
   Prefer a separate manual-only publication workflow to keep publication checks off PRs;
   retain immutable-tag validation, both packaging/test dependencies, artifact verification,
   pinned tooling, and `marketplace-production` approvals. Do not blindly restore old tooling.
2. Verify publisher ownership, credential validity, Windows publishing tooling and IDE smoke
   results. Resolve the Visual Studio checksum/provenance verification gap before enabling it.
3. Restore the dispatch contract and release-helper preflight together; update topology and
   release-helper tests and these runbooks. Keep publishing opt-in, never default-on.
4. Run actionlint, workflow policy/unit tests, release-helper tests, unchanged local hooks,
   independent review, and CI on the final head. Actual publication needs separate owner approval.

The instructions below are preparation for that future re-enablement, **not executable
publication instructions during the pause**. Secrets `VSCE_PAT` / `VS_MARKETPLACE_PAT`,
environment `marketplace-production`, and publisher ID `thanhnt-sm` are historical configuration;
their current availability was not inspected.

## Prerequisite 1 — Tạo/verify publisher (bắt buộc, do owner làm)

VS Code và Visual Studio Marketplace dùng chung nền Azure DevOps. Publisher ID là identity riêng, **không** tự sinh từ GitHub username.

1. Truy cập https://marketplace.visualstudio.com/manage, đăng nhập bằng Microsoft account của owner.
2. Chọn **Create publisher**.
3. Điền:
   - **ID**: `thanhnt-sm` (chỉ dùng nếu ID này còn trống và do owner sở hữu; đây là giá trị đang ghi trong `src/DataGuard.VSCode/package.json` và `src/DataGuard.VisualStudio/vs-publish.json`).
   - **Name**: `Than Nguyen` (hoặc tên hiển thị owner chọn).
4. Ghi nhận publisher ID cuối cùng. Nếu khác `thanhnt-sm`, phải cập nhật `package.json`, `vs-publish.json`, `source.extension.vsixmanifest` trước khi publish.

## Prerequisite 2 — Tạo Personal Access Token (PAT)

### VS Code Marketplace (`VSCE_PAT`)

1. Vào https://dev.azure.com (tổ chức của owner).
2. **User settings → Personal access tokens → New Token**.
3. Tên token tùy chọn; Organization: **All accessible organizations**.
4. Scopes: **Custom defined** → mở **Show all scopes** → **Marketplace** → tick **Manage**.
5. Copy token ngay (chỉ hiện một lần).

> Deadline 2026-12-01: global PAT bị retire. Sau này chuyển sang Microsoft Entra workload identity (`vsce publish --azure-credential`). Runbook này dùng PAT làm bước đầu; không để PAT hết hạn gây đứt pipeline.

### Visual Studio Marketplace (`VS_MARKETPLACE_PAT`)

`VsixPublisher.exe` dùng PAT cho publisher tương ứng. Có thể dùng cùng token Marketplace Manage, hoặc token riêng với quyền Marketplace Manage.

## Prerequisite 3 — Tạo GitHub Environment và secrets

1. Repository → **Settings → Environments → New environment**.
2. Tên: **`marketplace-production`** (khớp `environment:` trong workflow).
3. Thêm environment secrets:

| Secret | Giá trị |
|---|---|
| `VSCE_PAT` | PAT Azure DevOps với scope Marketplace → Manage |
| `VS_MARKETPLACE_PAT` | PAT cho VsixPublisher.exe (Marketplace Manage) |

## Bước 4 — Smoke test Visual Studio (gate bắt buộc)

Trước khi publish public, phải chạy trên máy Windows có Visual Studio 2022:

1. Tải artifact `dataguard-visualstudio-vsix` từ run Marketplace gần nhất.
2. Giải nén `.vsix`, double-click để cài vào VS 2022 (hoặc **Extensions → Manage Extensions → Install**).
3. Mở solution có `.dataguard.yml`, chạy **Tools → DataGuard: Run Validation**.
4. Xác nhận: Error List có diagnostics từ SARIF; không hiển thị stdout/stderr CLI thô.
5. Test missing-CLI path (xóa `dataguard` khỏi PATH hoặc `DATAGUARD_CLI_PATH` sai) và **Cancel Validation** (process tree bị terminate).

Không publish public khi chưa qua smoke này.

## Bước 5 — Tạo release tag (SemVer mới)

Không di chuyển tag cũ `v0.1.0`. Tạo tag mới trỏ đúng commit đã verify:

```bash
git fetch origin --tags
git checkout main
git pull --ff-only origin main
git tag v0.2.0
git push origin v0.2.0
```

Tag phải khớp commit được package trong cùng run publish.

## Bước 6 — Dispatch publish (only after re-enablement)

Repository → **Actions → Marketplace Extensions → Run workflow**:

- `tag`: `v0.2.0` (hoặc tag ở bước 5)
- `publish`: **true**

Workflow sẽ:
1. Package + attest + SBOM cho cả hai artifact tại chính tag đó.
2. Publish VS Code qua `npx --no-install @vscode/vsce publish --packagePath "$VSIX_PATH" (PAT via environment)`.
3. Publish Visual Studio qua `VsixPublisher.exe publish`.

## Bước 7 — Xác minh kết quả

- **VS Code**: https://marketplace.visualstudio.com/items?itemName=thanhnt-sm.dataguard-vscode
- **Visual Studio**: https://marketplace.visualstudio.com/items?itemName=thanhnt-sm.DataGuard.VisualStudio

Kiểm tra version khớp tag, có icon/description/license, và acquiition có thể cài từ host tương ứng.

## Known gaps (không được coi là đã hoàn tất)

- `publish-visualstudio` chạy `vswhere` để tìm `VsixPublisher.exe`. GitHub-hosted `windows-latest` thường **không** có Visual Studio extension development workload → job sẽ fail "VsixPublisher.exe was not found". Trước khi automate, phải:
  - Cài workload trong runner (self-hosted hoặc setup bước cài), **hoặc**
  - Dùng fallback thủ công: tải `.vsix` artifact rồi upload qua https://marketplace.visualstudio.com/manage → **New extension**.
- Chưa có VS 2022 Experimental Instance smoke tự động trên CI; bước 4 là thủ công.
