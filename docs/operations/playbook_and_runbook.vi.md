[English](playbook_and_runbook.md) | [Tiếng Việt](playbook_and_runbook.vi.md)

# Cẩm Nang Vận Hành SRE & Quy Trình Xử Lý Sự Cố (Runbook)

**Mã tài liệu**: `OPS-RUNBOOK-2026.1`  
**Đối tượng**: Kỹ sư DevOps, SRE, Người vận hành hệ thống, Maintainer  
**Dịch vụ**: Daemon `eco-support-rs` Native & Server FastMCP

---

## 🚀 1. Quy Trình Vận Hành Tiêu Chuẩn (SOP)

### SOP-001: Cấu Hình Môi Trường
Tạo hoặc cập nhật file `.env` tại thư mục gốc workspace:

```bash
# Anthropic API Key (Bắt buộc cho chế độ Claude 3.7 Thinking trực tiếp)
ANTHROPIC_API_KEY=sk-ant-api03-...

# Đặc tả phiên bản mô hình
ANTHROPIC_MODEL=claude-3-7-sonnet-20250219

# Ngân sách Token Extended Thinking (Mặc định: 4096, Tối đa: 16384)
THINKING_BUDGET_TOKENS=4096

# GitHub API Token (Tùy chọn, tránh chạm giới hạn rate limit khi quét radar)
GITHUB_TOKEN=ghp_...

# Mức độ Log
LOG_LEVEL=info
```

### SOP-002: Phát Hành Đa Nền Tảng An Toàn

Thực hiện theo [Hướng dẫn phát hành an toàn](release_guide.vi.md). Điểm vào release duy nhất là:

```bash
bash tools/git-tools/dg-release --tag v1.2.3 --publish-marketplaces --dry-run
```

Lệnh production trong hướng dẫn đòi hỏi xác nhận tường minh và không lưu credential Marketplace, NuGet hoặc GitHub trong repository.

### SOP-003: Khởi Chạy Dịch Vụ & Triển Khai Daemon
```bash
# Chế độ A: Giao diện dòng lệnh CLI tương tác cho Developer
cargo run -p eco-cli -- scan --category c-ffi --limit 10

# Chế độ B: Binary Production Release (Chuẩn Stdio cho Claude Desktop)
./target/release/eco-support mcp-serve --transport stdio

# Chế độ C: Dịch vụ Radar chạy ngầm (Headless Background Daemon)
nohup ./target/release/eco-support scan --category general-niche --limit 50 > /var/log/ecosupport_radar.log 2>&1 &
```

---

## 🩺 2. Ma Trận Khắc Phục Sự Cố & Ứng Phó Sự Cố

| Mã sự cố | Triệu chứng | Nguyên nhân khả dĩ | Biện pháp xử lý ngay lập tức |
| :--- | :--- | :--- | :--- |
| **ERR-API-401** | `API returned 401 Unauthorized` | Thiếu hoặc hết hạn `ANTHROPIC_API_KEY`. | Kiểm tra file `.env` và test tính hợp lệ của key qua lệnh `curl https://api.anthropic.com/v1/messages`. Hệ thống sẽ tự động hạ cấp xuống chế độ mô phỏng tất định nếu key chưa được set. |
| **ERR-RATE-429** | `GitHub API Rate limit exceeded` | Quét ẩn danh không token vượt quá 60 reqs/giờ. | Cấu hình `GITHUB_TOKEN` trong `.env` để tăng giới hạn lên 5,000 reqs/giờ. |
| **ERR-MCP-TIMEOUT** | Claude Desktop bị timeout khi gọi tool | Ngân sách Extended Thinking đặt quá cao (> 32k) trên kết nối mạng chậm. | Giảm `THINKING_BUDGET_TOKENS` xuống `4096` hoặc kiểm tra độ trễ mạng tới máy chủ Anthropic. |
| **ERR-AUDIT-REJECT** | `audit_mcp_security` gắn cờ SAFE=False | Mã nguồn chứa lệnh nguy hiểm như `subprocess.run(shell=True)` hoặc `eval()`. | Thay thế việc gọi shell bằng véc-tơ tham số có cấu trúc và áp dụng whitelist tên miền nghiêm ngặt. |

---

## 🔄 3. Kịch Bản Khôi Phục Sau Thảm Họa (Disaster Recovery)

### Kịch Bản A: GitHub API Gặp Sự Cố Gián Đoạn
Nếu GitHub hoặc các package registry bên ngoài bị sập:
1. **Niche Radar** tự động chuyển sang sử dụng kho dữ liệu hạt giống cục bộ tại [`research/data/niche_seed_registry.json`](file:///Volumes/Data/101.AI/GitHub/eco_support/research/data/niche_seed_registry.json).
2. Dịch vụ duy trì hoạt động 100% ở chế độ chẩn đoán offline từ bộ nhớ đệm mà không gây crash/panic hệ thống.

### Kịch Bản B: Anthropic API Gặp Sự Cố Gián Đoạn
1. **Claude Client** tự động bắt lỗi truyền tải HTTP và chuyển hướng sang **Chế Độ Mô Phỏng Offline Độ Chính Xác Cao (Offline High-Fidelity Simulation Mode)**.
2. Telemetry ghi nhận sự kiện fallback trong log (`WARN: Running deterministic offline simulation harness`).

---

## 💰 4. Quản Lý Chi Phí Token & Rào Chắn Ngân Sách

Để ngăn ngừa chi phí API phát sinh ngoài tầm kiểm soát:
- Mọi lượt gọi suy luận đều ghi log chi tiết prompt tokens, completion tokens và thinking tokens qua `eco_core::telemetry::log_token_metrics`.
- Giới hạn cứng token tối đa được chốt tại `max_tokens = 20000`.
- Ngân sách suy luận mặc định là `4096` tokens mỗi lượt (~$0.015 cho mỗi lần chẩn đoán khám nghiệm issue).
