# Kiến Trúc Cấu Hình Agent

## Tổng Quan

Có hai tầng riêng biệt và KHÔNG ĐƯỢC nhập nhằng:

1. **Runtime sản phẩm (`eco-agents` crate)** — swarm agent Rust do repository này
   phân phối. Nó được cấu hình lập trình qua `eco_core::Config` và giao tiếp với
   Anthropic Claude qua `ClaudeClient`. Nó KHÔNG đọc file `.omo/` và KHÔNG route
   sang opencode-zen.
2. **Developer workflow (OMP)** — harness lập trình tương tác mà người làm việc
   trên repo này sử dụng. Cấu hình nằm ở `~/.omp/agent/config.yml` (OMP 17.x tự
   nạp) và được ghi trong `.omp/readme.md`.

## OMP Developer Workflow

Pipeline được khóa vào hai model:

| Vai trò | Model |
|---|---|
| Worker: dev/test/khai phá source/nghiên cứu, mọi subagent | `deepseek/deepseek-v4-pro:high` |
| Solver: thiết kế giải pháp, plan, verify | `openai-codex/gpt-5.6-terra:high` |
| Title/memory nội bộ (`tiny`) | `deepseek/deepseek-v4-flash:low` |

Các thiết lập chính trong `~/.omp/agent/config.yml`:

- `modelRoles` — role → selector pin provider (bảng trên).
- `retry.fallbackChains` — chỉ fallback cùng provider: DeepSeek Pro → Flash;
  GPT Terra → Sol. Nếu cả chain thất bại, request báo lỗi; không chuyển nhiệm vụ
  sang provider khác.
- `modelProviderOrder: [deepseek, openai-codex]`.
- `tools.approvalMode: write`; `advisor.enabled: false`; `prewalk.enabled: false`.
- `task.maxConcurrency: 3`, `task.batch: true`.

Handoff giữa hai model dùng `.omp/handoffs/CURRENT.md` (ghi đè tại mỗi milestone).
Template bắt buộc định nghĩa trong `~/.omp/agent/AGENTS.md`.

## Tích Hợp Rust (`eco-agents`)

`crates/eco-agents/src/bridge.rs` cung cấp:

- `BridgeResult` — tên package, tên file server sinh ra, source server, README
  markdown, thinking trace tùy chọn.
- `DocBridgeAgent` — sinh FastMCP 2.0 / rmcp server sẵn sàng production từ API
  signatures qua `ClaudeClient`.

```rust
use eco_agents::{BridgeResult, DocBridgeAgent};
use eco_core::Config;

let agent = DocBridgeAgent::new(config);
let result: BridgeResult = agent
    .generate_mcp_bridge("my-package", &api_signatures, 2000)
    .await?;
```

## Lệnh Đã Kiểm Chứng

```bash
omp --version
omp config list --json
omp models find deepseek-v4-pro --json
omp models find gpt-5.6-terra --json
cargo check --workspace
cargo test -p eco-agents
```

## Bảo Mật

- Không lưu API key hay token trong YAML hay file đã commit; OMP giải quyết
  credential qua credential store riêng.
- Không bao giờ ghi giá trị `.env`, key, token, database session hay transcript
  vào prompt hoặc file handoff.
