# Golden Standard — Patterns đáng giá

> **Mục đích**: document hóa các kỹ thuật đã được chứng minh trong workspace DataGuard để tái sử dụng cho sản phẩm khác. Mỗi pattern: vấn đề → cách làm → verify.

## 1. SHA-pinned actions

**Vấn đề**: `uses: actions/checkout@v4` là mutable tag — supply-chain attack vector (action bị chiếm → tag trỏ sang commit độc hại).

**Cách làm**: pin full 40-hex commit SHA kèm comment version:
```yaml
- uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
```

**Verify**: `grep -E 'uses:' .github/workflows/*.yml | grep -vE '@[0-9a-f]{40}'` → rỗng.
Dependabot group `github-actions` tự raise PR bump SHA khi có version mới.

## 2. Permission tối thiểu mỗi workflow

**Vấn đề**: default `GITHUB_TOKEN` quá rộng; một workflow bị exploit đọc được secret scope cao hơn nhu cầu.

**Cách làm**: khai báo tường minh, mặc định từ chối:
```yaml
permissions: read-all          # top-level
# hoặc trong job cần ghi release:
permissions:
  contents: write              # chỉ job publish
```

**Verify**: `grep -L '^permissions:' .github/workflows/*.yml` → rỗng (mọi file khai báo).

## 3. NuGet Trusted Publishing (OIDC)

**Vấn đề**: API key dài hạn trên nuget.org hết hạn 01/11/2026 và là secret tĩnh dễ leak.

**Cách làm**: `NuGet/login@v1` lấy short-lived credential qua OIDC, không lưu key:
```yaml
- uses: nuget/login@<sha>
  with:
    user: ${{ secrets.NUGET_USER }}
- run: dotnet nuget push "*.nupkg" --source https://api.nuget.org/v3/index.json --api-key $NUGET_API_KEY
```
Fallback `NUGET_API_KEY` cho trường hợp OIDC chưa bật.

## 4. Exit-code contract

**Vấn đề**: CLI trả exit code tùy tiện khiến CI consumer và IDE extension không phân biệt được fail loại nào.

**Cách làm**: chốt contract 3 mức, tài liệu trong README + test assert thật:
| Code | Ý nghĩa |
|---|---|
| 0 | pass / informational |
| 1 | validation fail hoặc drift (khi `--fail-on-drift`) |
| 2 | config/usage error |

**Verify**: test invoke CLI thật với từng scenario, assert `Environment.ExitCode`.

## 5. Test đỏ→xanh cho bug fix

**Vấn đề**: fix bug không có test chứng minh → regression quay lại âm thầm.

**Cách làm**: trước khi fix, viết test fail đúng bug (chứng minh bug tồn tại); sau fix test phải xanh. Commit message ghi rõ test nào chứng minh fix nào.

**Verify**: git log — mỗi bugfix commit kèm test; revert fix → test đỏ.

## 6. Golden corpus exact-match

**Vấn đề**: test "≥1 diagnostic" để lọt false positive/negative; refactor im lặng đổi hành vi rule.

**Cách làm**: fixture JSON (input SQL/descriptor → expected diagnostics chính xác RuleId+message). Test so sánh **exact match**, cả `unexpectedErrors == 0`. Thêm case mới = thêm fixture, không sửa assertion cũ.

**Verify**: suite golden-corpus chạy standalone (`DataGuard.GoldenCorpus.Tests`).

## 7. Zero-egress telemetry

**Vấn đề**: tool dev gửi metric về server = rò rỉ metadata codebase doanh nghiệp; ngân hàng yêu cầu zero network egress.

**Cách làm**:
- Default `Enabled=false`, `ExportEndpoint=null` — không tạo HttpClient khi disabled (test chốt).
- Khi bật explicit: allowlist scheme+host (HTTPS, loopback), reject plain HTTP remote/invalid URI.
- Circuit breaker: sau N lỗi export liên tiếp (DataGuard dùng 3) ngừng export, reset khi thành công — tránh retry storm.

**Verify**: unit test "zero HttpClient when disabled", "allowlist accepts/rejects", "circuit breaker stops/resets".

## 8. Credential zero-trust

**Vấn đề**: connection string nằm trong config file plaintext = leak vào repo/log/screenshot.

**Cách làm**:
- Precedence: env var > secret manager (Key Vault/Vault/AWS) > config file.
- Config file plaintext bị chặn mặc định (`AllowPlaintextConfigFallback=false`) — flag dev-only, error message hướng dẫn đúng.
- `config show` redact secret; audit log hash-chain tamper-evident, không chứa secret.

**Verify**: test "plaintext fallback fails closed", test redaction output.

## 9. Deterministic schema hash

**Vấn đề**: hash phụ thuộc thứ tự phần tử → cùng schema sinh hash khác nhau → drift false positive.

**Cách làm**: sort keys (Ordinal) trước khi hash; hash **schema descriptor** (columns/types/nullability/length), không hash rule output.

**Verify**: test reordered input → same hash; DDL change → different hash.

## 10. Repo tự chứng minh chuẩn (standards-audit)

**Vấn đề**: checklist giấy tờ lạc hậu ngay sau khi người ta quên maintain.

**Cách làm**: CI workflow kiểm repo thân (`standards-audit.yml`): mọi artifact bắt buộc tồn tại, mọi action SHA-pinned, mọi workflow khai báo permissions. Fail build khi lệch chuẩn.

**Verify**: workflow chạy xanh trên main; xóa thử CODEOWNERS → audit đỏ.

## 11. Test isolation với env var (xUnit parallel)

**Vấn đề**: hai test class chạy song song, một class set env var mà không dọn → class kia đọc phải giá trị lạ → flaky ngẫu nhiên khó reproduce.

**Cách làm**:
- Class set env var: `[Collection("Sequential")]` + `IDisposable.Dispose()` clear biến.
- Class đọc env var nhạy cảm: constructor clear trước khi chạy.
- Chạy `dotnet test` 5 lần liên tiếp để xác nhận ổn định.

**Verify**: 5/5 runs green; không còn failure "Expected string to be X but found Y" ngẫu nhiên.

## 12. Warning-as-error từ ngày đầu

**Vấn đề**: warning debt tích lũy (~3200 warnings) đến mức không ai dám bật gate; CI step analyzer thành no-op.

**Cách làm**: `TreatWarningsAsErrors=true` toàn solution từ project đầu tiên; suppression (.editorconfig) phải kèm comment lý do + điều kiện gỡ. Debt cũ xử lý theo nhóm (CS1591 XML doc → SA1xxx format) trước khi bật.

**Verify**: `dotnet build DataGuard.sln` → `0 Warning(s)`; CI fail nếu warning mới xuất hiện.
