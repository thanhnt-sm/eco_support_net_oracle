# Thương hiệu StackAccord và tương thích

[English](branding.md) · [Chiến lược sản phẩm](strategy.vi.md) · [Nội dung Marketplace](marketplace-copy.vi.md)

StackAccord là tên sản phẩm đã chọn. Dùng tên này trong tiêu đề, giới thiệu sản phẩm hiện hành, biểu mẫu issue, mô tả phát hành và nhãn sản phẩm SBOM. Tên hiển thị Visual Studio là **StackAccord – .NET Database Contracts (Beta)**; menu Tools và Output pane dùng **StackAccord (Beta)**. Nhãn hiển thị VS Code dùng **StackAccord**. Đây là quy ước tên hiển thị cho source; nghiệm thu editor đã cài và nâng cấp phải thực hiện trên đúng artifact đóng gói.

## Tên kỹ thuật được giữ

| Bề mặt | ID được giữ | Lý do |
|---|---|---|
| CLI và package | `dataguard`, `DataGuard.Cli`, `DataGuard.*`, `DataGuard.sln` | Giữ lệnh, dependency, assembly và entrypoint build hiện có |
| Cấu hình và biến môi trường | `.dataguard.yml`, `.dataguard-snapshot.json`, `.dataguard-baseline.json`, `DATAGUARD_*` | Giữ tương thích project, snapshot, baseline và automation |
| Identity IDE | `DataGuard.VisualStudio`, `thanhnt-sm.dataguard-vscode`, publisher `thanhnt-sm` | Nâng cấp cùng extension, không tạo bản cài thứ hai |
| Settings và lưu trữ IDE | `dataguard.*`, `%APPDATA%\DataGuard\logs` | Giữ settings, consent và đường dẫn lưu hiện có |
| Protocol và diagnostic | `DG*`, rule ID theo provider, `dataguard/v2`, `// DataGuard:` | Giữ consumer diagnostic, fingerprint baseline và marker comment do code fix sinh |
| Phân phối và repository | `dataguard-*.zip`, `dataguard-vscode-*.vsix`, `DataGuard.VisualStudio-*.vsix`, `thanhnt-sm/eco_support_net_oracle` | Giữ bộ chọn artifact, checksum, link và script phát hành |

Đường settings hiển thị là **Tools > Options > StackAccord**. ID category Options/automation vẫn là `DataGuard`; resource `110` đổi nhãn hiển thị, còn settings/automation hiện có tiếp tục dùng ID canonical.

Đây là ID tương thích có chủ đích. Không thay StackAccord vào các tên này chỉ để đổi hình thức. Nếu đổi ID sau này, cần thiết kế migration và bằng chứng nâng cấp riêng. Slug repository dự kiến trong chiến lược vẫn là đề xuất riêng.

## Tài liệu lịch sử và pháp lý

Giữ tên gốc trong `CHANGELOG.md`, journal theo ngày, bằng chứng discovery/lần chạy, ADR, quyết định theo ngày và thông báo pháp lý. Tài liệu đã gắn nhãn lịch sử hoặc archived cũng giữ nội dung cũ. Những tên này phản ánh đề xuất, bản phát hành hoặc quan sát tại thời điểm ghi lại, không chứng minh khả dụng hay nghiệm thu hiện tại. Tài liệu hiện hành có đánh số và nội dung sản phẩm ở root dùng StackAccord. Điều khoản và lịch sử giấy phép giữ nguyên.

## Kiểm tra hồi quy

Chạy từ root repository:

```bash
python3 scripts/check_product_branding.py
python3 -m unittest discover -s scripts/tests -p 'test_product_branding.py'
./scripts/verify_docs_sync.sh
```

Audit kiểm tra nội dung sản phẩm ở root, Markdown trong `docs/`, Markdown/YAML trong `.github/` và `packaging/` nếu có, gồm nhãn Mermaid và nhãn UI trong inline code. Ví dụ kỹ thuật trong code block, URL và đích link được giữ. Ngoại lệ hẹp cho lời giải thích ID canonical Options/automation, identity kỹ thuật, marker comment và bằng chứng DLL theo ngày đều có lý do trong script; ngoại lệ không che nội dung khác trên cùng dòng. Đường Options hiển thị được kiểm tra trước ngoại lệ, gồm dạng Markdown, inline code, mũi tên toán học và HTML. Test kiểm tra cả lỗi thương hiệu cần chặn lẫn tên tương thích hợp lệ, cùng lệnh, settings, fingerprint, tên artifact và identity extension thực tế. CI hiện có tự khám phá unittest; không thay gate workflow.

Baseline của lane này là `872974c5f333ef97f142b3bf55c2fe3202816b09`. Phạm vi gồm nội dung sản phẩm ở root, tài liệu và nhãn phát hành. Source và README đóng gói trong `src/` hoặc `extensions/` thuộc lane IDE/runtime, nằm ngoài audit này. Audit đạt không chứng minh UI đã cài hoàn toàn đồng bộ, nâng cấp giữ ID/settings, nghiệm thu host Windows/macOS, listing Marketplace bên ngoài, quyền thương hiệu hay phê duyệt xuất bản. Vẫn tạm dừng Marketplace.
