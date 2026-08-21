[English](CONTRIBUTING.md) | [Tiếng Việt](CONTRIBUTING.vi.md)

# Hướng Dẫn Đóng Góp Cho DataGuard

Cảm ơn bạn đã quan tâm đến việc đóng góp cho **DataGuard**! DataGuard là contract validation engine cho .NET — phát hiện drift giữa entity và SQL mà code phụ thuộc (tham số stored procedure, result-set shape, nullability, length semantics, dialect mismatch) ngay tại thời điểm thiết kế và trong CI.

---

## 🧭 Quy Tắc Ứng Xử & Nguyên Tắc Sơ Cấp

1. **Evidence-first**: mọi claim (tài liệu, mô tả PR, commit message) phải kiểm chứng được — lệnh + output. Bug fix nên kèm test đã fail trước khi fix.
2. **Tư thế enterprise**: mặc định không telemetry, không để secret trong log/argv/SARIF, xử lý credential fail-closed. Nếu thay đổi chạm các vùng này, nêu rõ cách các đảm bảo được giữ nguyên.
3. **Một PR một mục đích**: giữ pull request nhỏ và tập trung; tuân theo conventional commits (`fix:`, `feat:`, `test:`, `docs:`, `ci:`).

---

## 🛠️ Quy Trình Phát Triển (Development Workflow)

1. **Fork và Clone** repository:
   ```bash
   git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
   cd eco_support_net_oracle
   ```
2. **Build, Test, Format** (.NET 9):
   ```bash
   dotnet build DataGuard.sln                 # phải 0 errors, 0 warnings
   dotnet test DataGuard.sln                  # toàn bộ test phải pass
   dotnet format DataGuard.sln --verify-no-changes
   ```
   Integration test cần Docker (Testcontainers) sẽ tự skip khi không có Docker daemon.
3. **Gửi Pull Request (PR)**:
   - Đảm bảo feature/rule mới có unit test đi kèm trong `tests/`.
   - Chú ý public API surface: `DataGuard.Contracts` (netstandard2.0) được project người dùng reference — breaking change cần ADR trong `plans/adr/`.
   - Chạy `dotnet list DataGuard.sln package --vulnerable --include-transitive` và đảm bảo không đưa vào package có lỗ hổng.

---

## 📐 Bố Cục Dự Án

| Đường dẫn | Nội dung |
|---|---|
| `src/DataGuard.Contracts` | Contract attributes dùng chung với IDE analyzers (netstandard2.0, zero deps) |
| `src/DataGuard.Core` | Rules engine, baseline, security, sources, reporting |
| `src/DataGuard.*.Adapter` | Ground-truth readers cho SQL Server / Oracle / MySQL / PostgreSQL |
| `src/DataGuard.Analyzers` / `CodeFixes` | Roslyn analyzer (IDE-light) và code fixes |
| `src/DataGuard.Cli` | `dotnet tool` — validate/snapshot/baseline/oracle-check |
| `src/DataGuard.VSCode` / `DataGuard.VisualStudio` | Editor extensions (CLI là authority; host giữ mỏng) |
| `tests/` | Core.Tests, GoldenCorpus.Tests, Analyzers.Tests |
| `plans/` | Kế hoạch và ADR (xem `plans/ACTIVE_SESSION_REGISTER.md`) |

---

## 📖 Đọc Tiếp

- Quyết định kiến trúc: `plans/adr/`
- Roadmap và công việc đang mở: `plans/ACTIVE_SESSION_REGISTER.md`
- Chính sách bảo mật: [SECURITY.md](SECURITY.md)
