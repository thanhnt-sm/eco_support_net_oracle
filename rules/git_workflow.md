# Git Workflow — Quy tắc chống vi phạm cho AI agent

Ràng buộc mọi AI agent (opencode, oh-my-pi/omp, Codex, Claude Code, và mọi harness tương lai) hoạt động trong workspace DataGuard. Mục tiêu: không bao giờ để agent tự ý commit/push, commit với message rác, hoặc phá history bằng thao tác phá hủy.

## Luật tuyệt đối (MUST)

1. **Không bao giờ commit, push, force-push, reset, amend, rebase, cherry-pick, hoặc tạo tag khi chưa có yêu cầu tường minh từ người dùng.** Một câu "hãy sửa X" không phải là lệnh commit.
2. **Không bao giờ gọi `dg-git` trần** hoặc `dg-git sync` (đường dẫn tự commit+push với message rác). Chỉ dùng `dg-git doctor` / `dg-git secret` (read-only), hoặc `dg-git commit -m "<conventional message>"` khi đã được phép commit.
3. **Conventional Commits bắt buộc**, định dạng `<type>[<scope>]: <subject>` (feat/fix/chore/docs/style/refactor/perf/test/build/ci/revert). Cấm message dạng `auto-sync`, timestamp-junk, hoặc rỗng.
4. **Một commit = một thay đổi logic.** Không quét WIP không liên quan vào cùng commit. Nếu đang có `.omo/boulder.json` active (work plan đang chạy), tuyệt đối không commit — việc commit thuộc về bước hoàn tất của plan.
5. **Không bao giờ `--no-verify`, `--no-gpg-sign`, `--force`, hoặc `git clean -fdx`.** Không bypass hook.
6. **Không push thẳng vào branch được bảo vệ** (`main`/`master`) trừ khi người dùng yêu cầu tường minh. Ưu tiên branch riêng + PR.
7. Trước khi commit: `git status`, `git diff`, `git diff --cached`, `git log --oneline -5`. Chỉ stage đúng file thuộc nhiệm vụ. Không `git add -A` trừ khi nhiệm vụ chính là commit toàn bộ cleanup đã được duyệt.

## Hook bảo vệ (đã cài)

- `.githooks/pre-commit` — format whitespace, `anti_garbage_guard.sh`, `verify_docs_sync.sh`.
- `.githooks/pre-push` — restore + build + test Release.
- `.githooks/commit-msg` — chặn message rác + ép Conventional Commits.

Bật hooks (một lần mỗi checkout): `git config core.hooksPath .githooks`.
Kiểm tra: `git config core.hooksPath` phải in `.githooks`.

## `dg-git` đã được làm an toàn

- Gọi trần `dg-git` → in usage, exit 1 (KHÔNG còn tự commit+push).
- `dg-git sync` yêu cầu `-m "<message>"` tường minh; không còn tự sinh `chore: auto-sync`.
- Mọi message `auto-sync` bị từ chối ở cả `dg-git` lẫn hook `commit-msg`.

## Khi vi phạm

- Commit tự động xuất hiện ngoài ý muốn → xem xét `git reset --soft HEAD~1` (chỉ khi chưa push) để tách lại; KHÔNG tự động làm nếu đã push — báo người dùng.
- Nghi ngờ có commit rác đã push → dừng, báo người dùng, không tự rewrite history.

## Ngoại lệ duy nhất

Chỉ người dùng được phép ra lệnh commit / push / push --force / rebase. Khi đó agent thực hiện đúng lệnh, vẫn đi qua hook, vẫn Conventional Commits.
