# DataGuard Agent Workspace Rules

## Thứ tự ưu tiên

1. Chỉ thị người dùng và system/developer rule đang hiệu lực.
2. `rules/git_workflow.md` cho kỷ luật git commit/push của agent.
3. `rules/workspace_governance.md` cho topology cụ thể của workspace.
4. `plans/2026-08-20-workspace-rationalization.md` cho phân loại và phase cleanup.
5. `.github/workflows/`, `DataGuard.sln`, `Directory.Build.props`, và `Dockerfile` cho entrypoint build/release thực tế.
6. `~/.omp/agent/AGENTS.md` cho policy OMP toàn cục.

## Topology và placement

- Production DataGuard: `src/`; test C#: `tests/DataGuard.Core.Tests/`, `tests/DataGuard.GoldenCorpus.Tests/`.
- Documentation/tri thức: `docs/`, `plans/`, `research/`, `grants/`, `brainstorm/`.
- Automation: `.github/`, `.githooks/`, `scripts/`, `tools/`, Docker files và DataGuard root manifests.
- Runtime local: `.omp/`, `.omo/`, `.codegraph/`, `.codex/`, cache lint/test. Các path này không phải documentation.
- Không tạo path top-level mới khi chưa đối chiếu workspace governance và project convention.

## Skills naming convention (cross-agent)

Skills toàn cục đặt tại `~/.claude/skills/` và được expose cho workspace qua symlink tại `.codex/skills/`. Tên đăng ký của mọi skill lấy từ frontmatter `name:` trong SKILL.md, luôn có prefix `ck:` (ví dụ `ck:cook`, `ck:git`, `ck:fix`).

1. Khi invoke skill, LUÔN dùng tên đầy đủ có prefix `ck:` — không bao giờ gọi tên trần (gọi `cook` sẽ lỗi `Unknown skill`; đúng phải là `ck:cook`).
2. Cú pháp invoke theo tool: oh-my-pi `/skill ck:<tên>`; Codex `$ck:<tên>` hoặc để auto-activation khớp theo `description`; opencode dùng skill tool với tên `ck:<tên>`.
3. Skill chỉ mang hướng dẫn quy trình; tính năng đặc thù của một tool (subagent/task delegation, v.v.) không chuyển sang tool khác — khi thiếu tool tương đương, tự thực hiện trực tiếp theo đúng các bước của skill.
4. Không sửa frontmatter (`name:`) trong `~/.claude/skills/` để "bỏ prefix" — sẽ phá các flow đang tham chiếu `/ck:*` và bị ClaudeKit updater ghi đè.
5. Sau khi cài/xoá skill mới ở mức global, chạy lại bước đồng bộ symlink `.codex/skills/` (idempotent: chỉ link dir có `SKILL.md`, bỏ qua `_shared`, `common`, `manifests`, `document-skills`).

## Quy tắc thay đổi

1. Sửa production source phải cập nhật docs DataGuard bị ảnh hưởng và chạy verification theo contract thay đổi.
2. Mọi file tạm phải nằm trong location gitignored đã được policy cho phép; không đặt artifact/generated output ở root.
3. Không xóa, đổi tên, hay tách tracked source, research, license, agent config hoặc session state chỉ dựa trên absence search.
4. Cleanup không đảo ngược cần manifest `from → disposition`, bảo toàn WIP, phê duyệt owner rõ ràng, rồi loại bỏ toàn bộ caller/link/hook/validator/lock file liên quan.
5. Không tạo `archive/` hoặc `legacy/` trong production repo; dùng repository/branch riêng cho tài sản còn giữ.

## Git workflow (cross-agent)

Áp dụng `rules/git_workflow.md`. Tóm tắt bắt buộc:

1. Không bao giờ commit / push / reset / amend / rebase khi chưa có yêu cầu tường minh từ người dùng.
2. Không bao giờ gọi `dg-git` trần hoặc `dg-git sync` (foot-gun tự commit+push). `dg-git` trần giờ exit 1.
3. Conventional Commits bắt buộc; cấm `auto-sync` / timestamp-junk (hook `commit-msg` + `dg-git` sẽ chặn).
4. Không `--no-verify`, `--force`, `git clean -fdx`; không push thẳng `main` khi chưa được phép.
5. Hook đã cài ở `.githooks/` (pre-commit, pre-push, commit-msg). Bật một lần: `git config core.hooksPath .githooks`.

## Xác minh tối thiểu

- Source C#: `dotnet build DataGuard.sln --configuration Release` và test bị ảnh hưởng.
- Workflow/container: YAML/actionlint và Docker smoke test khi có daemon.
- Docs/rules: `./scripts/verify_docs_sync.sh`, cùng kiểm tra các link/command đổi theo product canonical.

