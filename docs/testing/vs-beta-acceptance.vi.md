# Nghiệm thu bản ứng viên Visual Studio

[English](vs-beta-acceptance.md) · [Contract phát hành](../marketplace-automation.vi.md) · [Tóm tắt hỗ trợ](../operations/support-summary.vi.md)

Đây là checklist cho người vận hành, không phải bằng chứng đã cài hoặc quyền publish. Ghi pass/fail/unavailable, build/edition VS thực tế, SHA-256 gói, source SHA, run ID, thời gian và bằng chứng đã che dữ liệu cho từng ca. Chỉ dùng project tổng hợp dùng một lần ngoài repo người dùng; không dùng DB, secret, migration hoặc source production.

## Prepare → test → publish

Tái sử dụng `.github/workflows/marketplace-release.yml`, không tạo workflow publish trùng. Prepare mặc định `publish=false`, có gate, checksum/SBOM/provenance và artifact giữ 7 ngày. Nếu sau này được phép publish, chọn tag release số ổn định đã được duyệt (từ v0.4.0) khớp source. Validator từ chối `v0.4.0-beta.1`; hiện chưa có kênh beta private/prerelease riêng. Cần quyết định hình thức phân phối trước khi gọi đây là beta Marketplace.

Tải đúng artifact, kiểm tra hash, publisher/ID/version VSIX, identity DLL/CLI, license/notices và provenance. Giữ nguyên bytes đã nghiệm thu. Technical identity DataGuard không đổi; StackAccord là tên hiển thị sản phẩm; chưa chứng minh đã xuất bản hoặc nghiệm thu cài đặt. Publish yêu cầu main vẫn đúng SHA của prepare, lần chạy đầu thành công và đúng artifact ID/hash đã chấp nhận. Main thay đổi thì prepare và nghiệm thu lại; không rebuild gói đã duyệt.

Sau nghiệm thu host và quyền publish rõ ràng, dùng workflow hiện có với target `visualstudio`, `publish=true`, tag/source/prepared-run ID/hash VS chính xác, readiness evidence và chuỗi `PUBLISH <tag> visualstudio`. Owner xác minh environment/auth hiện có; tên secret trong source không chứng minh quyền truy cập. Không đưa credential vào chat/log hoặc tạo token. Validator hiện chặn publish từ 2026-12-01 để review authentication. URL readiness hợp lệ chỉ chứng minh cú pháp. Luồng Marketplace cũ và publish qua release-helper vẫn pause.

## Ca kiểm tra trên VS 18.10.2

1. Có quyền cài; ghi build host thực tế, không giả định version. Cài gói đúng hash, kiểm tra activation, CLI đi kèm và settings.
2. Validate fixture offline ở IDE-safe; đối chiếu Error List với CLI/SARIF; kiểm tra cancel, timeout, dọn process và UI responsive.
3. Thử 1:1, 42:12, SQL nhiều dòng, Unicode, tab, CRLF/LF; so tọa độ SARIF, Error List và caret khi double-click. Test squiggle Roslyn riêng.
4. DG019 dùng config Oracle tường minh. Grammar chưa hỗ trợ phải unevaluated (thường exit 3); allow-unevaluated không có nghĩa đã xác thực. Offline không chứng minh live Oracle; live cần tài nguyên dùng một lần được phép riêng.
5. Preview quickfix trong fixture, xem diff, undo và chạy lại; không sửa source thật khi chưa có quyền.
6. Upgrade từ version trước đã duyệt, giữ settings, uninstall/reinstall và kiểm tra analyzer trùng.
7. Preview support summary, cancel cửa sổ và save dialog, so file đúng bytes preview. Xác minh không có secret tổng hợp, SQL, đường dẫn thô hay message diagnostic; kiểm tra giới hạn/rotation log. Chỉ chuyển summary được duyệt, không tự upload ActivityLog/audit/source/crash dump.

## Blocker và backlog

Chưa release khi thiếu nghiệm thu host/navigation, identity/license gói bất biến, quyết định phân phối, quyền listing/publisher hoặc readiness approval. Ghi unavailable nếu không có Windows/editor. Grammar parity đầy đủ và tính năng enterprise/agent tương lai là backlog trừ khi lỗi trái với tính năng đang quảng bá. VS Code có nghiệm thu riêng nếu chỉ phát hành VS. Checklist không khẳng định đang có công cụ computer-use.

Đổi tên hiển thị Beta: menu/Output là **StackAccord (Beta)**; **Tools > Options > DataGuard**, command ID, CLI/config, diagnostic và tên file không đổi. Cần kiểm chứng giữ identity/settings trên đúng artifact Windows.

Unit test package trên Windows chạy `OutputPaneWriter` từ assembly production với JTF context no-op và service double cho VS Output. Test kiểm tra identity/tên pane, banner offline/live, redaction và dịch vụ pane vắng mặt/lỗi. Test không xác minh UI-thread affinity hay chạy host VS đã cài hay hộp thoại consent/export; vẫn cần nghiệm thu thủ công bên trên. Test đọc source để kiểm tra tên hiển thị không phải coverage runtime.
