# Phát hành DataGuard trên Marketplace

[English](marketplace-publishing.md) · [Các kênh release khác](operations/release_guide.vi.md)

## Phạm vi và trạng thái hiện tại

Công bố qua workflow cũ `.github/workflows/marketplace.yml` vẫn tạm dừng; workflow này chỉ còn
`test`, `vscode-package`, `visual-studio-package`; không có job công bố hoặc input `publish`.
Vẫn giữ test, đóng gói, checksum, SBOM và attestation ngoài PR. `dg-release` từ chối
`--publish-marketplaces` và `PUBLISH_MARKETPLACES=true`; `--no-marketplaces` ghi đè cấu hình cũ.
Đăng nhập trình duyệt không bật workflow hay sửa xác thực GitHub CLI.

Công bố bằng trình duyệt theo ủy quyền owner là đường riêng, chỉ thực hiện sau các gate dưới đây.
Không cần PAT để upload thủ công qua portal Marketplace đã đăng nhập; không tạo `VSCE_PAT`,
`VS_MARKETPLACE_PAT`, token GitHub hay setting mới chỉ để dùng đường này. Hướng dẫn không phải
bằng chứng gói đã sẵn sàng hoặc đã công bố.

## Publisher và gate release

- Mở <https://marketplace.visualstudio.com/manage> bằng tài khoản Microsoft của owner; xác minh
  quyền quản lý publisher/listing. Quyền GitHub không chứng minh quyền Marketplace. ID cấu hình:
  publisher `thanhnt-sm`, VS Code `thanhnt-sm.dataguard-vscode`, VS `thanhnt-sm.DataGuard.VisualStudio`.
  Nếu thiếu quyền hoặc ID khác, dừng xử lý danh tính, không tự đổi ID.
- Publisher mới có thể hiện **Publisher Agreement**. Owner phải đọc và phê duyệt rõ việc chấp
  nhận điều khoản đang hiển thị. Đăng nhập hoặc yêu cầu hỗ trợ upload không tự cho phép trợ lý
  chấp nhận thỏa thuận pháp lý mới. Ghi nhận phê duyệt, không sao chép bí mật; không tạo publisher
  khác để né lỗi quyền.
- Chốt SHA nguồn sau merge và chọn version chưa dùng bằng cách kiểm tra lịch sử hai listing.
  Manifest hiện là **0.3.0**, nhưng tài liệu giữ MIT đến v0.3.0 và bắt đầu GPL từ **0.4.0**.
  Phải đối chiếu version/license trước công bố; tên file hoặc hậu tố nightly không sửa metadata.
- Ghi từng tên VSIX, publisher/ID/version bên trong, SHA nguồn, workflow run, **SHA-256**,
  checksum sidecar và provenance. Đối chiếu SBOM/dependency với nội dung, license/notices và
  kiểm tra config/bí mật ngoài ý muốn. Giữ nguyên byte đã nghiệm thu, không rebuild/đổi file
  trước upload. Kiểm tra link hỗ trợ/privacy và nội dung listing; lưu bằng chứng độc lập với
  URL nightly có thể đổi. Checksum xác định gói, không thay nghiệm thu sản phẩm.

## Nghiệm thu editor đã cài gói

Cài VSIX ứng viên thật. Trên Visual Studio **18.10.2**, ghi đầy đủ version IDE và SHA-256;
kiểm tra activation, version CLI đi kèm, IDE-safe, Error List, hủy và dọn process. Dùng fixture
lưu trên đĩa tại 1:1, 42:12, SQL nhiều dòng, Unicode, tab, CRLF/LF; đối chiếu SARIF, Error List
và vị trí caret khi double-click. Kiểm tra squiggle Roslyn riêng. CI đóng gói/unit test tọa độ
không thay nghiệm thu editor thật.

VS Code cần kiểm tra activation gói đã cài, runtime/LSP, range diagnostic, workspace trusted/
untrusted, CLI tương thích, timeout/deactivate. Gói chứa language server, không chứa CLI;
server dùng .NET 9 qua `dotnet`. Sửa riêng chênh lệch engine ^1.85.0 với languageclient ^1.91.0
trước khi công bố hỗ trợ. Thử upgrade từ version đã phát hành và uninstall/reinstall, bao gồm
setting/trust lưu lại và xóa credential tổng hợp; không dùng credential production.

## Upload bằng trình duyệt: Visual Studio

Chọn publisher đúng, **New extension → Visual Studio**, hoặc edit/upload listing hiện có.
Chọn VSIX đã duyệt, rà soát metadata. **Save & Upload** lưu gói VS ở bước staging;
**Make Public** là bước công khai được hướng dẫn. Kiểm tra preview/trạng thái hiện tại,
kể cả khi cập nhật listing công khai. Sau công bố, kiểm tra listing và cài thử.
Nguồn: [Visual Studio](https://learn.microsoft.com/en-us/visualstudio/extensibility/walkthrough-publishing-a-visual-studio-extension?view=visualstudio).

## Upload bằng trình duyệt: Visual Studio Code

Chọn **New extension → Visual Studio Code** hoặc cập nhật listing, rồi gửi VSIX đã duyệt.
Upload thủ công là lựa chọn thay CLI. Coi submission là hành động công bố; không giả định
VS Code có bước staging **Save & Upload → Make Public** giống VS. Kiểm tra validation,
visibility và listing sau submission; pending chưa phải thành công.
Nguồn: [VS Code](https://code.visualstudio.com/api/working-with-extensions/publishing-extension#publish-an-extension).

## Hỗ trợ thao tác máy tính

Trong phạm vi owner cho phép, trợ lý có thể xem trang đã đăng nhập, chuẩn bị metadata, kiểm tra
gói và điền form. Trước bước công khai/khó đảo ngược, trình bày đúng sản phẩm, publisher,
version, hash và thay đổi visibility; dùng ủy quyền đã có nếu bao gồm hành động cụ thể đó.
Không suy ra quyền chấp nhận thỏa thuận mới, tạo publisher/token hoặc thay quyền. Nếu bị chặn
bởi tài khoản, điều khoản hay validation, báo đúng trạng thái, không đi vòng. Patch tài liệu
này không thực hiện hành động Marketplace nào.

## Nightly là kênh công bố khác

`.github/workflows/installers.yml` chạy khi push main, trừ `paths-ignore` (`**/*.md`, `docs/**`,
`plans/**`), và khi dispatch. Push đủ điều kiện sẽ công bố GitHub prerelease **nightly** sau gate.
Dispatch mặc định `publish_prerelease=true`; chọn rõ `publish_prerelease=false` nếu chỉ cần
artifact. Đây không phải công bố Marketplace. Patch docs có thêm regression test không nhất
thiết được loại trừ như thay đổi chỉ Markdown.

Job publish xóa release nightly và tag cũ (`--cleanup-tag`), xóa tag nightly mồ côi nếu có,
rồi tạo lại tại commit hiện tại. Không thay tag `v*`. URL/tag nightly có thể đổi, không phải
provenance bất biến. Tên VSIX nightly có thể mang hậu tố prerelease nhưng version bên trong
vẫn là fallback đã commit; không coi đó là bản cập nhật Marketplace hợp lệ.

`build_release.yml` tạo bundle theo version mà không công bố Marketplace/NuGet/GitHub Release/
GHCR; ghi rõ revision dispatch và input. `dg-release` có thể tạo/push tag và kích hoạt release
NuGet/GitHub/GHCR; không dùng như điều kiện upload trình duyệt hoặc lối tắt chỉ phát hành extension.

## Workflow thủ công riêng

Workflow `marketplace-release.yml` có [quy trình hai bước prepare/publish riêng](marketplace-automation.vi.md), mặc định không công bố. `marketplace.yml` và release helper vẫn tạm dừng công bố Marketplace. Bản triển khai cục bộ còn cần hosted preparation được owner cho phép, nghiệm thu editor và xác minh xác thực/environment có sẵn. Đường trình duyệt không phụ thuộc việc khôi phục workflow cũ. Không chạy mã publish từ tag cũ hoặc giảm bảo vệ để vượt gate.
