# Nghiên cứu: Tái sử dụng skills/flows Claude Code CLI cho opencode / oh-my-pi / codex

- Ngày: 2026-08-23
- Phạm vi: khảo sát global (`~/.claude`, `~/.omp`, `~/.config/opencode`, `~/.codex`) + workspace-level agent configs
- Mục tiêu: 3 tool ngoài (opencode.ai, oh-my-pi, codex) dùng lại skills đã cài global cho Claude Code CLI ở `~/.claude/skills/`, thay đổi tối thiểu
- Trigger: lỗi oh-my-pi (model gpt5.6-terra): `Unknown skill: cook ... Available: ck:cook, ...`

---

## 1. Kiến trúc hiện tại

### 1.1 Nguồn skills duy nhất (single source of truth)

`~/.claude/skills/` — ~85 skills ClaudeKit. Prefix `ck:` **nằm sẵn trong frontmatter** của từng SKILL.md, không phải do tool nào thêm vào:

```yaml
# ~/.claude/skills/cook/SKILL.md
---
name: ck:cook          # <-- tên đăng ký = frontmatter name
description: "Implement features, plans, and fixes..."
metadata:
  author: claudekit
---
```

Ngoài ra có `_shared/`, `common/`, `manifests/`, `document-skills/` (không phải skill đơn lẻ) và file `.md` rải root — phải loại khi wiring.

### 1.2 Ma trận discovery của từng tool (đã verify từ binary strings, changelog nhúng trong omp binary, và docs)

| Tool | Cơ chế đọc skills | Đang thấy `~/.claude/skills`? | Tên đăng ký |
|---|---|---|---|
| Claude Code CLI | native `~/.claude/skills/*/SKILL.md` | ✅ (nguồn gốc) | `ck:*` |
| opencode.ai | plugin `oh-my-openagent@latest` (config `~/.config/opencode/opencode.jsonc`) | ✅ tự động — toàn bộ skills đã vào system prompt của opencode | `ck:*` |
| oh-my-pi (omp) | provider riêng: `~/.claude/skills/*/SKILL.md`, `~/.codex/skills`, `.codex/skills`, `.opencode/skills`, `~/.config/opencode/skills`, `.github/skills`, `~/.claude/plugins/cache`, managed-skills `~/.omp/agent/managed-skills`. Hỗ trợ symlinked dirs | ✅ tự động (bằng chứng thực tế: danh sách `Available: ck:...` trong lỗi chính là nội dung `~/.claude/skills`) | `ck:*` |
| Codex CLI | `~/.codex/skills/<name>/SKILL.md` (user), `.codex/skills/` (project); **hỗ trợ chính thức symlinked skill folders**; invoke bằng `$name` / auto-activation theo description | ❌ `~/.codex/skills/` đang trống (0 item) | sẽ là `ck:*` |

Workspace-level: repo này có `.omo/` (config opencode), `.omp/handoffs/`; không có `.claude/`, `.codex/`, `.opencode/skills` project-level → không xung đột placement.

### 1.3 Chẩn đoán lỗi `Unknown skill: cook`

- Registry của omp lookup theo **frontmatter name** → chỉ tồn tại `ck:cook`.
- Model gpt5.6-terra gọi tên trần `cook` (đoán tên theo ngữ cảnh) → registry throw `Unknown skill`.
- Đây là lỗi **quy ước đặt tên**, không phải lỗi kiến trúc hay mất skill. Cùng cơ chế lỗi sẽ xảy ra trên codex nếu model gọi tên trần.

Lưu ý nhỏ: `claude-task-executor` trong danh sách Available của omp không có trong `~/.claude/skills` — nhiều khả năng đến từ plugin `pi-commandcode-provider` (duy nhất trong `~/.omp/plugins/omp-plugins.lock.json`); node_modules bị chặn đọc bởi `.ckignore` nên chưa verify trực tiếp. Không ảnh hưởng giải pháp.

---

## 2. Giải pháp đề xuất (thay đổi tối thiểu)

Nguyên tắc: giữ nguyên source of truth `~/.claude/skills/`, không sửa frontmatter, không copy file.

### A. Wire Codex bằng symlink (thay đổi cấu trúc DUY NHẤT)

Codex chính thức hỗ trợ symlinked skill folders → trỏ từng skill dir về `~/.claude/skills`:

```bash
#!/bin/zsh
# sync-claude-skills-to-codex.sh — idempotent, chạy lại được sau khi cài skill mới
set -euo pipefail
SRC="$HOME/.claude/skills"
DST="$HOME/.codex/skills"
mkdir -p "$DST"
count=0
for d in "$SRC"/*/; do
  name="$(basename "$d")"
  [ -f "${d}SKILL.md" ] || continue          # bỏ qua _shared, common, manifests, document-skills...
  target="$DST/$name"
  [ -L "$target" ] && continue
  [ -e "$target" ] && { echo "SKIP (exists): $target"; continue; }
  ln -s "$d" "$target"
  count=$((count+1))
done
echo "Linked $count skills -> $DST"
```

Vị trí đặt script (theo workspace governance): đây là automation cá nhân cấp user, không thuộc repo production → đặt ở `~/scripts/` hoặc `~/.codex/` đều được; **không** đặt vào repo DataGuard.

### B. Khắc phục lỗi naming cho models — chỉ thêm instruction, 0 thay đổi cấu trúc

Cả omp và codex đều inject AGENTS.md global vào mọi session → thêm 1 dòng quy ước:

1. `~/.omp/agent/AGENTS.md` (append):
   ```markdown
   ## Skills naming convention
   Skills toàn cục có tên dạng `ck:<tên>` (vd: `ck:cook`). Khi invoke skill, LUÔN dùng tên đầy đủ có prefix (`/skill ck:cook`); không bao giờ gọi tên trần `cook`.
   ```

2. `~/.codex/AGENTS.md` (tạo mới — hiện chưa tồn tại):
   ```markdown
   Global skills are registered with the `ck:` prefix (e.g. `ck:cook`, `ck:git`). When invoking a skill explicitly use the full prefixed name; otherwise rely on automatic activation by description.
   ```

### C. opencode — không làm gì cả

Plugin `oh-my-openagent@latest` đã load đủ `~/.claude/skills` vào system prompt. Trạng thái hoàn chỉnh.

---

## 3. Phương án đã cân nhắc và loại bỏ

| Phương án | Lý do loại |
|---|---|
| Bỏ prefix `ck:` trong frontmatter (~85 file sed) | Phá muscle memory/flow đang tham chiếu `/ck:*`; ClaudeKit updater có thể ghi đè ngược; rủi ro lớn, không "tối thiểu" |
| Copy skills sang `~/.codex/skills` | Drift khi cập nhật skills; tốn disk; symlink đạt cùng kết quả mà tự đồng bộ |
| Chuẩn hoá về `~/.agents/skills` (agentskills.io) | Cả 3 tool hiện tại không đọc native location này → vẫn phải symlink, không ít thay đổi hơn |
| Alias engine trong omp/codex | Không có cơ chế alias skill native trong cả hai; AGENTS.md instruction rẻ và đủ |

---

## 4. Rủi ro & giới hạn còn lại

1. **Parity tính năng ≠ parity skill**: skill dùng subagent/task() kiểu Claude Code (vd vài skill `ck:*` gọi task tool) sẽ giảm hiệu lực trên codex (codex không có hệ subagent tương đương). Nội dung hướng dẫn thì vẫn dùng được.
2. **Explicit invocation trên codex**: `$ck:cook` chứa dấu `:` — cần test 1 lần; nếu CLI không nhận, fallback là auto-activation (luôn hoạt động theo description) hoặc gọi trong câu hỏi thường ("dùng skill ck:cook để...").
3. Symlink là con trỏ 1 chiều: xoá skill khỏi `~/.claude/skills` sẽ để lại broken link trong `~/.codex/skills` → chạy lại script (nó chỉ skip link tồn tại; có thể bổ sung dọn broken links khi cần).
4. `.ckignore` chặn đọc `node_modules/*` cho agent session — không liên quan runtime của 3 tool, chỉ ảnh hưởng việc audit (như đã gặp trong phiên này).

---

## 5. Verification plan (khi triển khai)

1. Chạy symlink script → `ls -l ~/.codex/skills | wc -l` ≈ số skill dir thật (loại trừ `_shared/common/manifests/document-skills`).
2. Trong codex: mở `/skills` selector → thấy danh sách; thử `$ck:git` và một prompt khớp description (implicit).
3. Trong omp: `/skill ck:cook` → load OK; thử lại đúng kịch bản cũ gây lỗi → model dùng tên có prefix nhờ AGENTS.md.
4. Trong opencode: đã tốt từ trước, smoke-test 1 skill bất kỳ.
