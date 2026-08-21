# ARCHIVE — EcoSupport Rust Era (2026-08-17 → 2026-08-19)

> **Di sản lịch sử.** Repo bắt đầu là Rust workspace "EcoSupport Native" nhắm grant "Claude for Open Source" của Anthropic. Từ 2026-08-19 chuyển hẳn sang sản phẩm canonical **DataGuard** (.NET). File này giữ lại phần register cũ để tham chiếu; KHÔNG dùng làm nguồn sự thật. SSOT hiện tại: `plans/ACTIVE_SESSION_REGISTER.md`.

## Mục tiêu cũ (đã thay thế)

EcoSupport Native: hệ thống tự động phát hiện và hỗ trợ thư viện OSS niche có nguy cơ cao. KPI: hồ sơ grant Anthropic được chấp thuận → Claude Max 20x API usage.

## Cấu trúc workspace cũ (không còn đúng)

```
eco_support/               (Cargo workspace)
├── crates/eco-core, eco-radar, eco-mcp, eco-agents, eco-cli
├── docs/, rules/, brainstorm/, research/, grants/, scripts/, scratch/
```

Các thư mục này vẫn tồn tại vật lý trong repo nhưng không còn là sản phẩm canonical; disposition trong `plans/2026-08-20-workspace-rationalization.md`.

## Mốc lịch sử

| Ngày | Sự kiện |
|---|---|
| 2026-08-17 | Fresh git repo (xóa history cũ, backup tar.gz), 123 files Rust + research |
| 2026-08-17 | Đổi tên repo → `eco_support_net_oracle`, account `thanhnt-sm`, push online |
| 2026-08-19 | Quyết định chuyển sản phẩm canonical sang DataGuard .NET (ADR-001 v4) |
| 2026-08-20 | Workspace rationalization; cutover toàn bộ rules sang policy DataGuard |

## Git history cũ

```
874cea9 chore: initial commit (fresh repository)
ea41340 docs(session-register): record fresh repository creation
e68f165 chore(repo): rename to eco_support_net_oracle across metadata and docs
9400772 fix(repo): correct GitHub account to thanhnt-sm in repo metadata
```

Chi tiết phiên làm việc thời Rust: xem git history của `plans/ACTIVE_SESSION_REGISTER.md` trước commit 2026-08-21.
