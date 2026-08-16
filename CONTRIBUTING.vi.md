[English](CONTRIBUTING.md) | [Tiếng Việt](CONTRIBUTING.vi.md)

# Hướng Dẫn Đóng Góp Cho EcoSupport

Cảm ơn bạn đã quan tâm đến việc đóng góp cho **EcoSupport**! Chúng tôi cam kết xây dựng một nền tảng hạ tầng minh bạch, an toàn và đặt cộng đồng open-source maintainer lên hàng đầu.

---

## 🧭 Quy Tắc Ứng Xử & Nguyên Tắc Tôn Trọng Maintainer

1. **Sự Đồng Thuận & Tôn Trọng Maintainer**: Các agent của EcoSupport được thiết kế để giảm tải gánh nặng cho maintainer, tuyệt đối không tạo ra các bình luận spam tự động vô nghĩa. Toàn bộ quá trình khám nghiệm (triage) và tạo Pull Request (PR) phải có thể kiểm chứng độc lập và đạt chất lượng cao.
2. **Tiêu Chuẩn An Toàn Anthropic**: Chúng tôi tuân thủ các nguyên tắc Constitutional AI của Anthropic. Không bao giờ tạo ra các công cụ hoặc prompt thực thi mã nguồn từ xa không được kiểm duyệt khi chưa có môi trường sandbox độc lập.

---

## 🛠️ Quy Trình Phát Triển (Development Workflow)

1. **Fork và Clone** repository:
   ```bash
   git clone https://github.com/thannt/eco_support_net_oracle.git
   cd eco_support_net_oracle
   ```
2. **Biên Dịch và Kiểm Thử (Rust Native Engine)**:
   ```bash
   cargo check --workspace
   cargo test --workspace
   cargo clippy --workspace --all-targets -- -D warnings
   cargo fmt --check
   ```
3. **Gửi Pull Request (PR)**:
   - Đảm bảo tất cả các tính năng mới đều có unit test đi kèm trong `crates/eco-cli/tests/` hoặc test suite của crate tương ứng.
   - Duy trì tài liệu song ngữ cho mọi tài liệu hướng dẫn mới hoặc cập nhật (`.md` và `.vi.md`).
   - Chạy `./scripts/verify_docs_sync.sh` và `./scripts/anti_garbage_guard.sh` trước khi đẩy commit lên Git.
