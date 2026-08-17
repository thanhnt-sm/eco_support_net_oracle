# Kiến Trúc Cấu Hình Agent

## Tổng Quan

Tài liệu này mô tả hệ thống cấu hình agent cho EcoSupport Native sử dụng các model miễn phí của opencode zen. Cấu hình cho phép chọn model theo loại tác vụ (task-routed) với tự động fallback sang model cục bộ.

## Các File Cấu Hình

### `.omo/agents.toml` — Registry Agent

Định nghĩa tất cả agent (model) có sẵn cùng các thuộc tính:

| Trường | Mô Tả |
|--------|-------|
| `name` | Định danh agent duy nhất |
| `provider` | Nhà cung cấp: `opencode-zen` hoặc `ollama` |
| `model` | ID model (vd: `nvidia/nemotron-3-ultra`) |
| `tags` | Các loại tác vụ agent này giỏi |
| `context_window` | Số token ngữ cảnh tối đa |
| `max_output` | Số token đầu ra tối đa |
| `temperature` | Nhiệt độ lấy mẫu |
| `top_p` | Tham số lấy mẫu hạt nhân |
| `endpoint` | URL endpoint API |

**Các Model Miễn Phí Đã Cấu Hình:**
- `nemotron-3-ultra-free` — NVIDIA Nemotron 3 Ultra (reasoning, planning)
- `llama-3.1-405b-free` — Meta Llama 3.1 405B (coding, implementation)
- `qwen2.5-coder-32b-free` — Qwen 2.5 Coder 32B (tác vụ nhanh)
- `deepseek-v3-free` — DeepSeek V3 (chung, đa ngôn ngữ)
- `gemini-2.0-flash-free` — Google Gemini 2.0 Flash (nhanh, đa phương thức)

**Model Fallback Cục Bộ (Ollama):**
- `ollama-nemotron3-8b` — Nemotron 3 Ultra 8B local
- `ollama-qwen2.5-coder-32b` — Qwen 2.5 Coder 32B local
- `ollama-llama3.1-70b` — Llama 3.1 70B local

### `.omo/config.toml` — Routing & Giới Hạn

Chứa quy tắc routing, giới hạn, và cài đặt i18n.

**Routing (`[omp.routing]`):**
Ánh xạ loại tác vụ sang tên agent:
```
reasoning → nemotron-3-ultra-free
planning → nemotron-3-ultra-free
architecture → nemotron-3-ultra-free
coding → llama-3.1-405b-free
implementation → llama-3.1-405b-free
debug → llama-3.1-405b-free
review → nemotron-3-ultra-free
quick → qwen2.5-coder-32b-free
general → deepseek-v3-free
multilingual → deepseek-v3-free
fast → gemini-2.0-flash-free
```

**Fallback Chain (`[omp.routing.fallback_chain]`):**
```
primary → nemotron-3-ultra-free
secondary → deepseek-v3-free
tertiary → ollama-nemotron3-8b
```

**Giới Hạn (`[omp.limits]`):**
Giới hạn số agent spawn và ngân sách token cho mỗi loại tác vụ.

**i18n (`[omp.i18n]`):**
```
supported_languages = ["vi", "en", "ja", "ko"]
default_language = "vi"
fallback_to_english = true
```

## Tích Hợp Rust

### `OpencodeZenBridgeAgent` (trong `crates/eco-agents/src/bridge.rs`)

```rust
use eco_agents::{OpencodeZenBridgeAgent, OpencodeZenConfig};

let agent = OpencodeZenBridgeAgent::new()?;
let model = agent.route_task("coding"); // Trả về "llama-3.1-405b-free"
let result = agent.generate_with_model("coding", "Viết hàm Rust...", 2000).await?;
```

**Phương Thức:**
- `new()` — Nạp config từ `.omo/agents.toml` và `.omo/config.toml`
- `route_task(task_type)` — Trả về tên agent cho loại tác vụ
- `fallback_agent(attempt)` — Trả về agent fallback tại chỉ số
- `generate_with_model(task_type, prompt, thinking_budget)` — Tạo nội dung với model được route + fallback chain

### Xử Lý Lỗi

```rust
pub enum OpencodeZenError {
    NoProviderAvailable,
    ModelNotFound(String),
    RoutingError(String),
    ConfigReadError(String),
}
```

## Ví Dụ Sử Dụng

### CLI
```bash
# Kiểm tra config
omp config validate

# Liệt kê agent
omp agent list

# Route một tác vụ
omp agent route coding
```

### Rust
```rust
use eco_agents::OpencodeZenBridgeAgent;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let agent = OpencodeZenBridgeAgent::new()?;
    
    // Route tác vụ sang model tốt nhất
    let model = agent.route_task("architecture");
    println!("Architecture task → {}", model);
    
    // Tạo nội dung với fallback tự động
    let response = agent.generate_with_model(
        "debug",
        "Sửa lỗi borrow checker Rust này...",
        4096
    ).await?;
    
    println!("{}", response);
    Ok(())
}
```

## Thêm Model Mới

1. Thêm entry `[[agents]]` vào `.omo/agents.toml`
2. Thêm quy tắc routing vào `[omp.routing]` trong `.omo/config.toml`
3. Thêm giới hạn vào `[omp.limits]` nếu cần
4. Thêm tag ngôn ngữ vào `[omp.i18n.model_language_tags]` nếu đa ngôn ngữ
5. Chạy `cargo check --workspace`
6. Chạy `./scripts/verify_docs_sync.sh`

## Bảo Mật

- `restricted_models` chặn các model độc quyền (GPT-4, v.v.)
- `enable_model_quota` thực thi giới hạn tốc độ
- API key qua biến môi trường `OPENCODE_ZEN_API_KEY`