> **Định hướng sản phẩm (2026-10-18):** [StackAccord](product/strategy.md) là thương hiệu đề xuất; ID kỹ thuật hiện tại giữ nguyên. [Roadmap](product/roadmap.md) và [bản nháp Marketplace](product/marketplace-copy.md) phân biệt triển khai, kiểm chứng, phát hành và kế hoạch. Các hướng dẫn bên dưới được đối chiếu source trong SA-DOC-015; đây không phải bằng chứng đã công bố. Workflow Marketplace cũ vẫn tạm dừng; đường thủ công riêng cần gate nghiệm thu.

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
  Manifest hiện là **0.4.0**, khớp mốc GPL đã ghi; các bản lịch sử đến v0.3.0 giữ điều khoản MIT.
  Phải đối chiếu version/license trong gói thật trước công bố; tên file hoặc hậu tố nightly không sửa metadata.
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
server dùng .NET 9 qua `dotnet`. Engine đã khớp languageclient khóa ở ^1.91.0; vẫn phải nghiệm thu host đã cài
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

Workflow `marketplace-release.yml` có [quy trình hai bước prepare/publish riêng](marketplace-automation.vi.md), mặc định không công bố. `marketplace.yml` và release helper vẫn tạm dừng công bố Marketplace. Lần hosted preparation cụ thể ghi tại [bằng chứng chuẩn bị](product/release-claims.md) đã đạt. Nghiệm thu editor đã cài và xác minh xác thực/environment công bố có sẵn vẫn còn chờ; artifact tương lai phải có lần chuẩn bị được cho phép và nghiệm thu riêng. Đường trình duyệt không phụ thuộc việc khôi phục workflow cũ. Không chạy mã publish từ tag cũ hoặc giảm bảo vệ để vượt gate.

### Chuẩn bị Visual Studio 0.4.0

Manifest nguồn và fallback đăng ký Visual Studio hiện hướng tới 0.4.0, phù hợp mốc GPL đã ghi trong notice đóng gói. Các bản lịch sử đến v0.3.0 giữ điều khoản cũ; không sửa nội dung giấy phép. `scripts/assert-vsix.ps1` gọi `check-license-consistency.py --vsix <file> --expected-version 0.4.0` để kiểm phiên bản bên trong và so sánh từng byte giấy phép/notice với nguồn canonical. Tên tệp nightly không chứng minh phiên bản bên trong hay cờ prerelease.

Đây là chuẩn bị nguồn, chưa phải phê duyệt phát hành. Manifest hai extension, lockfile VS Code và fallback helper build đã đồng bộ 0.4.0 chỉ để giữ hợp đồng phát hành chung; mục tiêu công bố vẫn chỉ là Visual Studio. Engine trong package.json và lockfile đã khớp mức tối thiểu ^1.91.0 của vscode-languageclient 10.1.2 bị khóa. Đây là sửa hợp đồng nguồn, không khẳng định nghiệm thu VS Code thật. Baseline đã có bản sửa PR73 cho optional input bị bỏ trống; bản sửa readiness này giữ nguyên chuẩn hóa đó và các kiểm tra công bố. Cần build nguồn phát hành bất biến đã được duyệt trên Windows, kiểm catalog, đăng ký, informational version của extension/analyzer/CLI, chạy test đóng gói và ghi nhận cài đặt thật trên host Visual Studio đích trước khi chấp nhận hash VSIX. Nếu không override rõ ràng, MinVer suy phiên bản phát triển từ lịch sử Git; không giả định phiên bản đó khớp version VSIX. Preparation thủ công nay truyền PACKAGE_VERSION đã kiểm tra qua MinVerVersionOverride tới build VS và tiến trình publish CLI riêng; build analyzer con kế thừa giá trị này. RequireReleaseIdentity đối chiếu file/product version của DLL/CLI thật trong VSIX, giữ binding identity major.0.0.0 của MinVer 8. Build local có override đã tạo metadata 0.4.0; không tạo tag. Đóng gói Windows, kiểm PE CLI và test từ chối PowerShell đã đạt trong run chuẩn bị `39176474395`, như ghi tại [bằng chứng phát hành](product/release-claims.md). Các kiểm tra này vẫn bắt buộc với từng artifact được chuẩn bị trong tương lai; nghiệm thu editor đã cài vẫn còn chờ. Helper build lấy tên artifact từ VS Code; tên tệp đã đồng bộ vẫn không chứng minh phiên bản assembly Visual Studio bên trong. Các bước kiểm này không cho phép tạo tag, dispatch workflow hay công bố Marketplace.

Kết quả build-only có giới hạn cho nguồn `121b05d` được ghi tại [bằng chứng chuẩn bị](product/release-claims.md). Cổng identity Windows đã đạt; chưa nghiệm thu editor đã cài hoặc phát hành Marketplace. Kết quả không phê duyệt artifact hay tag phát hành khác.
