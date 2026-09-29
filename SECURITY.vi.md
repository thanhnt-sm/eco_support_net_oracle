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
| 0.1.x (pre-release) | Nỗ lực tốt nhất — xem release notes |

## Tư thế bảo mật

- **Thông tin xác thực (Credentials)**: secret manager (Azure Key Vault, AWS Secrets Manager,
  HashiCorp Vault) hoặc biến môi trường là các nguồn duy nhất được hỗ trợ trong production; thông tin
  xác thực plaintext trong file cấu hình bị tắt theo mặc định (`AllowPlaintextConfigFallback=false`).
- **Chuỗi cung ứng**: package NuGet được ký (Sigstore keyless), publish qua Trusted Publishing
  (OIDC), kèm SBOM + provenance attestation; GitHub Actions được pin theo SHA.
- **CI gates**: quét lỗ hổng (fail khi có package vulnerable), quét secret bằng TruffleHog, và CodeQL
  chạy trên mọi branch/PR và tag release.
- **Audit**: việc truy cập credential được ghi vào log hash-chain chỉ-ghi-thêm chống giả mạo với khả
  năng phát hiện tail-truncation.
- **Plugins**: rule plugin chỉ được nạp từ thư mục được cấu hình rõ ràng vào isolated, collectible
  assembly-load context.
- **IDE hosts**: extension Visual Studio và VS Code chạy CLI với `--ide-safe`, bỏ qua mọi thiết lập do
  repository kiểm soát có thể nạp assembly (`GroundTruthMode: Manual`, `ManualAssemblyPath`), mở kết nối
  database/secret-manager/network, hoặc ghi vào đường dẫn do repo chọn. Visual Studio còn yêu cầu đồng ý
  một lần cho mỗi solution và hash `.dataguard.yml` trước lần chạy đầu, không bao giờ hỏi từ build event,
  không tự cài CLI, và chỉ chấp nhận đường dẫn CLI tuyệt đối.
