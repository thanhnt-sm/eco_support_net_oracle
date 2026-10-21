# LSP process-test diagnostics

## Confirmed boundary and limits

Main Marketplace runs `39540600267` (872974c) and `39547822832` (45aba14) both failed in `LanguageServerLifecycleTests` while awaiting the initialize response, before constructing or sending the mixed-line-ending document. Both builds succeeded; the three-second response-read budget expired in the header reader. This is not evidence that UTF-16 source mapping failed. The cause of the slow or incomplete initialize response is not yet proven; child startup, scheduling and transport hypotheses need phase/state evidence. Do not classify a later green run as a root-cause fix.

The legacy test reader exposed only an OperationCanceledException. It did not report response progress or child state. Its process fixture redirected stderr without draining it and killed failed/unfinished children without awaiting their exit. These are confirmed harness diagnostic/lifetime gaps; neither is asserted to have caused the hosted timeout.

## Small test-only slice

- Preserve one three-second response-read budget across the entire header and payload; partial progress does not reset it. Preserve all lifecycle and exact diagnostic/range assertions.
- Report only allowlisted phase, elapsed time, header/payload byte counts and child/runtime scalar state. Never include raw headers, JSON payload, stderr text, environment values or exception messages in timeout diagnostics. A failing state snapshot must not replace the timeout.
- Drain stderr with a fixed buffer and retain only its byte count/failure category. Own, stop and await the synthetic child and drain task, with bounded cleanup waits. Do not alter production server scheduling, timeouts or behavior.
- Exercise fragmented UTF-8 and adjacent frames, EOF, malformed/oversized frames, cancellation during header/payload, a single frame budget and privacy sentinels. Validate the actual server lifecycle tests and canonical gates on the resulting commit.

Implemented, locally tested, hosted-tested and merged states must be reported separately. The investigation branch `fix/lsp-process-diagnostics` was created from main 45aba14 and fast-forwarded to main 1415202 before final verification; the StreamAsync draft and other lanes are outside this patch. No claim of general protocol conformance, root-cause elimination, speed improvement, Windows editor-host validation or long-run leak freedom follows from these test helpers.

## Tiếng Việt

Hai lỗi CI đã xác minh xảy ra lúc chờ initialize, trước khi gửi tài liệu; chưa chứng minh lỗi CR/LF hoặc UTF-16, cũng chưa chứng minh contention là nguyên nhân. Slice này chỉ sửa khoảng trống chẩn đoán và ownership trong test harness: giữ nguyên một budget đọc 3 giây cho cả frame, thêm thông tin phase/progress/trạng thái an toàn, drain stderr bằng buffer cố định và chờ thu dọn tiến trình thuộc test. Không tăng timeout, giảm gate hoặc sửa production để làm xanh CI. Kết quả local, hosted và merge phải ghi riêng; nếu lỗi còn lặp lại, dùng bằng chứng mới để điều tra nguyên nhân thay vì retry mù.
