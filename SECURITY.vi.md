# Chính sách bảo mật

## Báo cáo lỗ hổng

Chúng tôi coi trọng bảo mật của DataGuard và hệ sinh thái .NET/EF Core downstream mà nó bảo vệ.

Nếu bạn phát hiện một lỗ hổng tiềm ẩn trong DataGuard (Core, adapters, analyzers, CLI, hoặc extension
VS Code), vui lòng báo cáo một cách riêng tư:

- Mở **GitHub Security Advisory riêng tư** tại
  `https://github.com/thanhnt-sm/eco_support_net_oracle/security/advisories`
  (khuyến nghị — advisory được giữ riêng tư cho đến khi fix được phát hành)

Vui lòng kèm theo:

- Package/thành phần bị ảnh hưởng và phiên bản (hoặc commit SHA)
- Bản tái hiện tối thiểu (đoạn SQL, config, hoặc code)
- Mô tả tác động (rò rỉ dữ liệu, injection, từ chối dịch vụ, chuỗi cung ứng)

Chúng tôi cam kết phản hồi báo cáo trong vòng 5 ngày làm việc và phát hành fix nhanh nhất có thể
tùy theo mức độ nghiêm trọng.

## Các phiên bản được hỗ trợ

| Phiên bản | Hỗ trợ |
|---------|-----------|
| 0.2.x (tag mới nhất `v0.2.2`) | Nỗ lực tốt nhất — xem release notes; CLI 0.2.x bị IDE host từ chối cho `validate`/`assess` |
| 0.3.x (sắp phát hành, tag kế tiếp `v0.3.0`) | Sẽ là dòng được hỗ trợ; bắt buộc với extension Visual Studio và VS Code |
| 0.1.x | Không còn hỗ trợ |

## Tư thế bảo mật

- **Thông tin xác thực (Credentials)**: secret manager (Azure Key Vault, AWS Secrets Manager,
  HashiCorp Vault) hoặc biến môi trường là các nguồn duy nhất được hỗ trợ trong production; thông tin
  xác thực plaintext trong file cấu hình bị tắt theo mặc định (`AllowPlaintextConfigFallback=false`).
- **Chuỗi cung ứng**: package NuGet được ký (Sigstore keyless), publish qua Trusted Publishing
  (OIDC), kèm SBOM + provenance attestation; GitHub Actions được pin theo SHA.
- **Allow-list giấy phép**: CI fail khi bất kỳ package NuGet nào được resolve bởi `DataGuard.sln`
  (kể cả transitive) hoặc package npm production nào của extension VS Code mang giấy phép ngoài
  `scripts/allowed-licences.txt` (các SPDX id permissive cộng với ngoại lệ từng package có lý do và
  được neo theo marker, ví dụ Visual Studio SDK và driver Oracle); xem `scripts/check-nuget-licences.py`.
- **CI gates**: quét lỗ hổng (fail khi có package vulnerable), quét secret bằng TruffleHog, và CodeQL
  chạy trên mọi branch/PR và tag release.
- **Audit**: việc truy cập credential được ghi vào log hash-chain chỉ-ghi-thêm chống giả mạo với khả
  năng phát hiện tail-truncation.
- **Plugins**: rule plugin chỉ được nạp từ thư mục được cấu hình rõ ràng vào isolated, collectible
  assembly-load context.
- **IDE hosts (`validate` / `assess`)**: extension Visual Studio và VS Code chạy `validate` và `assess`
  với `--ide-safe`, bỏ qua mọi thiết lập do repository kiểm soát có thể nạp assembly
  (`GroundTruthMode: Manual`, `ManualAssemblyPath`), mở kết nối database/secret-manager/network, hoặc
  ghi vào đường dẫn do repo chọn; đồng thời giới hạn `MaxDegreeOfParallelism` ≤ số CPU,
  `MaxViolationQueueSize` ≤ 100 000 và `ValidationTimeoutSeconds` ≤ 900. CLI in `ide-safe: active` làm
  dòng stderr đầu tiên; host huỷ kết quả nếu thiếu dòng này (CLI cũ, 0.2.2 trở xuống, từ chối cờ và được
  báo là quá cũ — cần CLI 0.3.0 trở lên). Baseline vẫn được áp dụng dưới `--ide-safe` nhưng mọi suppression
  đều hiển thị: dòng stderr `baseline: <n> violations suppressed by <path>` và progress event
  `BaselineApplied`.
- **IDE hosts (credential của người dùng)**: `--allow-env-connection` (chỉ đi kèm `--ide-safe`, chỉ với
  `validate`) giữ lại credential `DATAGUARD_CONNECTION_STRING` do host cung cấp; connection string trong
  `.dataguard.yml` luôn bị xoá. VS Code chỉ truyền cờ này khi có credential lưu trong SecretStorage.
  Dưới `--ide-safe`, `validate` dùng credential đó **chỉ để đọc catalog ground-truth** (schema và định
  nghĩa stored procedure); không bao giờ gửi hay describe SQL trích từ repository lên database — kiểm tra
  live SQL shape (`sp_describe_first_result_set` trên SQL của repo) vẫn bị tắt dưới `--ide-safe` và chỉ
  chạy qua `verify-shape`. `snapshot`, `baseline` và `verify-shape` là các lệnh kết nối database thật:
  trong VS Code chúng dùng credential của bạn và luôn hỏi xác nhận (modal) nêu host đích đã che; Visual
  Studio không có lệnh kết nối database.
- **Cổng tin cậy Visual Studio**: đồng ý một lần cho mỗi solution file (khoá theo thư mục solution, đường
  dẫn `.sln` và hash `.dataguard.yml`) trước lần chạy đầu, không bao giờ hỏi từ build event, không tự cài
  CLI, và chỉ chấp nhận đường dẫn CLI tuyệt đối.
- **Phân phối CLI**: `DataGuard.Cli` chưa được publish lên nuget.org (owner sẽ đăng ký package ID) — cài
  `dataguard` từ [GitHub Releases](https://github.com/thanhnt-sm/eco_support_net_oracle/releases): mỗi
  release đính kèm `dataguard-<version>-<rid>.zip` (`win-x64`, `linux-x64`, `osx-arm64`; framework-dependent,
  cần .NET 9 runtime) cùng file `.sha256` đi kèm và build-provenance attestation — kiểm tra checksum
  trước khi dùng; VSIX Visual Studio bundle sẵn `cli\dataguard.exe`, và cả CI lẫn
  release đều kiểm tra nội dung VSIX (`scripts/assert-vsix.ps1`). VSIX build từ pull request của fork
  không bao giờ được upload.
- **Gia cố**: mọi regex trong tiến trình CLI có match timeout 1 giây; SQL literal dài hơn 256 KiB bị bỏ
  qua kèm ghi chú `[WARN] DG1291` thay vì đưa vào rule.
