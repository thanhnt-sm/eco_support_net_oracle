[English](CONTRIBUTING.md) | [Tiếng Việt](CONTRIBUTING.vi.md)

# Contributing to EcoSupport

Thank you for your interest in contributing to **EcoSupport**! We are committed to building a transparent, safe, and community-first infrastructure platform for open-source maintainers.

---

## 🧭 Code of Conduct & Maintainer First Principles

1. **Maintainer Consent & Respect**: EcoSupport agents are designed to reduce maintainer burden, never to generate spammy automated comments. All triage and PR generation must be verifiable and high-signal.
2. **Anthropic Safety Standards**: We follow Anthropic's Constitutional AI guidelines. Never generate tools or prompts that execute unvetted remote code without explicit sandboxing.

---

## 🛠️ Development Workflow

1. **Fork and Clone** the repository:
   ```bash
   git clone https://github.com/thannt/eco_support_net_oracle.git
   cd eco_support_net_oracle
   ```
2. **Compile and Test (Rust Native Engine)**:
   ```bash
   cargo check --workspace
   cargo test --workspace
   cargo clippy --workspace --all-targets -- -D warnings
   cargo fmt --check
   ```
3. **Submitting a Pull Request**:
   - Ensure all new features have accompanying unit tests in `crates/eco-cli/tests/` or relevant crate test suites.
   - Maintain bilingual documentation for any new/updated human guides (`.md` and `.vi.md`).
   - Run `./scripts/verify_docs_sync.sh` and `./scripts/anti_garbage_guard.sh` before pushing.

