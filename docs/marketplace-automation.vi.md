# Workflow phát hành Marketplace thủ công

[English](marketplace-automation.md) · [Tải lên bằng trình duyệt, không cần PAT](marketplace-publishing.vi.md)

`.github/workflows/marketplace-release.yml` là workflow `workflow_dispatch` riêng, chỉ chạy khi người dùng chủ động chọn. Mặc định `publish=false` chỉ kiểm thử và đóng gói; chỉ chấp nhận dispatch từ `main` của repository này. `marketplace.yml` vẫn chỉ test/package, helper vẫn tạm dừng Marketplace và `installers.yml` giữ hành vi nightly đã ghi nhận. Test cục bộ thành công chưa chứng minh sẵn sàng phát hành: thay đổi này chưa chạy hosted preparation, công cụ Windows, editor thật, quyền environment hoặc xác thực publisher.

SA-DOC-015 đối chiếu workflow/helper tại baseline `ca68a3159425495f771da866188df75a8237ab4b`; không truy vấn listing, credential hoặc trạng thái hosted run. ID DataGuard và lịch sử license giữ nguyên; thương hiệu đề xuất xem [chiến lược](product/strategy.md).

## Điều kiện trước khi chạy và điểm chặn hiện tại

Có thể chuẩn bị trước khi metadata phát hành sẵn sàng. Với `publish=false`, để trống `release_tag` thì workflow dùng `source_sha` bất biến và version thật trong source. Version 0.3.0 và engine VS Code chưa khớp được ghi rõ là điểm chặn công bố trong summary; không coi là đã tương thích hoặc sẵn sàng phát hành. Vẫn bắt buộc identity/version trong gói khớp và mọi test/kiểm tra đóng gói thành công. Công bố vẫn cần tag ổn định từ 0.4.0 và engine phù hợp. Không tự sửa version, publisher, tag hay setting.

Công bố dùng lại environment `release` mà `release.yml` đã tham chiếu. Validation kiểm tra environment tồn tại trước khi job publisher đủ điều kiện; nếu thiếu hoặc không đọc được thì dừng, không tự tạo. GitHub thực thi các quy tắc deployment hiện có. Bản sửa không bắt buộc người thứ hai, ngăn tự review, chính sách admin bypass hoặc branch protection mới. Maintainer duy nhất tự chọn hash đã nghiệm thu, target và xác nhận. Reviewer độc lập là phương án tăng bảo vệ tùy chọn, không phải điều kiện mới. Không tắt bảo vệ hiện có để chạy được workflow. Xem [quy tắc environment của GitHub](https://docs.github.com/actions/using-jobs/using-environments-for-jobs).

## Chuẩn bị đúng byte sẽ phát hành

1. Trong Actions, chọn **Manual Marketplace Release**, **Run workflow**, nhánh **main**. Nhập `source_sha` là SHA commit main đã review đầy đủ 40 ký tự viết thường; để trống `release_tag` cho build-only hoặc nhập tag ổn định có sẵn tương ứng, `target` là `vscode`, `visualstudio` hoặc `both`. Giữ `publish=false`. Bước chuẩn bị luôn đóng gói cả hai extension.
2. Source phải trùng tag nếu có nhập tag, thuộc lịch sử main được chọn và khớp version, product ID, publisher ID. Cả test sản phẩm/Node trên Linux lẫn extension trên Windows phải thành công. Công cụ gồm .NET 9.0.319, Node 22.23.3, bộ công cụ VS Code theo lock và công cụ build Windows.
3. Lưu ID của lần chạy thành công ngay attempt đầu và đúng các tệp `package.vsix` trong `marketplace-release-vscode`, `marketplace-release-visualstudio`. Artifact hết hạn sau bảy ngày, chứa manifest, checksum, SBOM và attestation cho package/manifest. Artifact VS còn chứa `vs-publish.json`, `overview.md` từ source; hash của chúng được gắn vào manifest có attestation.
4. Cài và kiểm thử đúng byte này. Ghi source/tag/version, SHA-256, activation/LSP/quickfix VS Code, nghiệm thu editor/navigation Visual Studio 18.10.2, quyền publisher/Publisher Agreement, listing và license. Ghi bằng chứng vào issue của repository hoặc URL blob gắn với commit bất biến. URL là bằng chứng do người phụ trách cung cấp: kiểm tra cú pháp URL không xác nhận việc test editor hay phê duyệt đã diễn ra.

Chuẩn bị thành công là kết quả đóng gói, chưa phải quyền công bố. Review package và tệp listing VS thực tế. Tải lên bằng trình duyệt vẫn là đường riêng cần owner cho phép.

## Đường đi cụ thể từ source hiện tại tới nghiệm thu

1. Sau khi workflow được review và có trên main, chọn SHA source đầy đủ, để trống `release_tag`, giữ `publish=false`, chọn `target=both`. Không cần PAT, environment mới, tag, release hay gửi Marketplace cho bước chuẩn bị. Cả hai gói giữ version 0.3.0 của source. Lỗi test/công cụ đóng gói vẫn chặn run; cảnh báo không chứng minh engine tương thích.
2. Tải artifact chỉ để kiểm tra build/cài đặt. Ghi rõ cảnh báo version/license và engine. Chưa khẳng định hosted build trước khi run thành công; vẫn cần nghiệm thu Windows.
3. Trong thay đổi source riêng được owner cho phép, đồng bộ version/license và engine VS Code (hiện ^1.85.0, dependency khóa yêu cầu ^1.91.0). Chạy test và nghiệm thu editor thật. Patch này không sửa các giá trị đó.
4. Chỉ khi có chủ đích phát hành, dùng tag ổn định đã duyệt từ v0.4.0 và SHA chính xác để chuẩn bị lại. Nghiệm thu hash mới trước lần `publish=true` riêng. Không thể nâng artifact 0.3 chưa tag thành bản công bố bằng đổi tên hay sửa input.

Preparation chỉ tạo Actions artifact/attestation tạm thời. Đưa workflow lên main là thao tác repository riêng: `installers.yml` không đổi vẫn có thể công bố rolling nightly khi push main phù hợp. Vì vậy không gọi việc merge mã này là không có side effect release. Tác vụ cục bộ này không merge hoặc dispatch.

## Chỉ công bố artifact đã nghiệm thu

Người phụ trách dispatch lần hai từ **main**, cùng tag/SHA và target đã chọn. Đặt `publish=true`, `prepared_run_id` đúng lần chuẩn bị đã nghiệm thu; điền `vscode_sha256`/`visualstudio_sha256` cho từng target được chọn. Cung cấp `readiness_url` và xác nhận chính xác `PUBLISH <release_tag> <target>`. Nếu environment hiện có yêu cầu phê duyệt, tuân thủ đúng quy tắc đó sau khi xem bằng chứng; workflow không yêu cầu reviewer bổ sung.

Lần chuẩn bị phải thành công, attempt đầu, đúng repository và đúng SHA main của workflow hiện tại. Nếu main đổi hoặc artifact hết hạn, chuẩn bị và nghiệm thu lại; không thay byte đã duyệt bằng bản rebuild. Tải xuống dùng artifact ID được kiểm tra từ run, không nhận URL tùy ý. Kiểm tra gồm identity/version/payload trong gói, source/run trong manifest, hash người dùng chấp nhận và attestation GitHub gắn với SHA workflow và hosted runner. Lần công bố chạy lại test nhưng không rebuild package.

Chỉ bước publisher cuối nhận secret của target. Cài dependency không nhận secret công bố; job VS Code tắt install scripts. VS Code dùng `vsce` theo lock; Visual Studio dùng `Microsoft.VSSDK.BuildTools` 18.5.40034 từ nuget.org. Cả hai job phụ thuộc validation và test Linux/Windows. `both` không phải giao dịch nguyên tử: một bên có thể thành công, bên kia thất bại. Kiểm tra kết quả riêng và chỉ thử lại target thất bại với hash/xác nhận/provenance còn hợp lệ; không công bố lại bên đã thành công chỉ để làm xanh toàn bộ run.

## Xác thực và chi phí

`VSCE_PAT`, `VS_MARKETPLACE_PAT` chỉ là tên secret, không chứng minh credential đã tồn tại. Cầu nối PAT tạm thời từ chối công bố từ **2026-12-01**, chờ review xác thực được hỗ trợ. Đăng nhập trình duyệt không xác thực runner. VS Code có hướng dẫn Entra qua `vsce --azure-credential`; workflow này không cấu hình identity hay dùng `--oidc` thử nghiệm. Giao diện VsixPublisher chính thức dùng `-personalAccessToken`, không chứng minh có phương án Entra tương đương. Công cụ bắt buộc nhận PAT qua đối số tiến trình: không bật trace hoặc in token; masking không biến đối số thành kho bí mật.

Nguồn: [VS Code publishing](https://code.visualstudio.com/api/working-with-extensions/publishing-extension), [Visual Studio publishing](https://learn.microsoft.com/en-us/visualstudio/extensibility/walkthrough-publishing-a-visual-studio-extension?view=visualstudio), [ngừng global PAT](https://devblogs.microsoft.com/devops/retirement-of-global-personal-access-tokens-in-azure-devops/). Owner cần cấu hình và review kế hoạch chuyển đổi; không tạo credential rộng hoặc giảm bảo vệ để vượt điểm chặn.

GitHub có chính sách miễn phí standard hosted runner cho repository công khai, nhưng lưu trữ artifact/cache và các loại sử dụng khác vẫn có hạn mức/quy tắc tính phí. Retention bảy ngày giảm lưu trữ, không đảm bảo mọi cách dùng đều miễn phí. Xem [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions) theo tài khoản owner.

## Ánh xạ nguồn, test và nghiệm thu

Workflow dùng `scripts/marketplace_release.py` để kiểm tra và dừng khi không đủ điều kiện. `scripts/tests/test_marketplace_release.py` dùng fixture giả lập cho input sai, tráo provenance, source đổi, identity/version sai, ZIP nhập nhằng, thay listing, environment, target và mặc định không công bố. `test_marketplace_docs.py` bảo vệ hướng dẫn trình duyệt và workflow hiện có; test policy yêu cầu job công bố phụ thuộc test sản phẩm. Xem [kế hoạch](../plans/2026-10-18-manual-marketplace-release.md). Actionlint/policy/test/docs cục bộ không thay thế lần hosted preparation không công bố được owner cho phép và nghiệm thu trên editor thật.

GitHub có thể bỏ hẳn trường tùy chọn trống khỏi JSON input. Verifier chuẩn hóa `release_tag`, `prepared_run_id`, hai hash package, `readiness_url` và `confirm` bị thiếu thành chuỗi rỗng ở mọi entrypoint; giá trị được truyền rõ nhưng không phải chuỗi sẽ bị từ chối. Vì vậy bước record build-only xử lý trường bị bỏ và chuỗi rỗng như nhau; công bố vẫn cần đủ giá trị cho target đã chọn và không thể nâng manifest chưa tag thành bản phát hành.
