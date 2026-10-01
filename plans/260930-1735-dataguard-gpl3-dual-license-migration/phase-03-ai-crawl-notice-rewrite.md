---
phase: 3
title: "AI crawl notice rewrite"
status: pending
priority: P2
effort: "2h"
dependencies: [2]
---

# Phase 3: AI crawl notice rewrite

<!-- Updated: Red Team + Validation Session 1 - A5: copilot-instructions.md là prompt file cho Copilot, không phải bề mặt TDM/crawler → rút thành pointer; thông báo bảo lưu quyền đặt ở README + docs/legal -->

## Overview
`.github/copilot-instructions.md` hiện cấp quyền AI-training/crawl theo MIT (dòng 5 và 9). File này là **prompt file cho GitHub Copilot**, không phải bề mặt mà crawler/TDM đọc → rút xuống **2 dòng pointer**; nội dung bảo lưu quyền đặt ở README FAQ (mục do P2 tạo) và `docs/legal/AI-USAGE-NOTICE.md`. Nói thẳng phần nào chỉ là ý định, phần nào có hiệu lực. Phụ thuộc P2 vì sửa cùng `README.md`/`README.vi.md`.

## Requirements
- Functional: thông báo nêu (1) áp dụng GPL-3.0-only/giấy phép thương mại; (2) không cấp phép riêng cho AI training/crawling; (3) chủ sở hữu bảo lưu quyền, gồm opt-out text-and-data-mining theo Điều 4(3) Chỉ thị EU 2019/790 (DSM) — **chỉ là tuyên bố ý định**, ADR không được viết "đã khai báo DSM opt-out" dựa trên file này; (4) giữ cho phép agent AI được ủy quyền làm việc trong workspace cục bộ của owner.
- Non-functional: không viết như điều khoản của giấy phép — GPLv3 §10 cấm "further restrictions"; đây là **tuyên bố bảo lưu quyền**, không phải điều kiện cấp phép.

## Architecture
<!-- Updated: Red Team + Validation Session 1 - A5: thêm copilot-instructions vào hàng "ý định" -->
| Cơ chế | Máy đọc được? | Hiệu lực | Ghi chú |
|---|---|---|---|
| Điều khoản GPL-3.0 / Commercial | Không | **Có hiệu lực pháp lý** (bản quyền) | Mã GPL bị sao chép vào output AI là câu hỏi bản quyền → luật sư (reopen), không giải quyết bằng văn bản repo |
| README FAQ + `docs/legal/AI-USAGE-NOTICE.md` | Không (văn xuôi) | Chỉ là tuyên bố bảo lưu; một tòa án Đức được cho là coi văn xuôi là chưa đủ [UNVERIFIED] | Bằng chứng ý định |
| `.github/copilot-instructions.md` | Không (prompt cho Copilot) | **Chỉ ý định**; không phải bề mặt crawler/TDM | 2 dòng pointer tới README/docs/legal |
| `robots.txt` root repo (đã có: GPTBot, ClaudeBot, CCBot, Google-Extended…) | Có | **Chỉ khai báo ý định**: GitHub không phục vụ file này ở gốc `github.com` | Giữ nguyên |
| TDMRep (`/.well-known/tdmrep.json`), `ai.txt` | Có | Chỉ hữu ích khi có website riêng | **Bỏ (YAGNI)**; mở lại khi có domain |
Các bản ≤v0.3.0 (MIT) đã phát hành: thông báo này không ngăn bên thứ ba dùng chúng cho AI training.

## Related Code Files
<!-- Updated: Red Team + Validation Session 1 - thêm docs/legal/AI-USAGE-NOTICE.md; copilot-instructions chỉ còn pointer -->
- Create: `docs/legal/AI-USAGE-NOTICE.md` (EN, ≤20 dòng: 4 ý ở Requirements + bảng "ý định vs hiệu lực" rút gọn + câu "không phải điều khoản của giấy phép").
- Modify: `.github/copilot-instructions.md` (→ 2 dòng: "Licence: GPL-3.0-only + commercial, see README → Dual licence & FAQ. AI/crawler notice: docs/legal/AI-USAGE-NOTICE.md."), `README.md`, `README.vi.md` (mục con "AI training & crawlers" dưới FAQ của P2, 3-4 dòng, link tới notice).
- Không sửa: `robots.txt`, `LICENSE`, `docs/legal/THIRD-PARTY-NOTICES.md`. Đã kiểm `AGENTS.md`, `devin_instructions.md`, `AI_AGENT_AUDIT.md`: không cấp quyền training/crawl (`AGENTS.md:34` chỉ nói không xóa licence/agent config dựa trên search rỗng).

<!-- Updated: Red Team + Validation Session 1 - TDD: Tests-first -->
## Tests-first (Red → Green)
1. RED hôm nay: `grep -nE "training pipelines|crawled under the MIT|\bMIT\b" .github/copilot-instructions.md` → 3 hit (dòng 5, 9); `grep -ciE "AI training|crawler|text-and-data-mining" README.md README.vi.md` → 0; `test -f docs/legal/AI-USAGE-NOTICE.md` → 1 (thiếu).
2. GREEN đích: (a) lệnh grep trên copilot-instructions → 0 hit và `wc -l .github/copilot-instructions.md` ≤ 3; (b) `grep -cE "reserv|bảo lưu" README.md README.vi.md docs/legal/AI-USAGE-NOTICE.md` ≥ 1 mỗi file; (c) **không cấp quyền**: `grep -niE "(granted|permitted|allowed|may be) .*(train|crawl|index)" docs/legal/AI-USAGE-NOTICE.md README.md` → chỉ được khớp dòng chứa "not"/"no" (kiểm thủ công, ghi output); (d) `git diff --stat main -- robots.txt` rỗng; (e) `python3 scripts/check-license-consistency.py` vẫn exit 0 (file mới nằm trong `docs/legal/` nên ngoài `FILE_LIST`); (f) `./scripts/verify_docs_sync.sh` xanh.

## Implementation Steps
1. **Chạy các lệnh RED ở Tests-first #1 và ghi kết quả.**
2. Viết `docs/legal/AI-USAGE-NOTICE.md` (tiếng Anh; 4 ý; bảng rút gọn; câu "This is a statement of reserved rights, not a licence condition"; giữ mục "Authorized Usage" cho agent cục bộ của owner).
3. Rút `.github/copilot-instructions.md` còn 2 dòng pointer.
4. Thêm mục "AI training & crawlers" vào FAQ README EN/VI (link notice; một câu "chỉ là tuyên bố bảo lưu; không phải điều khoản").
5. Không thêm `ai.txt`/TDMRep; ghi vào ADR (P6): hoãn có chủ đích, mở lại khi có website; ADR **không** claim "đã khai báo DSM opt-out".
6. Chạy Tests-first #2.

## Success Criteria
<!-- Updated: Red Team + Validation Session 1 - tiêu chí theo Tests-first -->
- [ ] Tests-first #2 (a)–(f) đều đạt; output dán vào PR body.
- [ ] `.github/copilot-instructions.md` ≤ 3 dòng, không chứa "MIT", "training", "crawled".
- [ ] Câu "bảo lưu quyền, không phải điều kiện cấp phép" có ở README EN/VI và `docs/legal/AI-USAGE-NOTICE.md`.

## Risk Assessment
| Rủi ro | K×T | Giảm thiểu |
|---|---|---|
| Người đọc/ADR hiểu thông báo là chặn được AI training hoặc "đã opt-out DSM" | Cao×TB | Bảng "ý định vs hiệu lực"; ADR ghi "tuyên bố ý định"; không dùng từ "cấm" |
| Thông báo văn xuôi không đủ làm opt-out máy đọc được | TB×Thấp | Ghi UNVERIFIED; mở lại khi có website (TDMRep) |
| Rollback | — | `git revert`; không ảnh hưởng build |
