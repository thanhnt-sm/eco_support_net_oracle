# ADR-20260930-dataguard-license-after-mit: DataGuard nên chuyển từ MIT sang giấy phép nào để ngăn sao chép source và cấm dùng thương mại?

Status: Decided
Date: 2026-09-30
Council: business domain, 4 lenses (legal-compliance-risk, finance, customer-market, operations) + research

## Question

DataGuard là repo GitHub công khai, hiện dùng MIT (0 star, 0 fork, đã phát hành tag v0.1.0 đến v0.3.0). Chủ sở hữu là Than Nguyen, cá nhân ở Việt Nam, làm một mình. Chủ sở hữu muốn (a) ngăn người khác sao chép và tái sử dụng source, (b) cấm dùng thương mại cả source lẫn sản phẩm, và yêu cầu giấy phép có mức bảo vệ tối đa. Ràng buộc: không đổi dependency; theo `CLAUDE.md`, thay đổi phải cập nhật tài liệu liên quan và chạy `scripts/verify_docs_sync.sh`. Một câu hỏi phụ là VSIX hiện "Digital Signature: None" khi cài. Chữ ký số không phụ thuộc vào giấy phép, nên được ghi thành một hạng mục riêng.

## Context

Chữ "MIT" xuất hiện ở nhiều nơi: `LICENSE` (bản quyền "Than Nguyen", `LICENSE:3`), `src/DataGuard.VisualStudio/LICENSE.txt`, `src/DataGuard.VSCode/LICENSE`, sáu file csproj khai báo `PackageLicenseExpression>MIT`, các README, `grants/*` và `.github/copilot-instructions.md:5,9`. File `.github/copilot-instructions.md` còn tự cấp thêm quyền dùng code cho AI training và cho phép crawl theo MIT. Repo đã từng đổi giấy phép một lần: bỏ PolyForm Noncommercial và chuyển sang MIT (`CHANGELOG.md:58`).

Đây là lúc chi phí đổi giấy phép thấp nhất. Chưa có fork, chưa có star, và chưa có package nào trên nuget.org (NuGet key đang bị 403, số liệu đo được). Như vậy chưa có bản MIT nào nằm ở một kho mà chủ sở hữu không thể gỡ. Riêng các bản đã phát hành theo MIT thì mãi mãi là MIT với những ai đã nhận được chúng. Cả năm nguồn đầu vào đều đồng ý điểm này.

Lý do từng được đưa ra để giữ MIT là chương trình "Claude for Open Source" của Anthropic. Finance, customer-market và research đều ghi nhận trang chính thức chỉ nêu ngưỡng định lượng và không có điều kiện về giấy phép. DataGuard hiện chưa đạt ngưỡng nào, dù giữ MIT hay không.

## Options considered

| Option | Description |
|---|---|
| 1 | EULA độc quyền + repo private: source đóng, chỉ phát hành binary, cấm dùng thương mại nếu không có hợp đồng riêng |
| 2 | PolyForm Noncommercial 1.0.0 (source công khai) + bán giấy phép thương mại riêng (dual-license) |
| 3 | BUSL 1.1 với Additional Use Grant tự soạn (tự chuyển sang giấy phép mở sau tối đa 4 năm) |
| 4 | PolyForm Shield / FSL: chỉ cấm dùng để cạnh tranh; FSL tự chuyển sang MIT/Apache sau 2 năm |
| 5 | AGPL-3.0 + giấy phép thương mại (dual-license) |
| 6 | Giữ MIT (giữ điều kiện xin grant "Claude for Open Source" như `grants/*` đang viết) |

## Lens verdicts

| Lens | Verdict | Confidence |
|---|---|---|
| legal-compliance-risk | Option 1. Chỉ Option 1 đáp ứng đủ (a), (b) và điều kiện "không tự chuyển sang giấy phép mở". Thứ hạng 1 > 2 > 3 > 4 > 5 > 6. Kết quả được đánh dấu có thể là bản tạm, nhưng nội dung đầy đủ. | 0.70 |
| finance | Option 6. Chủ động giữ MIT, thêm CLA/DCO, chỉ đổi giấy phép cho các bản sau khi có trigger (có khách thương mại hỏi mua, đạt 500 star, có lượt tải NuGet). Lens tự thừa nhận không định giá được mục tiêu bảo vệ IP của chủ sở hữu. | 0.68 |
| customer-market | Option 2. Người mua là doanh nghiệp, nên cần có giấy phép thương mại để bán. Tiền lệ EPPlus. Thứ hạng 2 > 3 > 1 > 4 > 5 > 6. | 0.65 |
| operations | Option 2. Trong các option đáp ứng mục tiêu, đây là option rẻ nhất để vận hành (khoảng 30 file, nửa ngày sửa text). Option 1 tốn kém nhất (cần nơi phân phối thứ hai, mất Scorecard, Actions tính phí). | 0.70 |
| research (researcher) | Option 2 cho các phiên bản tương lai; chọn Option 1 nếu yêu cầu ngăn sao chép là bắt buộc. Không giấy phép nào của repo public ngăn được việc sao chép (GitHub ToS D.5). | 0.60 |

## Evidence

| Claim | Source | Date | Type | Supports |
|---|---|---|---|---|
| Repo public cấp cho người dùng GitHub quyền xem và fork (giấy phép không độc quyền để tái tạo bằng fork), bất kể file LICENSE ghi gì | docs.github.com — GitHub Terms of Service D.5 (legal, research) | 2026-09-30 | official | Chỉ Option 1 đáp ứng được (a) |
| Chuyển repo sang private không xóa được các fork hay bản sao đã có | docs.github.com (operations) | 2026-09-30 | official | Giới hạn của Option 1; nên làm sớm |
| Gỡ theo DMCA: GitHub không tự vô hiệu hóa fork, người khiếu nại phải liệt kê từng fork và ký cam kết (penalty of perjury) | docs.github.com DMCA policy (legal, operations) | 2026-09-30 | official | Cưỡng chế tốn kém với mọi option |
| PolyForm NC: "Any noncommercial purpose is a permitted purpose". Không định nghĩa "commercial"; tổ chức chính phủ và giáo dục được dùng; có 32 ngày khắc phục vi phạm; không có ngày tự chuyển đổi | polyformproject.org/licenses/noncommercial/1.0.0; github.com/polyformproject/polyform-licenses (legal, research) | 2026-09-30 | official | Option 2 không đáp ứng (a); đáp ứng (b) một phần |
| Tác giả PolyForm thừa nhận khái niệm "noncommercial" còn mơ hồ | writing.kemitchell.com/series/polyform (research) | undated | independent | Rủi ro của Option 2 |
| BUSL chuyển sang Change License vào Change Date hoặc ngày kỷ niệm năm thứ 4, tùy mốc nào đến trước; licensor không được sửa phần còn lại của văn bản | mariadb.com/bsl11 (legal) | 2026-09-30 | official | Option 3 thất bại trước yêu cầu bảo vệ lâu dài |
| FSL cho phép mọi mục đích trừ Competing Use; tự động cấp MIT không thể thu hồi vào ngày kỷ niệm năm thứ 2. Shield cho phép mọi mục đích trừ cạnh tranh | fsl.software/FSL-1.1-MIT.template.md; polyformproject.org/licenses/shield/1.0.0 (legal) | 2026-09-30 | official | Option 4 không đáp ứng (b) |
| Tiêu chí OSD số 6 cấm hạn chế việc dùng cho kinh doanh, nên AGPL không cấm được dùng thương mại | opensource.org/osd (legal) | 2026-09-30 | official | Option 5 không đáp ứng (b) |
| MIT cấp quyền cho bất kỳ ai, không có điều khoản thu hồi | opensource.org/license/mit (legal) | 2026-09-30 | official | Bản ≤v0.3.0 vẫn là MIT vĩnh viễn |
| Bản cũ vẫn giữ giấy phép cũ sau khi đổi giấy phép (FluentAssertions <8 vẫn là Apache) | opensource.guide/legal; HashiCorp license FAQ; aaronstannard.com/relicense-or-die (research) | undated | independent | Giới hạn chung của mọi option 1–5 |
| nuget.org không xóa vĩnh viễn package; phiên bản unlisted vẫn tải được nếu biết đúng version | learn.microsoft.com/nuget/nuget-org/policies/deleting-packages (legal) | 2025-10-31 | official | Nên đổi giấy phép trước lần publish NuGet đầu tiên |
| nuget.org chỉ chấp nhận license expression được OSI/FSF phê duyệt; giấy phép tùy chỉnh phải đóng gói thành file | learn.microsoft.com/en-us/nuget/reference/nuspec (legal, customer-market, operations, research) | 2026-06-11 | official | Options 1–4 cần `PackageLicenseFile` |
| PolyForm-NC, BUSL-1.1 và FSL-1.1 có SPDX ID nhưng không được OSI/FSF phê duyệt; Shield không có SPDX ID; AGPL được OSI phê duyệt | spdx.org/licenses v3.29.0 (legal, operations) | 2026-09-16 | official | Chi phí vận hành của Options 1–4 |
| USCO Copyright & AI Part 2: chỉ được bảo hộ khi con người kiểm soát phần biểu đạt | copyright.gov (legal) | 2025-01-29 | official | Rủi ro cưỡng chế cho mọi giấy phép hạn chế |
| 31/327 commit có trailer `Co-Authored-By: Claude`; tác giả: thannt 170, thanhnt-sm 146, dependabot 11 | git shortlog (legal, finance) | 2026-09-30 | measured | Cần xác minh quyền tác giả trước khi đổi giấy phép |
| Repo có 0 fork, 0 star; DataGuard không có trên nuget.org (totalHits 0, registration 404); NuGet key 403 | gh repo view; NuGet search API; plans/ACTIVE_SESSION_REGISTER.md @9f183e7 (finance, customer-market, operations) | 2026-09-30 | measured | Thời điểm: chi phí đổi giấy phép đang thấp nhất |
| `LICENSE` hiện là MIT, "Copyright (c) 2026 Than Nguyen" | LICENSE:1,3 (adjudicator đọc) | 2026-09-30 | measured | Điểm xuất phát; tên chủ bản quyền |
| Repo đã từng đổi PolyForm Noncommercial sang MIT | CHANGELOG.md:58 (adjudicator đọc; operations) | undated | measured | Rủi ro đổi đi đổi lại; Option 2 từng được dùng rồi bỏ |
| Sáu csproj khai báo `PackageLicenseExpression>MIT` (Core, Cli, SqlServer, Oracle, MySql, PostgreSql) | src/DataGuard.Core/DataGuard.Core.csproj:8; src/DataGuard.Cli/DataGuard.Cli.csproj:8; src/DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj:7; src/DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj:7; src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj:7; src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj:7 (adjudicator grep) | 2026-09-30 | measured | Phạm vi thay đổi |
| Bảy project packable chưa có metadata giấy phép; không có thuộc tính giấy phép chung | Directory.Build.props:25-34 (operations) | 2026-09-30 | measured | Phạm vi thay đổi |
| `.github/copilot-instructions.md` cấp quyền dùng cho AI training và crawl theo MIT | .github/copilot-instructions.md:5,9 (adjudicator đọc; operations) | 2026-09-30 | measured | Phải sửa khi đổi giấy phép |
| CI yêu cầu có file tên đúng `LICENSE`; `verify_docs_sync.sh` yêu cầu 4 file `grants/*.md` phải tồn tại | .github/workflows/standards-audit.yml:36; scripts/verify_docs_sync.sh:44-47 (adjudicator đọc; operations) | 2026-09-30 | measured | Sửa nội dung, không đổi tên hay xóa file |
| Các file grant ghi MIT | grants/ecosystem_impact_matrix.md:10; grants/written_explanation.md:38; grants/grant_pitch.md:31; grants/SUBMISSION_CHECKLIST.md:12,26 (adjudicator grep; legal) | 2026-09-30 | measured | Phạm vi thay đổi |
| Gate giấy phép trong CI chỉ đọc dependency, không kiểm tra giấy phép của chính DataGuard | scripts/check-nuget-licences.py:117,144 (operations) | 2026-09-30 | measured | Đổi giấy phép không làm vỡ gate |
| Manifest marketplace trỏ tới file giấy phép | src/DataGuard.VSCode/package.json:7; source.extension.vsixmanifest:8 (legal, operations) | 2026-09-30 | measured | Thay đổi marketplace ít |
| Claude for OSS: 6 tháng Claude Max 20x; ngưỡng 500+ repo phụ thuộc / 100+ package / 200K+ lượt tải/tháng / 20+ contributor ngoài / OpenSSF ≥0.4; không có điều kiện giấy phép | claude.com/contact-sales/claude-for-oss (finance, customer-market, research) | 2026-09-30 | official | Grant không phải lý do để giữ MIT |
| Repo private: 2,000 phút Actions/tháng miễn phí, sau đó tính phí; code scanning trên repo private cần license GitHub Code Security | docs.github.com billing / code security (finance) | 2026-09-30 | official | Chi phí vận hành Option 1 |
| Google cấm dùng giấy phép noncommercial, BUSL và AGPL | opensource.google/documentation/reference/thirdparty/licenses (customer-market) | 2026-09-30 | official | Rào cản tiếp cận doanh nghiệp của Options 2, 3, 5 (chỉ một công ty) |
| EPPlus chuyển LGPL sang PolyForm NC + bản trả phí; 211.1M lượt tải; package ship dạng `license type="file"` | nuget.org/packages/EPPlus; epplussoftware.com (customer-market, research) | 2026-09-30 | measured | Option 2 khả thi (có điều kiện) |
| HashiCorp sau khi đổi sang BUSL: tỷ lệ PR cộng đồng giảm 21.12% xuống 9.30%, xuất hiện fork OpenTofu | spacelift.io (finance) | undated | independent | Rủi ro fork/phản ứng khi đổi giấy phép |
| FluentAssertions 8 đổi Apache sang trả phí, bị phản đối, xuất hiện fork AwesomeAssertions | infoq.com (customer-market, research) | 2025-01 | independent | Rủi ro phản ứng; mức độ thấp với 0 star |
| Ký VSIX là tùy chọn; VS Marketplace không nhận chứng chỉ tự ký | learn.microsoft.com signing-vsix-packages (research) | 2026-04-24 | official | Hạng mục chữ ký số riêng |
| Azure Artifact Signing: cá nhân phải ở US/Canada; tổ chức chỉ thuộc danh sách quốc gia không có Việt Nam | learn.microsoft.com/en-us/azure/artifact-signing/quickstart (research, operations) | 2026-09-29 | official | Hạng mục chữ ký số: phải đi đường chứng chỉ OV/IV |
| Chứng chỉ OV khoảng $219/năm; từ 2023-06-01 bắt buộc token phần cứng/HSM; cá nhân không mua được EV | SSL Dragon page; ssl.com (research) | undated | vendor | Hạng mục chữ ký số |
| VS Code Marketplace ký extension khi upload | github.com/microsoft/vscode-discussions/discussions/229 (research; nguồn yếu) | undated | independent | Hạng mục chữ ký số |

## Decision

**Option 1, triển khai theo giai đoạn, áp dụng từ phiên bản sau v0.3.0.** Từ v0.4.0 trở đi, source DataGuard chuyển vào repo private. Sản phẩm chỉ phát hành ở dạng binary (CLI, VSIX, NuGet) kèm một EULA độc quyền. EULA cho phép dùng cá nhân và phi thương mại, cấm dùng thương mại nếu không có giấy phép thương mại riêng bằng văn bản. EULA cũng cấm sửa đổi, phân phối lại và dịch ngược trong phạm vi luật cho phép. Theo mitigation của lens legal, nếu chưa kịp có luật sư duyệt thì tạm dùng văn bản PolyForm NC làm giấy phép cho binary. Vẫn giữ quyền bán giấy phép thương mại theo từng thỏa thuận riêng, nhưng không xây dựng kênh bán hàng lúc này (YAGNI). Giới hạn không thể vượt qua: mọi bản ≤v0.3.0 đã phát hành vẫn là MIT vĩnh viễn với người đã nhận, và ADR này không cố thu hồi chúng.

Có ba điều kiện bắt buộc trước khi đổi (G0–G2 trong checklist): (1) xác nhận thannt và thanhnt-sm là cùng một người, và không có contributor nào khác đóng góp code có bản quyền; (2) kiểm kê mọi kênh phân phối công khai đang có (GitHub Releases, `nightly`, ghcr.io, VS/VS Code Marketplace); (3) có luật sư duyệt EULA trước bản phát hành độc quyền đầu tiên. Nếu chủ sở hữu thấy việc công khai source (uy tín, cho phép kiểm toán mã của công cụ bảo mật) quan trọng hơn yêu cầu (a), thì phương án dự phòng là **Option 2** (PolyForm NC 1.0.0 + giấy phép thương mại). Các bước sửa file giấy phép, csproj và tài liệu gần như giống nhau, chỉ khác bước chuyển repo sang private.

## Rationale

**Tiêu chí quyết định là mục tiêu (a)+(b) và "bảo vệ tối đa" do chủ sở hữu đặt ra, không phải doanh thu hay mức độ phổ biến.** Lọc qua yêu cầu (a) trước vì đây là ràng buộc chặt nhất:

- GitHub ToS D.5 (official) cho mọi người dùng GitHub quyền fork repo public, bất kể LICENSE ghi gì. Vì vậy Options 2–6 đều không đáp ứng (a) khi source còn public.
- Riêng Option 2 còn có văn bản PolyForm NC (official) cho phép sao chép và sửa đổi cho mọi mục đích phi thương mại.
- Với (b): BUSL tự chuyển sang giấy phép mở trong tối đa 4 năm, FSL tự chuyển sang MIT sau 2 năm, Shield chỉ cấm cạnh tranh (đều theo official text). AGPL không thể cấm dùng thương mại theo OSD số 6 (official). MIT thì không đáp ứng cả (a) lẫn (b).
- **Cấp tài liệu chính thức (official/primary docs) đã phá thế hòa** giữa Option 1 (legal) và Option 2 (customer-market, operations, research). Nó cũng phá thế hòa giữa Option 1 và Option 6 (finance). Research tự ghi "Option 1 first nếu ngăn sao chép là yêu cầu bắt buộc", và chủ sở hữu đã nêu đúng yêu cầu đó.

**Dữ liệu đo được quyết định thời điểm, không quyết định lựa chọn.** Repo có 0 fork, 0 star và chưa có gì trên nuget.org (measured). Đây là lúc chi phí và phần rò rỉ không thể thu hồi ở mức thấp nhất. Nếu publish NuGet dưới MIT rồi mới đổi thì các bản đó không bao giờ gỡ được (theo official policy của nuget.org). Vì vậy đề xuất "đổi sau khi có trigger" của finance không bảo vệ được những gì đã phát hành trước trigger. Chính lens finance đã tự nêu rủi ro này.

**Đánh giá confidence từng lens:**

- **finance (0.68 → Option 6):** confidence này không có cơ sở cho câu hỏi đang xét. Lens tự thừa nhận không định giá được mục tiêu IP, và xếp hạng nhất một option thất bại cả hai tiêu chí. Lens này trả lời câu hỏi "làm sao kiếm tiền", không phải câu hỏi được hỏi. Tuy vậy vẫn giữ các đóng góp đúng của lens: doanh thu đo được hiện là $0 cho mọi option, giá trị kỳ vọng của grant ≈ $0, và chi phí cưỡng chế xuyên biên giới cao.
- **customer-market (0.65):** tự ghi nhận nhận định về allow-list chỉ dựa trên chính sách của một công ty (Google). EPPlus đã có nhiều năm dùng LGPL trước khi đổi, trong khi DataGuard bắt đầu từ con số 0.
- **operations (0.70):** bằng chứng tốt (nhiều file:line đo được). Lens đúng rằng Option 1 tốn kém nhất, nhưng cũng tự xếp Option 1 vào nhóm "meets goal". Lý do lens chọn Option 2 là chi phí, không phải mức đáp ứng mục tiêu.
- **legal (0.70):** được caller đánh dấu có thể là bản tạm, nhưng bằng chứng là official và đầy đủ nhất. Không kế thừa nguyên con số 0.70.

**Confidence của adjudicator: 0.70.** Việc chỉ Option 1 đáp ứng (a) đã được official docs xác lập với độ chắc chắn cao. Các yếu tố còn mở và làm giảm confidence: khả năng cưỡng chế thực tế (code có AI hỗ trợ theo USCO, kiện xuyên biên giới từ Việt Nam), rủi ro khi soạn EULA, chi phí vận hành Option 1 (nơi phân phối thứ hai, Actions, Code Security, mất Scorecard), và việc chủ sở hữu có thực sự coi (a) là bắt buộc hay không (repo đã từng bỏ PolyForm NC, `CHANGELOG.md:58`). Nếu (a) bị hạ xuống thành "mong muốn", Option 2 sẽ thắng vì chi phí vận hành thấp hơn. Đó là lý do Option 2 được ghi là phương án dự phòng kèm trigger cụ thể, thay vì bị bác bỏ hẳn.

## Risks & mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Các bản ≤v0.3.0 vẫn là MIT; bất kỳ ai đã tải đều có thể fork hoặc bán lại | High | Không có cách thu hồi. Làm nhanh để hạn chế lan rộng. Cân nhắc gỡ asset trên GitHub Releases/`nightly` (giảm khả năng bị tìm thấy, không thu hồi được quyền). Ghi rõ trong NOTICE rằng chỉ bản ≤v0.3.0 là MIT |
| Chưa xác minh quyền tác giả duy nhất (hai danh tính được cho là cùng một người; 11 commit của dependabot chưa kiểm tra từng cái) | High | G0: đối chiếu email của hai danh tính và xác nhận bằng văn bản; kiểm tra commit dependabot chỉ chạm lock/manifest |
| 31/327 commit có trailer Claude; theo USCO, phần do AI tạo có thể được bảo hộ yếu, làm giảm khả năng cưỡng chế. Quy định của Việt Nam về tác giả AI chưa được xác minh (UNVERIFIED) | Medium | Luật sư đánh giá ở G2; giữ lịch sử commit làm bằng chứng về quyền kiểm soát sáng tạo của con người |
| Soạn EULA tự chế có thể có lỗ hổng; quy định EU 2009/24/EC Art 6 có thể cho phép dịch ngược để tương thích (UNVERIFIED) | Medium | G2: dùng mẫu có luật sư duyệt; tạm dùng PolyForm NC cho binary nếu cần |
| Chưa kiểm kê kênh phân phối: ảnh ghcr.io (nhãn giấy phép chưa kiểm tra), listing VS/VS Code Marketplace, `nightly` pre-release. Nếu đã public thì đó là các bản MIT không thu hồi được | High | G1: kiểm kê và ghi danh sách vào CHANGELOG; đổi nhãn OCI `org.opencontainers.image.licenses` cho các bản mới |
| Chi phí vận hành repo private: Actions quá 2,000 phút/tháng bị tính phí; code scanning cần Code Security; Scorecard `publish_results` có thể không chạy (`.github/workflows/scorecard.yml:44`, UNVERIFIED) | Medium | Đo số phút Actions thực tế trong 1 tháng sau khi chuyển; tắt hoặc giảm các job không cần; tự dùng CodeQL CLI tại máy nếu không mua Code Security |
| Đường cài đặt công khai bị vỡ: GitHub Releases của repo private không truy cập được (CHANGELOG hướng dẫn cài từ GitHub Releases) | High | Tạo kênh phân phối binary công khai riêng (ví dụ repo public chỉ chứa release asset + EULA, hoặc marketplace) trước khi chuyển private |
| Mất tín hiệu tin cậy: người mua là ngân hàng/đơn vị bảo mật không kiểm toán được source; VSIX không ký từ publisher lạ (lập luận của customer-market, UNVERIFIED) | Medium | Hạng mục ký số (checklist); SBOM + provenance đã có; có thể cho khách thương mại xem source theo NDA |
| Đổi giấy phép lần thứ ba (PolyForm NC → MIT → EULA) làm giảm uy tín | Low | Cam kết giữ ADR này ít nhất đến khi gặp một reopen condition; ghi lịch sử giấy phép trong CHANGELOG |
| Nhận định "Claude for OSS không yêu cầu OSI" chỉ dựa trên tóm tắt của công cụ fetch (trang render bằng JS); có một đoạn trích nói ngưỡng 5,000+ star; ngày đóng đơn 2026-06-30 chưa được xác minh (UNVERIFIED) | Low | Không ảnh hưởng quyết định vì DataGuard chưa đạt ngưỡng nào. Cập nhật `grants/*` để không còn nói MIT |
| Khả năng cưỡng chế: kiện công ty nước ngoài từ Việt Nam có thể vượt khả năng tài chính của một cá nhân (UNVERIFIED); rủi ro này như nhau cho Options 1–5 | Medium | Không có mitigation đầy đủ. Option 1 giảm rủi ro vì không có source để sao chép |
| Chưa kiểm tra chính sách giấy phép của VS Marketplace Publisher Agreement; chưa kiểm tra liên kết giấy phép MS-PL | Low | Đọc Publisher Agreement trước khi publish VSIX theo EULA |
| `LICENSE:3` ghi "Than Nguyen" nhưng csproj ghi Authors "DataGuard Contributors" (`src/DataGuard.Analyzers/DataGuard.Analyzers.csproj:6`, theo operations) | Low | Thống nhất Authors/Copyright thành Than Nguyen |
| Ngưỡng "commercial" chưa rõ ràng (nhân viên dùng thử ở công ty, consultant, cơ quan chính phủ) | Medium | Định nghĩa rõ "commercial use" và "evaluation" trong EULA |

## Implementation checklist

- [ ] **G0 — Quyền tác giả:** xác nhận thannt và thanhnt-sm là một người (đối chiếu email trong `git log`); kiểm tra 11 commit dependabot chỉ sửa lock/manifest; ghi kết quả vào `plans/ACTIVE_SESSION_REGISTER.md`. Nếu có contributor thứ hai là người thật thì dừng và xin đồng ý bằng văn bản.
- [ ] **G1 — Kiểm kê kênh phân phối:** GitHub Releases v0.1.0–v0.3.0 và `nightly`, ảnh ghcr.io, VS Marketplace, VS Code Marketplace. Ghi danh sách các bản MIT không thu hồi được.
- [ ] **Giữ nguyên trạng NuGet:** không publish DataGuard lên nuget.org dưới MIT trong lúc chờ (các bản đã publish không bao giờ gỡ được).
- [ ] **G2 — EULA:** soạn EULA noncommercial độc quyền (định nghĩa commercial/evaluation, cấm sửa đổi/phân phối lại/dịch ngược trong phạm vi luật cho phép, có điều khoản giấy phép thương mại riêng) và nhờ luật sư duyệt. Nếu chưa có luật sư thì tạm dùng văn bản PolyForm NC cho binary.
- [ ] **Kênh phân phối thay thế:** chuẩn bị nơi phát hành binary công khai trước khi chuyển private, và cập nhật hướng dẫn cài đặt (README, `docs/USAGE.md`, CHANGELOG).
- [ ] **Sửa file giấy phép:** thay nội dung `LICENSE` (giữ tên file vì `standards-audit.yml:36`), `src/DataGuard.VisualStudio/LICENSE.txt`, `src/DataGuard.VSCode/LICENSE`; thêm NOTICE ghi "v0.1.0–v0.3.0 phát hành theo MIT".
- [ ] **NuGet metadata:** đặt `PackageLicenseFile` tập trung trong `Directory.Build.props`, đóng gói file giấy phép, xóa 6 dòng `PackageLicenseExpression>MIT`; thống nhất Authors/Copyright.
- [ ] **Tài liệu:** bỏ các khẳng định MIT trong `README.md`, `README.vi.md`, README của VS Code, `docs/PRODUCT.md`, `docs/architecture.md`, `grants/*` (sửa nội dung, không xóa file vì `verify_docs_sync.sh:44-47`); sửa `.github/copilot-instructions.md:5,9` để thu hồi quyền AI training/crawl cho các bản mới.
- [ ] **CHANGELOG + version:** ghi mục đổi giấy phép và tăng version lên v0.4.0; chạy `dotnet build DataGuard.sln --configuration Release` và `./scripts/verify_docs_sync.sh`.
- [ ] **Chuyển repo sang private** sau khi các bước trên đã merge; xử lý CI (Actions minutes, Scorecard, Code Security).
- [ ] **Phát hành v0.4.0** dưới EULA qua kênh binary mới.
- [ ] **Hạng mục phụ — chữ ký số VSIX (không phụ thuộc giấy phép):** Azure Artifact Signing không dùng được với cá nhân ở Việt Nam. Hướng khả thi là mua chứng chỉ code signing OV/IV từ CA công khai (Certum, Sectigo, DigiCert, GlobalSign, SSL.com; khoảng $219/năm theo trang vendor; bắt buộc token phần cứng/HSM; cá nhân không mua được EV) để ký VSIX cho Visual Studio. VS Code Marketplace ký extension khi upload (nguồn yếu, cần xác minh). Xác minh riêng việc Certum có cấp chứng chỉ open-source cho dự án không dùng giấy phép OSI hay không (UNVERIFIED).

## Reopen conditions

- Chủ sở hữu thấy source công khai (uy tín, kiểm toán) quan trọng hơn yêu cầu (a) → chuyển sang Option 2 (PolyForm NC 1.0.0 + giấy phép thương mại).
- Luật sư kết luận EULA không cưỡng chế được ở Việt Nam, hoặc code có AI hỗ trợ có bản quyền quá yếu → xem lại mức hạn chế (có thể không đổi giấy phép).
- G0 phát hiện một contributor thứ hai là người thật đã viết code có bản quyền → cần sự đồng ý của họ trước khi đổi.
- Có yêu cầu mua thương mại kèm giá cụ thể hoặc ý định mua bằng văn bản → chính thức hóa giấy phép thương mại và kênh bán.
- Nguồn chính thức của Anthropic xác nhận grant yêu cầu giấy phép OSI **và** DataGuard đạt ngưỡng → cân nhắc lại Option 5 hoặc 6.
- Chi phí vận hành repo private (Actions, Code Security) vượt ngân sách chủ sở hữu chấp nhận → xem lại Option 2.
- Chủ sở hữu chấp nhận nới mục tiêu thành "chỉ cấm đối thủ cạnh tranh" → FSL (Option 4).
- Có bằng chứng kênh ghcr.io hoặc marketplace đã phân phối rộng bản MIT → cân nhắc lại chi phí so với lợi ích của việc chuyển private.

## Owners

Than Nguyen (chủ sở hữu, người giữ bản quyền) chịu trách nhiệm cho G0–G2, việc chọn luật sư, chữ ký số, và theo dõi các reopen condition. Phần sửa file trong repo có thể giao cho executor sau khi G0–G2 đã xong.

## Council metadata

- Models used: 4 lenses opus/high, research (researcher), adjudicator opus/high
- Cost estimate: ≈$1–1.5
- Lenses run: legal-compliance-risk, finance, customer-market, operations (domain: business)
- Generated by: `/ck:council` on 2026-09-30
