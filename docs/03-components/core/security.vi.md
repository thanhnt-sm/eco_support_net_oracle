# Hệ thống bảo mật

> Nguồn: `src/DataGuard.Core/Security/`, `src/DataGuard.Core/Security/SecretStores/`; AWS adapter CLI: `src/DataGuard.Cli/Security/AwsSecretsManagerSecretStore.cs`.

Các thành phần giải quyết credential, bảo vệ kho local khi có backend OS, ghi audit và so sánh assembly với hash được cung cấp. Đây không phải cam kết mọi input đều không chứa secret, bộ nhớ process được bảo vệ, hoặc release đã xác minh provenance SLSA. Tài liệu mô tả source, không chứng nhận cloud hay nền tảng thực tế.

## Giải quyết credential

Constructor `ZeroTrustCredentialProvider` trong nguồn hiện tại ghép Azure → store do host đăng ký theo thứ tự được truyền → Vault. Resolver duyệt danh sách đó, chỉ gọi store có `IsConfigured=true`; bản thân `ISecretStore` không quy định thứ tự. Với cách ghép này, lookup tổng thể là:

| Thứ tự | Nguồn | Ranh giới |
| --- | --- | --- |
| 1 | `DATAGUARD_CONNECTION_STRING` cho database, rồi `DATAGUARD_{NAME}` | Giá trị môi trường ưu tiên; quyền đọc environment/process là đặc quyền. |
| 2 | Azure Key Vault | Kiểm tra endpoint trước khi xin token IMDS. |
| 3 | `ISecretStore` do host đăng ký | CLI đăng ký AWS Secrets Manager khi cấu hình; Core không phụ thuộc AWS SDK. |
| 4 | HashiCorp Vault | `VaultAddress` phải khớp `VAULT_ADDR` cung cấp độc lập trước khi đọc `VAULT_TOKEN`. |
| 5 | Kho credential local | Qua `CredentialManager.GetStoredConnectionStringAsync`; bảo vệ và khả dụng tùy nền tảng. |
| 6 | Configuration | Chỉ khi `AllowPlaintextConfigFallback=true`; nếu không sẽ từ chối fallback này. |

Store không có giá trị thì thử nguồn tiếp theo. Lỗi được log bằng định danh store/credential và loại exception, không dùng message tùy ý, rồi tiếp tục resolve. Cancellation được yêu cầu vẫn truyền lên. Vì vậy lỗi một store không làm dừng toàn bộ quá trình. Cờ plaintext dành cho development nhưng provider chỉ kiểm tra cờ, không kiểm tra riêng danh tính môi trường runtime.

Azure chỉ nhận root HTTPS dạng `https://name.vault.azure.net` trên port mặc định, không user information/query/fragment. Token managed identity lấy từ IMDS cố định; tên secret được escape trong request. Không hỗ trợ domain Vault tùy ý hoặc sovereign cloud trong triển khai này.

HashiCorp Vault yêu cầu root HTTPS không user information/query/fragment; origin cấu hình và origin tin cậy phải có cùng IDN host (không phân biệt hoa thường) và port. Store đọc KV v2 `/v1/secret/data/{escaped-name}`, trường `data.data.value`. HTTPS không tự cho phép gửi token môi trường đến endpoint do repository chọn. Shared HTTP client production tắt redirect; caller inject client vào store phải giữ yêu cầu đó.

## Bộ nhớ và lưu trữ credential

`CredentialHandle` giữ `char[]`; `Dispose` xóa array này, finalizer là fallback. `Use` truyền array gốc cho callback và **không** xóa sau mỗi lần gọi. Callback có thể giữ/copy dữ liệu; `GetString` tạo string bất biến. Không API nào ngăn memory dump, bản sao string trước đó hoặc caller dùng sai.

API riêng của `CredentialManager` chọn environment → connection string trong config → giá trị lưu. Đây là hành vi khác với fallback có cờ của `ZeroTrustCredentialProvider`; caller phải chọn API phù hợp.

| Nền tảng | Lưu trữ có bảo vệ |
| --- | --- |
| Windows | DPAPI `CurrentUser`, entropy cố định `DataGuard.Credential.Protection`, record chứa `ENC:` và ciphertext base64. |
| macOS | Login Keychain qua Security.framework; file giữ reference `KEYCHAIN:`. |
| Linux | Secret Service qua `/usr/bin/secret-tool`; file giữ reference `SECRET-SERVICE:`. Secret đi qua stdin, không qua command arguments. |

Helper Linux có timeout 10 giây, output giới hạn 1 MiB secret và 16 KiB lỗi. Chọn backend OS hoặc inject `ICredentialSecretStore` không chứng minh phiên Keychain/Secret Service thực tế dùng được. Khi yêu cầu encryption nhưng không có backend, store cảnh báo và fallback plaintext chỉ chủ sở hữu truy cập, trừ khi `RequireEncryptedCredentialStore=true`. Backend đã có nhưng ghi lỗi thì fail, không ghi plaintext rồi giả làm encrypted. `EncryptConnectionStringAtRest=false` chọn plaintext tường minh.

Path file credential từ chối link/reparse point và ancestor liên kết, trừ alias temporary-root cố định của OS. Đọc/record giới hạn 1 MiB. Ghi dùng temporary file cùng thư mục, flush và quyền Unix chỉ chủ sở hữu. Recheck path cuối giảm nhưng không xóa race check-to-open nếu process khác có thể sửa thư mục.

Rotation detection so sánh connection string hiện tại với giá trị lưu rồi phát audit fingerprint khi khác; không tự đổi credential tại database. `CredentialRotationWarningDays` là cấu hình được khai báo, không chứng minh có dịch vụ rotation theo lịch trong thành phần này.

## Audit logging

`FileAuditLogger.Create(config)` chọn HMAC key tùy chọn từ `DATAGUARD_AUDIT_KEY` hoặc `AuditKeyFile` (text trim, ít nhất 16 byte). Có key dùng HMAC-SHA256; không key dùng SHA-256 và trạng thái `Unkeyed`, không phải toàn vẹn có xác thực. `NullAuditLogger` không ghi log.

Entry nối hash trước với content serialize. Lock theo path trong process và file handle độc quyền ghi với retry có giới hạn điều phối append; writer đọc lại tail. Checkpoint giữ hash cuối, cờ keyed và salt theo file để tạo fingerprint. Verification kiểm tra chuỗi, downgrade keyed→unkeyed và checkpoint, trả `Valid`, `Unkeyed`, `Tampered` hoặc `KeyRequired`.

Đây là bằng chứng toàn vẹn local, không phải dịch vụ audit bất biến độc lập. Kẻ có thể viết lại file có thể tính lại chuỗi không key; HMAC phụ thuộc bảo vệ key. Phải bảo vệ log, checkpoint và thư mục. Khôi phục khi crash/ghi dở hoặc filesystem đối kháng cần kiểm chứng vận hành riêng.

Pattern nhạy cảm đã biết được redact; masking config chỉ giữ tối đa hai ký tự đầu/cuối với giá trị dài, che giá trị ngắn. Không thể nhận diện mọi secret trong text tùy ý. Không đưa secret vào tên, details tự do, exception message hoặc nhãn cấu hình.

## Kiểm tra chuỗi cung ứng

`SupplyChainVerifier.VerifyAsync` hash Core assembly hiện tại. Anchor phải là path tuyệt đối tồn tại, không file/ancestor liên kết, tối đa 1.024 byte. `ExpectedHashMatch` so sánh text trim của file với SHA-256 assembly. Caller phải xác lập nguồn gốc anchor độc lập: code không xác minh chữ ký, attestation hoặc tài liệu SLSA.

`AssemblyIntegrity` phản ánh có path anchor hợp lệ; việc khớp hash nằm ở `ExpectedHashMatch`. Mọi dependency được tham chiếu hiện đều báo chưa xác minh. Chưa có tích hợp verifier signed-provenance/SBOM để chuyển kết quả dependency thành đạt. `StrongNameSigning` chỉ thông tin và luôn đạt; dấu hiệu debug-build có thể làm check thất bại. `OverallPassed` đòi mọi check đạt. Không mô tả thành phần này như chứng nhận release.

## Cấu hình và bằng chứng

Mặc định: `EnableCredentialRotationDetection=true`, `EncryptConnectionStringAtRest=true`, `RequireEncryptedCredentialStore=false`, `EnableAuditLogging=true`, `AllowPlaintextConfigFallback=false`; địa chỉ/region cloud và audit path/key file là tùy chọn. Xem [cấu hình](../../guides/configuration.vi.md).

Nguồn regression: `ZeroTrustCredentialProviderTests`, `SecretStoreEndpointTests`, test credential-store, audit-chain và supply-chain trong `tests/DataGuard.Core.Tests/`. Source/test mô tả ranh giới dự kiến; đợt sửa tài liệu này không gọi cloud, đọc credential hay chạy tích hợp secret-store Windows/macOS.
