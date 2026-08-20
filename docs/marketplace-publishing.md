# DataGuard — Marketplace Publishing Runbook

Hướng dẫn từng bước cấu hình và phát hành hai extension lên Microsoft Marketplace. Các giá trị dưới đây khớp đúng workflow hiện tại (`.github/workflows/marketplace.yml`): environment `marketplace-production`, secrets `VSCE_PAT` và `VS_MARKETPLACE_PAT`, publisher ID `thanhnt-sm`.

## Trạng thái hiện tại

- Package jobs đã xanh trên CI: tạo `.vsix`, SHA-256, SBOM và provenance attestation cho cả hai artifact.
- Publish jobs chỉ chạy khi `workflow_dispatch` với `publish=true`, dùng environment `marketplace-production`.
- Chưa publish public vì thiếu publisher đã verify và thiếu secrets.

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

## Bước 6 — Dispatch publish

Repository → **Actions → Marketplace Extensions → Run workflow**:

- `tag`: `v0.2.0` (hoặc tag ở bước 5)
- `publish`: **true**

Workflow sẽ:
1. Package + attest + SBOM cho cả hai artifact tại chính tag đó.
2. Publish VS Code qua `vsce publish --pat "$VSCE_PAT"`.
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
