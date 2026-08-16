[English](tech_stack_evaluation.md) | [Tiếng Việt](tech_stack_evaluation.vi.md)

# Đánh Giá Công Nghệ & Benchmark Ngôn Ngữ Lập Trình

**Mã tài liệu**: `TECH-EVAL-2026.1`  
**Trạng thái**: ĐÃ DUYỆT & KHÓA (APPROVED & LOCKED)  
**Quyết định cốt lõi**: **Rust** là ngôn ngữ cài đặt native chính cho EcoSupport Core.

---

## 📊 Ma Trận So Sánh Hiệu Năng

| Tiêu chí | **Rust (Tokio + rmcp)** | **Go (Goroutines)** | **Zig** | **C++20** | **Mojo** | **Python (FastAPI/FastMCP)** |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **Kích thước Binary (Stripped)** | **~8 - 14 MB** | ~18 - 30 MB | **~2 - 5 MB** | ~12 - 25 MB | ~45 - 80 MB | N/A (Trình thông dịch > 60MB) |
| **Độ trễ khởi động nguội** | **< 1.5 ms** | ~4.0 ms | **< 0.8 ms** | < 1.8 ms | ~15 ms | ~280 - 650 ms |
| **Mức tiêu thụ RAM nhàn rỗi** | **~6 - 12 MB** | ~28 - 45 MB | **~3 - 8 MB** | ~10 - 20 MB | ~50 - 90 MB | ~85 - 180 MB |
| **Mô hình Concurrency** | Tokio (Work-stealing async) | M:N Scheduler | Event loop / Thủ công | `std::jthread` / ASIO | Async (đang phát triển) | GIL / `asyncio` đơn luồng |
| **An toàn FFI & Duyệt AST** | **An toàn bộ nhớ + Tree-sitter** | Chi phí CGO cao | Quản lý bộ nhớ thủ công | Con trỏ Unsafe | Tương thích Python | CFFI (Khóa GIL) |
| **Model Context Protocol (MCP)** | **SDK chính thức `rmcp`** | Phi chính thức / Cộng đồng | Cộng đồng | Phi chính thức | Chưa có | Chính thức `mcp` / `fastmcp` |
| **An toàn Chuỗi cung ứng & Kiểu** | **Cargo Deny + Zero Unsafe** | Hệ thống kiểu cơ bản | Quản lý bộ nhớ thủ công | Rủi ro Undefined Behavior | Hệ sinh thái non trẻ | Kiểu động, lỗi tại runtime |

---

## 🎯 Lý Do Kiến Trúc Lựa Chọn Rust

1. **Mức Chiếm Dụng RAM Tiên Lượng Được Cho Runtime AI Agent Cục Bộ & Edge**:
   Việc chạy các swarm AI agent bên trong môi trường phát triển cục bộ (Cursor, Claude Desktop, dev container) đòi hỏi mức tranh chấp tài nguyên tối thiểu. Một binary Rust chỉ tiêu thụ 12 MB RAM cho phép chạy 50+ trình giám sát chạy ngầm đồng thời mà không gây ảnh hưởng tới ngữ cảnh mô hình host hay băng thông bộ nhớ.

2. **Căn Chỉnh Giao Thức Chính Thức (`rmcp`)**:
   Anthropic và tổ chức Model Context Protocol duy trì SDK Rust chính thức **`rmcp`**. Viết bằng Rust mang lại cho EcoSupport khả năng tích hợp server và client MCP native hạng nhất mà không phát sinh chi phí gọi hàm ngoại lai (FFI).

3. **Duyệt Codebase AST Tốc Độ Cao Bằng Tree-Sitter**:
   Đánh giá các repository đa ngôn ngữ phức tạp (C, Rust, Python, Go, TypeScript) đòi hỏi tốc độ parse AST cực nhanh. Bindings native của `tree-sitter` trên Rust phân tích 100,000 dòng code chỉ trong < 15ms, cho phép lập chỉ mục codebase tức thì trước khi vòng lặp suy luận của Claude 3.7 khởi động.

4. **Phân Phối File Binary Đơn Độc Lập Không Phụ Thuộc**:
   Người dùng và pipeline CI có thể chạy EcoSupport thông qua một file thực thi duy nhất (`eco-support`) mà không cần cài đặt môi trường Python, virtualenv hay các thư viện biên dịch C native.
