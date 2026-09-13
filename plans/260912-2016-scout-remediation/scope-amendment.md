---
type: owner-scope-amendment
date: 2026-09-12
status: accepted-scope
---
# Scope amendment — implement all missing documented capabilities

## Owner decision

Người dùng: “Triển khai cả các tính năng còn thiếu để đáp ứng toàn bộ claim trong tài liệu”. Quyết định này thay thế giả định docs-only. Task hiện tại cập nhật plan/handoff; execution vẫn thuộc session Terra như yêu cầu trước.

## Authoritative changes

- Feature-gap chỉ đóng khi implementation + regression/integration/host evidence + docs parity đạt; không close bằng xóa claim.
- Preserve60 scout IDs +V01–V06 +15RT acceptance; bổ sung FC claim groups/subrows. Parent liên quan chỉ đóng sau FC children.
- Phases1–5 giữ safety/correctness nền; phase6 không xóa requirement để close; phase7 chỉ foundation verification. Phase15 final capability gate.
- Phases8–15 thêm claim contract, health host, remote advisory/score, IDE features, semantic/actions, integrity/plugins, benchmark/optimization và acceptance.
- Offline/no-egress default, redaction, explicit trust, no false-clean, ABI/migration và no unauthorized git/publish/destruction vẫn có hiệu lực. Có online capability không đồng nghĩa bật network mọi run.
- Có thể label “planned/not yet shipped” tạm thời cho đúng thực tế; label đó không đóng finding. Historical/superseded proposals không tự biến thành current requirements.
- Absolute/contradictory claims như “never expose credentials in memory dumps”, “zero allocation”, contradictory provider counts phải có measurable scope/owner decision. Giữ open nếu chưa đạt/chưa chốt, không âm thầm đổi thành lời hứa khác.

## Overrides

Older “do not add health/CVE/settings”, “docs-only”, “conditional future feature”, “remove unimplemented advertised actions to close” bị supersede về capability delivery. Safety vẫn giữ: không stream secrets, không DB/network per-keystroke analyzer, không auto-run REF CURSOR routines, không gọi ALC là sandbox, không tự mở public listener.

Historical reports phản ánh thiết kế trước, không authority scope hiện tại. Sol GO trước áp dụng narrower plan; expanded plan có review riêng. Giữ nguyên WIP docs/execution-evidence; executor đang chạy phải đọc amendment ở checkpoint tiếp theo trước close feature rows.

## Handoff

Terra đọc amendment +claims ledger +all15 phases, Sol kiểm scope/contracts trước source edits. Hydrate theo DAG:8 follows1, không chờ7 mới khám phá requirements. Terra sole writer; Sol read-only. Report parents,RT children,FC groups và individual claims riêng; không all-done khi child required còn open/blocked. No automatic source execution initiated by this plan update.
