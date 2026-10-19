[Tiếng Việt](release_guide.vi.md) | [English](release_guide.md)

> Marketplace publication is temporarily paused (2026-10-15). Use `--no-marketplaces`;
> `--publish-marketplaces` / `PUBLISH_MARKETPLACES=true` fail before release side effects.
> See [legacy pause and separate manual route](../marketplace-publishing.md).
> Existing settings are preserved; this guide does not authorize credential/protection changes.

# Hướng dẫn phát hành an toàn

`tools/git-tools/dg-release` là helper release bằng tag. Tool tạo/push tag annotated; chỉ chờ workflow **Release** khi có `DG_RELEASE_GITHUB_TOKEN`. Không dispatch Marketplace. Push tag khi không theo dõi API không chứng minh job release đã thành công.

Helper nhận `DG_RELEASE_GITHUB_TOKEN` từ environment hoặc, để tương thích, từ `.release.env`
khi environment chưa có giá trị. Tool có đọc file cấu hình đó; không đặt token vào ví dụ dưới
đây, ưu tiên cơ chế session đã được duyệt. Upload Marketplace qua trình duyệt không cần PAT
hay token dispatch GitHub. Xem [đường trình duyệt và gate](../marketplace-publishing.vi.md).

## 1. Cấu hình và xác thực hiện có

Hướng dẫn không cho phép tạo credential hay đổi bảo vệ repository. Kiểm tra workflow được chọn và cấu hình owner đã duyệt trước khi release thật.

- `release.yml` dùng environment `release`; tên đó không chứng minh đã có reviewer/branch protection.
- `marketplace.yml` cũ không có job publish. [Workflow Marketplace thủ công riêng](../marketplace-automation.vi.md) cũng dùng `release`; không tạo `marketplace-production` để làm theo hướng dẫn này.
- NuGet dùng `NUGET_USER` cho Trusted Publishing khi có cấu hình, fallback `NUGET_API_KEY`. Nếu thiếu cả hai, workflow cảnh báo rồi bỏ qua NuGet push: run xanh không chứng minh package đã có trên NuGet.
- `VSCE_PAT`/`VS_MARKETPLACE_PAT` thuộc đường Marketplace opt-in riêng, không phải `dg-release`. Upload browser không cần PAT. Tuân thủ gate xác thực/nghiệm thu của đường đó; không tạo secret rộng chỉ để vượt lỗi.

### Tạo token dispatch ở máy local

Nếu cần theo dõi qua API và owner đã cho phép tạo token riêng, vào **GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens** để tạo fine-grained PAT.

- Resource owner: chủ repository.
- Repository access: **Only select repositories** → chỉ repository này.
- Repository permissions: **Actions: Read and write**, **Contents: Read**.
- Expiration: 30 ngày hoặc ngắn nhất phù hợp.
- Không cấp Administration, Secrets, Workflows ở repository khác, Packages, hoặc quyền toàn organization.

Trong đường helper hiện tại, token dùng theo dõi workflow; push tag dùng credential Git transport có sẵn. Không bật dispatch Marketplace. Workflow tham chiếu credential NuGet/Marketplace bằng tên secret; không chứng minh đã cấu hình hay không có bản sao ở máy khác. Không chép credential vào máy chỉ để upload qua trình duyệt.

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
   PUBLISH_MARKETPLACES=false
   DRY_RUN=true
   CONFIRM_RELEASE=false
   ```
   *Lưu ý bảo mật*: `.gitignore` bỏ qua `.release.env` nhưng không mã hóa hay bảo đảm tránh lộ file. Không đặt token vào ví dụ này.

3. Chạy helper để kiểm tra cấu hình dry-run này:
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
- Nhánh hiện tại khớp `origin/HEAD` (fallback `main`) và commit remote-tracking tương ứng sau fetch.
- Tag đúng chuẩn SemVer có tiền tố `v`.
- Tag chưa từng tồn tại ở cả local lẫn GitHub (bảo đảm tính bất biến).
- Bộ phân tích JSON (`jq` hoặc `python`) sẵn sàng.

## 4. Bắt đầu tag release được cho phép

Sau khi dry-run pass và owner đã xác minh reviewer/bảo vệ environment thực tế đã sẵn sàng:

```bash
bash tools/git-tools/dg-release \
  --tag v1.2.3 \
  --no-marketplaces \
  --yes
```

Tool chạy tuần tự:

1. Tạo/push immutable annotated tag `v1.2.3`.
2. Có token thì chờ `release.yml`; thiếu token thì trả về sau push tag. Workflow có job build/test/sign/SBOM, GitHub Release, NuGet và GHCR, phụ thuộc gate và credential; NuGet có thể bị bỏ qua.
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

`--dry-run` của helper không tạo/push tag mới hay dispatch/publish. Preflight vẫn fetch refs/tags với prune/force và kiểm tra tag remote, nên không offline và có thể đổi local refs. Khác với dry-run hosted của `release.yml` vẫn build/ký/upload artifact. Không bypass fetch để tuyên bố sẵn sàng release.

## 6. Xử lý lỗi
- **Preflight fail:** sửa local state được báo. Chưa có remote state nào thay đổi.
- **Release workflow fail sau khi push tag:** không dùng lại, xóa hoặc force-move tag. Sửa lỗi, tăng version SemVer và tạo tag immutable mới.
- **Legacy Marketplace paused:** helper từ chối bật Marketplace. Chỉ dùng runbook thủ công riêng khi có quyền công bố rõ và artifact đã nghiệm thu.
- **Lộ credential:** revoke ngay tại issuer, rotate GitHub Actions secret liên quan, xem GitHub audit log và scan lịch sử repository trước khi retry.

## 6. Điều tool không được tự động hóa ở máy local

`dg-release` không nhận credential write của NuGet, VS Code Marketplace, Visual Studio Marketplace hay GitHub Release. Các thao tác đó chỉ chạy trên GitHub-hosted runners, với tham chiếu secret/OIDC và attestation. Tên environment trong YAML không chứng minh có bảo vệ; phải xác minh reviewer và giới hạn branch thực tế trước khi dựa vào gate phê duyệt.

## Nightly khác Marketplace

`installers.yml` có thể công bố GitHub nightly khi push main đủ điều kiện; dispatch mặc định
cũng công bố. Job xóa/tạo lại release và tag nightly, không công bố Marketplace. Chọn rõ
`publish_prerelease=false` khi chỉ cần artifact. Xem [trigger, paths-ignore và yêu cầu gói bất biến](../marketplace-publishing.vi.md#nightly-là-kênh-công-bố-khác).

## Bằng chứng cần sau release

Không coi banner success cuối helper là xác nhận mọi kênh: banner vẫn in khi không theo dõi API; NuGet có thể bỏ qua nếu thiếu credential. Kiểm tra tag/SHA, job, chữ ký artifact và từng registry/listing đích. Review source chưa chạy push tag có xác thực, OIDC/fallback, hosted signing, approval environment hoặc registry publication. Chỉ kiểm thử các bước đó trong lần release được owner cho phép; task tài liệu không đổi credential/setting.
