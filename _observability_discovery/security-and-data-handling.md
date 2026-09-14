# Security and Data Handling

## Scan result

- **[CONFIRMED]** High-confidence secret-pattern scan trên source/config không phải test/docs không phát hiện concrete password, API key, bearer/JWT, cloud key, connection string credential, private key hoặc webhook token.
- **[CONFIRMED]** Secret-like inventory chỉ thấy `.env.example` và `.release.env.example` dạng template; giá trị credential không xuất hiện. `.gitignore:1-109` loại env/cert/key extensions.
- **[REDACTED]** Tên biến/mục tiêu credential trong example và CI được ghi nhận ở dạng role only; giá trị thật không được đưa vào hồ sơ.
- **[UNVERIFIED_EXTERNAL]** Chưa chạy vulnerability/CVE lookup hoặc restore-based package audit trong discovery; CI có job audit nhưng kết quả của run không nằm trong workspace snapshot.

## Credential resolution and egress

- **[CONFIRMED]** Priority chain là environment → Azure Key Vault → AWS Secrets Manager → HashiCorp Vault → local encrypted store → plaintext config chỉ khi `AllowPlaintextConfigFallback=true` (`src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs:100-183`). Configuration defaults tắt plaintext fallback và telemetry (`Models/Configuration.cs:6-44`).
- **[CONFIRMED]** Key Vault URI phải HTTPS và host suffix `vault.azure.net`; managed identity token lấy từ link-local IMDS HTTP endpoint rồi dùng Bearer header tới vault (`ZeroTrustCredentialProvider.cs:191-240`).
- **[CONFIRMED]** Vault branch yêu cầu HTTPS và token lấy từ environment, gửi per-request `X-Vault-Token`; AWS dùng SDK Secrets Manager (`ZeroTrustCredentialProvider.cs:243-300`).
- **[CONFIRMED]** Provider log/audit credential name/source/hash, không log giá trị; source code declaration nói credential được fetch just-in-time và cleared after use (`ZeroTrustCredentialProvider.cs:19-24,53-87,303-327`).
- **[INFERRED_MEDIUM]** Declaration “cleared after use” không tự chứng minh mọi managed string/token đã zeroized; review memory lifetime/heap dump policy cần owner/security xác nhận.
- **[CONFIRMED]** Telemetry egress chỉ HTTPS hoặc loopback HTTP và disabled mặc định (`TelemetryCollector.cs:185-218,361-397`). VS Code extension không tự gọi network; assessment remote advisory chỉ khi explicit allow-network/approved public packages (`src/DataGuard.Cli/Program.cs:1038-1173`; `src/DataGuard.Cli/OsvAdvisoryClient.cs:7-50`).

## Local secret stores and process boundaries

- **[CONFIRMED]** macOS backend gọi Security.framework Keychain, không dùng `security` CLI để tránh lộ password qua process inspection; byte buffers được clear (`SecretStores/MacOsKeychainSecretStore.cs:7-10,17-61,64-96`).
- **[CONFIRMED]** Linux backend gọi `/usr/bin/secret-tool` với `UseShellExecute=false`, truyền secret qua stdin, bounded stdout/stderr, 10s timeout và kill process tree khi timeout (`SecretStores/LinuxSecretServiceSecretStore.cs:9-16,17-31,34-49,74-116`).
- **[CONFIRMED]** Plugin admission reject symlink/reparse paths, requires manifest identity/host API/digest and signed provenance by default; native dependencies blocked (`PluginAdmission.cs:65-131,134-167`; `RulePluginManager.cs:77-117`).
- **[CONFIRMED]** Supply-chain verifier fail-closed without expected hash anchor, bounds anchor file, checks dependencies as unverified unless independent provenance exists, and performs tampering/debug-symbol checks (`SupplyChainVerifier.cs:24-95,98-161,164-189`).
- **[INFERRED_MEDIUM]** Supply-chain dependency checks deliberately return `Passed=false` for referenced assemblies until host supplies provenance; release readiness therefore depends on an external verifier/anchor not present in this workspace.

## Output/data minimization

- **[CONFIRMED]** SARIF output allow-lists property keys/types, redacts sensitive text, rejects paths outside source root or with sensitive path components, sanitizes again at sink and writes atomically (`DiagnosticEmitter.cs:16-22,133-201,204-333`).
- **[CONFIRMED]** VS Code process output is bounded to 16 KiB, redacted, and detailed CLI output is not shown to avoid credential disclosure; SARIF artifact URI must remain inside trusted workspace (`extension.ts:212-280`; `security.ts:3-67`).
- **[INFERRED_MEDIUM]** Different redaction implementations (DiagnosticEmitter, ContractEvidence, AuditLogger, VS Code) may diverge; central policy/test corpus is not evidenced.

## Findings requiring follow-up

| ID | Finding | Class | Impact | Evidence |
|---|---|---|---|---|
| SEC-OBS-01 | Dynamic telemetry properties/tags have no collector allow-list | INFERRED_MEDIUM | Potential PII/cardinality leakage when enabled | `TelemetryCollector.cs:68-125` |
| SEC-OBS-02 | CredentialManager direct append bypasses FileAuditLogger hash chain | CONFLICT | Audit integrity and correlation semantics may differ | `CredentialManager.cs:392-416`; `IAuditLogger.cs:192-213` |
| SEC-OBS-03 | Contract evidence redaction patterns narrower than SARIF sanitizer | INFERRED_MEDIUM | Alternate secret syntax may survive evidence output | `ContractEvidence.cs:79-85`; `DiagnosticEmitter.cs:265-285` |
| SEC-OBS-04 | No authenticated remote health surface | CONFIRMED | Cannot assume remote probe exposure is safe | `Host/Program.cs:3-10`; `HealthHostBinding.cs:1-15` |
| SEC-OBS-05 | Supply-chain dependency provenance is unverified by default | CONFIRMED | Verification can fail closed in deployment unless anchor/verifier supplied | `SupplyChainVerifier.cs:146-161`; `PluginAdmission.cs:109-121` |
