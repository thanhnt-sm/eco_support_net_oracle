[Tiếng Việt](release_guide.vi.md) | [English](release_guide.md)

> Marketplace publication is temporarily paused (2026-10-15). Use `--no-marketplaces`;
> `--publish-marketplaces` / `PUBLISH_MARKETPLACES=true` fail before release side effects.
> See [pause and re-enablement](../marketplace-publishing.md). Marketplace environment/secret
> setup below is future preparation only; existing settings are preserved.

# Hướng dẫn phát hành an toàn

`tools/git-tools/dg-release` là một điểm vào rõ ràng để phát hành. Tool tạo một Git tag annotated bất biến, chờ workflow GitHub **Release** hoàn tất, không dispatch Marketplace trong thời gian tạm dừng.

Tool không nhận secret qua command line, không đọc file chứa secret và không in credential ra log.

## 1. Cấu hình GitHub một lần

### Tạo environment bảo vệ Marketplace

Vào **Settings → Environments**, tạo `marketplace-production`.

- Bắt buộc maintainer duyệt deployment.
- Nếu gói GitHub hỗ trợ, chỉ cho phép protected tag khớp `v*` deploy vào environment này.
- Không thêm tài khoản bypass trừ khi có quy trình incident được ghi nhận.

The publication jobs are currently removed. Preserve the environment for reviewed re-enablement.

### Tạo Actions repository secrets

Vào **Settings → Secrets and variables → Actions**, tạo các repository secret sau. Không đưa giá trị thật vào `.env`, `.dg-git.yml`, source code, issue/comment hay shell history.

| Secret | Workflow dùng | Giá trị và nguyên tắc tối thiểu quyền |
|---|---|---|
| `NUGET_USER` | NuGet Trusted Publishing | Tên profile NuGet.org. Đây là đường xác thực ưu tiên, không cần API key khi trust policy đã cấu hình. |
| `NUGET_API_KEY` | Chỉ fallback NuGet | Scoped key chỉ cho package ID DataGuard và quyền push. Đặt hạn ngắn, xóa khi Trusted Publishing hoạt động ổn định. |
| `VSCE_PAT` | VS Code Marketplace | Azure DevOps Marketplace PAT chỉ có quyền **Marketplace Manage** của publisher DataGuard. |
| `VS_MARKETPLACE_PAT` | Visual Studio Marketplace | PAT của Visual Studio Marketplace publisher, chỉ có quyền publish cần thiết. |

Trước production release đầu tiên, cấu hình NuGet Trusted Publishing cho repository này và `.github/workflows/release.yml`. Workflow chỉ dùng `NUGET_API_KEY` khi OIDC login không trả credential.

### Tạo token dispatch ở máy local

Vào **GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens**, tạo fine-grained PAT.

- Resource owner: chủ repository.
- Repository access: **Only select repositories** → chỉ repository này.
- Repository permissions: **Actions: Read and write**, **Contents: Read**.
- Expiration: 30 ngày hoặc ngắn nhất phù hợp.
- Không cấp Administration, Secrets, Workflows ở repository khác, Packages, hoặc quyền toàn organization.

Token này chỉ được dùng để theo dõi/gọi GitHub workflow. Credential NuGet và Marketplace vẫn chỉ nằm trong GitHub Actions secrets, không tồn tại trên máy local.

Nhập token vào shell hiện tại, không ghi vào file:

```bash
read -rsp 'GitHub release dispatcher token: ' DG_RELEASE_GITHUB_TOKEN; echo
export DG_RELEASE_GITHUB_TOKEN
```

PowerShell:

```powershell
$env:DG_RELEASE_GITHUB_TOKEN = Read-Host 'GitHub release dispatcher token' -AsSecureString |
  ConvertFrom-SecureString -AsPlainText
```

Trong PowerShell, nên đọc token từ password manager hoặc Windows Credential Manager thay vì lưu dài hạn. Sau release hãy xóa biến:

```bash
unset DG_RELEASE_GITHUB_TOKEN
```

```powershell
Remove-Item Env:DG_RELEASE_GITHUB_TOKEN
```

## 2. Quy trình cấu hình bằng file (không cần nhớ option)

Để release nhanh chóng mà không cần ghi nhớ các cờ dòng lệnh:

1. Copy file cấu hình mẫu:
   ```bash
   cp .release.env.example .release.env
   ```
2. Mở `.release.env` và điền các thông tin:
   ```env
   RELEASE_TAG=v1.0.0
   PUBLISH_MARKETPLACES=true
   DG_RELEASE_GITHUB_TOKEN=github_pat_...
   DRY_RUN=false
   CONFIRM_RELEASE=true
   ```
   *Lưu ý bảo mật*: File `.release.env` được quản lý nghiêm ngặt trong `.gitignore`, không bao giờ bị commit lên GitHub.

3. Chạy 1 lệnh duy nhất để toàn bộ hệ thống tự động chạy:
   ```bash
   bash tools/git-tools/dg-release
   ```
   Trên Windows Command Prompt (CMD) / PowerShell:
   ```cmd
   tools\git-tools\dg-release.cmd
   ```
   Hoặc thông qua bộ công cụ git:
   ```bash
   dg-git release
   ```

## 3. Quy trình kiểm tra thử nghiệm (Dry-run)

Trước khi phát hành thật sự, bạn có thể đặt `DRY_RUN=true` trong `.release.env` hoặc truyền cờ `--dry-run`:
```bash
bash tools/git-tools/dg-release --dry-run
```
Tool sẽ kiểm tra toàn diện:
- Thư mục làm việc sạch sẽ (chặn nếu còn code chưa commit).
- Nhánh hiện tại là `main` và đã khớp hoàn toàn với `origin/main`.
- Tag đúng chuẩn SemVer có tiền tố `v`.
- Tag chưa từng tồn tại ở cả local lẫn GitHub (bảo đảm tính bất biến).
- Bộ phân tích JSON (`jq` hoặc `python`) sẵn sàng.

## 4. Phát hành tất cả nền tảng

Sau khi dry-run pass và maintainer bảo vệ environment đã sẵn sàng:

```bash
bash tools/git-tools/dg-release \
  --tag v1.2.3 \
  --no-marketplaces \
  --yes
```

Tool chạy tuần tự:

1. Tạo/push immutable annotated tag `v1.2.3`.
2. Chờ `release.yml`: build, test, scan, package, sign, SBOM, publish NuGet, tạo GitHub Release, attest package và push image GHCR.
3. Marketplace publication is paused; no Marketplace dispatch occurs.

Nếu cần thời gian dài chủ động, dùng `--timeout-seconds 10800` cho ba giờ. Mặc định là hai giờ.

## 5. Biến thể an toàn hơn
Chỉ release NuGet/GitHub/GHCR, không publish extensions:

```bash
bash tools/git-tools/dg-release --tag v1.2.3 --yes
```

Lệnh vẫn trigger Release workflow nhưng không dispatch Marketplace.

## 6. Bàn giao artifact thủ công (không publish)

Dùng `.github/workflows/build_release.yml` khi cần bundle installer theo version nhưng không tạo GitHub Release và không publish lên NuGet, VS Code Marketplace, Visual Studio Marketplace hoặc GHCR.

Chạy từ giao diện GitHub Actions hoặc bằng:

```bash
gh workflow run build_release.yml -f version=1.2.3
gh workflow run build_release.yml -f version=1.2.3 -f include_docker=true
```

Version phải là SemVer không có tiền tố `v`. Workflow build từ revision đang được dispatch, không tự suy diễn tag hoặc branch. Tải artifact theo version, gồm `dataguard-installers-1.2.3`, trong thời hạn lưu 30 ngày. Aggregate chứa CLI portable archives, hai gói VSIX, NuGet packages, checksum sidecar, `SHA256SUMS.txt` và `release-manifest.json`. Phải kiểm tra checksum trước khi cài. Job Docker tùy chọn chỉ tạo OCI archive, không login hoặc push registry.

Đây là đường bàn giao riêng với production tag release ở trên: `dg-release` vẫn là entry point rõ ràng cho phát hành/publish.

Dry-run không tạo tag, không dispatch workflow, không publish package, không tạo release và không push image.

## 6. Xử lý lỗi
- **Preflight fail:** sửa local state được báo. Chưa có remote state nào thay đổi.
- **Release workflow fail sau khi push tag:** không dùng lại, xóa hoặc force-move tag. Sửa lỗi, tăng version SemVer và tạo tag immutable mới.
- **Marketplace paused:** do not retry `publish=true`; follow the re-enablement runbook first.
- **Lộ credential:** revoke ngay tại issuer, rotate GitHub Actions secret liên quan, xem GitHub audit log và scan lịch sử repository trước khi retry.

## 6. Điều tool không được tự động hóa ở máy local

`dg-release` không nhận credential write của NuGet, VS Code Marketplace, Visual Studio Marketplace hay GitHub Release. Các thao tác đó chỉ chạy trên GitHub-hosted runners, với secret scope hẹp, OIDC, build attestation và protected environment.
