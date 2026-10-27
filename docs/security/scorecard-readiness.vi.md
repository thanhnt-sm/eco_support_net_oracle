> **Đối chiếu nguồn 2026-10-18:** workflow/script hiện có tại `ca68a3159425495f771da866188df75a8237ab4b` đã được đọc; đoạn nghiên cứu/run ngày 2026-10-16 bên dưới là bằng chứng lịch sử, không được truy vấn lại. Đã có implementation trong baseline không đồng nghĩa hosted run thành công hay alert đã xử lý.

# Chờ bằng chứng SAST cho Scorecard (2026-10-16)

Đây là đề xuất local, chưa xác nhận alert #52 đã được xử lý hoặc điểm hosted đã tăng. Không thay đổi branch protection, required checks, CodeQL hay trạng thái tạm dừng Marketplace. Điểm ngày 2026-10-05 chỉ là dữ liệu lịch sử.

Bằng chứng mới từ nghiên cứu chỉ đọc: PR #66 (`93e485f`) merge lúc 11:23:51 UTC ngày 2026-10-16 vào `edf7314b6779a002f321d27305f9d2959d3281cb`. [Run Scorecard 38893501686/job 116948377331](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38893501686/job/116948377331) lấy mẫu lúc 11:24:06, báo SAST 9 với **26/28**, xử lý SARIF xong lúc 11:24:29; CodeQL trong PR CI `38893479991` và main CI `38893501684` vẫn chạy lúc 11:25:24. Grace 60 giây có thể hết trước khi dịch vụ đăng ký suite; bản reconcile chờ đăng ký trong toàn bộ ngân sách có giới hạn, không suy ra thành công từ suite vắng mặt. Đây là lần lặp lại race, không chứng minh mọi scan lịch sử bị thiếu đều do timing. Lịch sử alert ngày 12/09 có trước PR #65; 25/28 là số cũ. Patch được reconcile trên SHA main nêu trên, chưa chạy hosted sau sửa và chưa xử lý xong alert #52.

Workflow giữ push, schedule, branch-protection và manual; chỉ chạy job trên `refs/heads/main`. Manual trên nhánh khác bị bỏ qua. Không dùng `workflow_run`. Job `readiness` và `verify-report` chỉ đọc; `analysis` là job duy nhất có OIDC write; `upload-sarif` là job duy nhất có security-events write. Hai job có quyền ghi không chạy script repository.

`scripts/scorecard_readiness.py` chốt SHA main, lấy 30 commit tại SHA đó, tìm PR đã merge có base repository đúng (bao gồm fork), rồi kiểm tra final head SHA. Các head trùng chỉ poll một lần nhưng vẫn giữ trọng số từng commit. Direct commit được ghi rõ là loại khỏi mẫu; mẫu hoàn toàn không phù hợp trả về `not_applicable`, không phải thành công. Nhiều PR phù hợp cho một commit là mơ hồ và thất bại.

Chỉ suite đúng head từ app `github-advanced-security` hoặc `github-code-scanning` được xét. Mọi suite được trả về của app phù hợp phải completed/success. ID không xác định thứ tự rerun; suite ID thấp đang pending hoặc thất bại không được che bởi suite ID cao thành công. Suite thất bại lịch sử có thể ngăn readiness theo chính sách bảo thủ; bỏ qua `github-actions`. Đây là bằng chứng của dịch vụ, không chứng thực workflow hoặc bộ query cụ thể. Failure, cancelled, skipped, neutral và thiếu suite không được coi là thành công. Tổng thời gian tối đa 900 giây, grace đăng ký suite trong cùng ngân sách 900 giây, poll 30 giây, tối đa 1.000 request, 5 trang mỗi collection, 100 phần tử mỗi trang và 2 MiB mỗi response. Request tối đa 15 giây trong ngân sách còn lại. Chỉ GET tới HTTPS `api.github.com`; không redirect hoặc theo URL do server cấp. Lỗi API/quyền/rate limit/response/budget đều được báo thất bại, không ghi token hoặc body response.

Analysis vẫn chạy sau readiness thất bại; không lọc cảnh báo. Hai report gốc được lưu bằng artifact ID bất biến của chính run này. `scripts/verify_scorecard_report.py` xác thực cấu trúc có giới hạn, repository, SHA và provenance SARIF nếu có; không thay thế toàn bộ schema validator. JSON `repo.commit` là SHA được scan; metadata commit của Scorecard là phiên bản công cụ. Metadata sai hoặc analysis thất bại chặn upload; artifact vẫn có thể được giữ để điều tra. Nếu metadata hợp lệ nhưng khác hoặc thiếu SHA readiness, giữ metadata phục vụ điều tra nhưng verifier thất bại và chặn upload SARIF. Checkout/upload dùng cùng SHA và ref main. Artifact được giữ 7 ngày.

Preflight bảo thủ hơn Scorecard v5.6.0: chỉ CodeQL, chỉ success, yêu cầu mọi suite trả về đều thành công và từ chối nhiều PR liên quan; Scorecard có thể nhận neutral, app SAST khác và PR đầu tiên. REST/GraphQL có thể khác nhau, nên đây không phải phép tính lại điểm Scorecard. Action scan remote default HEAD; main có thể đổi sau readiness. Kiểm tra metadata phát hiện lệch sau scan nhưng không ngăn REST publication đã diễn ra. Concurrency theo ref không hủy run đang chạy nhưng có thể thay run pending; không bảo đảm FIFO hay mọi event.

Action SHA vẫn tham chiếu Docker tag có thể thay đổi. Runtime metadata được ghi nhận, không phải chứng thực digest. Required CodeQL check trên final head trước merge là quyết định bảo mật riêng của owner, chưa thay đổi.

Kiểm tra local: `python3 -m unittest discover -s scripts/tests`, `actionlint .github/workflows/*.yml` và các hook gate hiện có. Test dùng fixture giả, không API thật hoặc credential thật. Hosted Actions, OIDC admission, artifact transfer và ingestion SARIF chưa được xác minh. Không dispatch/publish/API retry khi chưa có phê duyệt riêng. Xem [tài liệu EN và nguồn upstream](scorecard-readiness.md), [kế hoạch](../../plans/2026-10-16-sast52-readiness.md).

## Nghiệm thu còn cần runtime

Nguồn được đối chiếu: `.github/workflows/scorecard.yml`, `scripts/scorecard_readiness.py` (`Readiness`, transport), `scripts/verify_scorecard_report.py` (`validate`, `main`), và test tương ứng trong `scripts/tests/`.

| Thử nghiệm chưa chạy | Hành vi kỳ vọng từ nguồn | Bằng chứng cần thu khi được phép |
|---|---|---|
| Missing/pending/failed CodeQL suite | Không suy success từ thiếu suite; bounded wait/report | Readiness JSON của đúng run, final head SHA và trạng thái suite |
| Readiness thất bại nhưng analysis metadata hợp lệ | Analysis vẫn được thử; report được giữ; chỉ upload nếu analysis success và metadata valid | Job results, immutable artifact ID, nguyên bản JSON/SARIF |
| Metadata hợp lệ nhưng SHA readiness khác | Verifier exit 1, giữ valid=true/SHA thực để điều tra; result của verifier chặn upload | Verify outputs và bằng chứng SARIF upload bị skip |
| Non-main dispatch hoặc cancelled run | Điều kiện job ngăn chạy tương ứng | Job skip/cancellation evidence; không dispatch trong nhiệm vụ này |
| OIDC/public REST, artifact transfer, code scanning | Cấu hình có ý định publish; không chứng minh admission/ingestion | Run được owner cho phép và artifact/ingestion evidence; không thử tự động |

Không thay quyền, protection hoặc required checks. Quyết định bắt buộc CodeQL trên final head thuộc owner. Không cần quyết định mới để diễn giải nguồn; kết quả dịch vụ thật vẫn chưa xác minh.


## Phục hồi có giới hạn khi upstream lỗi

Run 39420559904 ngày 2026-10-20 lúc 12:18:38 UTC gặp GitHub GraphQL internal error tại ListCommits/graphqlHandler.setup, request ID `1008:1F79F2:26CAAEA:7FBA3B8:6AD75C1B`. Action dừng trước khi tạo report, nên bước artifact báo thiếu file là hậu quả. Chưa có bằng chứng outage toàn dịch vụ hay lỗi bảo mật repository.

Analysis thử tối đa hai lần bằng action native trong 20 phút. Chỉ lần scan đầu có continue-on-error để cho phép retry; lần cuối thất bại vẫn làm job fail. Lỗi upload bằng chứng hoặc checkout không được bỏ qua. Report nguyên bản nếu có được giữ riêng theo run/attempt/scan trước checkout sạch, ngăn dùng lại results.json/SARIF từ lần publish lỗi. Không thêm shell retry/sleep, action bên thứ ba, env/defaults cấp job hay quyền mới vào job OIDC bị giới hạn.

Verifier bên ngoài luôn yêu cầu scan/publication thành công và đủ hai report hợp lệ; thiếu artifact vẫn fail kể cả action báo success. SHA phải khớp readiness trước upload SARIF. Retry sau API publication lỗi có thể publish lặp, không bảo đảm exactly-once; verifier chạy sau publication công khai của action nên không thu hồi được publication đó. Lỗi auth/config/upstream kéo dài vẫn fail khi hết lượt. Không lọc hay sửa security findings. Test tổng hợp không chứng minh OIDC admission hoặc recovery thật trên GitHub. Xem [kế hoạch](../../plans/2026-10-20-scorecard-bounded-recovery.md).
