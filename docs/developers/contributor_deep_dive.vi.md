> **⚠️ ARCHIVED** — This document describes the previous EcoSupport Rust/Python product. It does not apply to DataGuard (.NET). See [README](../../README.md) for current documentation.

[English](contributor_deep_dive.md) | [Tiếng Việt](contributor_deep_dive.vi.md)

# Cẩm Nang Chuyên Sâu Cho Nhà Phát Triển & Người Đóng Góp

**Mã tài liệu**: `DEV-MANUAL-2026.1`  
**Đối tượng**: Kỹ sư Rust, Nhà phát triển AI Agent, Người đóng góp Core

---

## 🛠️ 1. Kiến Trúc Crate & Khả Năng Mở Rộng

Workspace của EcoSupport được cấu trúc thành 5 crate chuyên biệt:

```
crates/
├── eco-core/    -> Cấu hình cơ sở, HTTP client, Claude 3.7 Thinking API, Quản lý Token & Telemetry
├── eco-radar/   -> Cỗ máy tính điểm nguy cấp (ECI), mô hình toán học, pipeline quét repository
├── eco-mcp/     -> Bộ phân tích giao thức JSON-RPC FastMCP 2.0 / rmcp, server và công cụ quét an ninh tĩnh
├── eco-agents/  -> Swarm subagent tự động (Triage, Patch, Doc Bridge)
└── eco-cli/     -> Binary CLI dòng lệnh siêu tốc (Clap + Indicatif)
```

---

## 🔌 2. Cách Bổ Sung Một FastMCP Tool Mới

Để mở rộng một tool native mới cho Claude Desktop hoặc Cursor:

1. **Định nghĩa Schema Đầu Vào** trong [`crates/eco-mcp/src/server.rs`](file:///Volumes/Data/101.AI/GitHub/eco_support/crates/eco-mcp/src/server.rs):
```rust
McpTool {
    name: "my_custom_tool".to_string(),
    description: "Thực thi kiểm tra AST tùy chỉnh trên các repo ngách.".to_string(),
    input_schema: json!({
        "type": "object",
        "properties": {
            "target_path": { "type": "string", "description": "Đường dẫn tuyệt đối cần kiểm tra" }
        },
        "required": ["target_path"]
    }),
}
```

2. **Cài đặt Handler Xử Lý** bên trong hàm `handle_tool_call`:
```rust
"my_custom_tool" => {
    let path = args.get("target_path").and_then(|p| p.as_str()).unwrap_or("");
    // Thực thi logic bất đồng bộ
    Ok(json!({ "status": "success", "inspected": path }))
}
```

3. **Thêm Integration Test** trong [`crates/eco-cli/tests/test_rust_mcp.rs`](file:///Volumes/Data/101.AI/GitHub/eco_support/crates/eco-cli/tests/test_rust_mcp.rs).

---

## 🦀 3. Quy Chuẩn Bất Biến & Thực Tiễn Tốt Nhất trong Rust

1. **Quy Tắc Zero-Unsafe**: Luôn duy trì `#![forbid(unsafe_code)]` trên tất cả các crate root.
2. **Lan Truyền Lỗi**: Sử dụng `thiserror::Error` cho các enum lỗi nội bộ crate và `eco_core::Result<T>` khi giao tiếp xuyên ranh giới crate.
3. **Structured Logging**: Sử dụng `tracing::info!`, `tracing::warn!`, và `tracing::error!` với các cặp key-value có cấu trúc. Tuyệt đối không dùng `println!` thô trong các thư viện crate.
