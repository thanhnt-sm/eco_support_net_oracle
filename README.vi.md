<div align="center">
  <p>
    <a href="README.md">English</a> | <b>Tiếng Việt</b>
  </p>
</div>

# DataGuard — Kiểm tra hợp đồng (Contract) giữa Entity ↔ Stored Procedure / Raw SQL

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**DataGuard** phát hiện lệch lạc (drift) giữa entity .NET và SQL mà chúng phụ thuộc — tham số stored procedure, hình dạng result set, nullability, ngữ nghĩa độ dài (CHAR/BYTE), lệch dialect — ngay tại thời điểm thiết kế và trong CI.

> **Vì sao tồn tại:** EF Core đã ghi nhận khoảng trống kiểm tra contract cho stored procedure từ [Microsoft EF issue #245 (2014)](https://github.com/dotnet/efcore/issues/245) và từ chối xây dựng. DataGuard đưa mẫu *model contracts* mà **dbt** đã chứng minh cho data engineering (preflight kiểm tra cột/tham số lúc compile, từ Core v1.5, 2023) vào thế giới stored-procedure/.NET.

## Bắt đầu nhanh

```bash
dotnet tool install -g DataGuard.Cli
cd YourProject
dataguard init            # tạo .dataguard.yml + .dataguard-snapshot.json
dataguard validate        # chạy rule contract với ground truth
dataguard snapshot diff   # phát hiện schema drift so với snapshot đã commit
```

## Ba chế độ ground truth

| Chế độ | Nguồn | Dùng khi |
|--------|-------|----------|
| **Full** | Kết nối DB trực tiếp | CI có credential được DBA duyệt |
| **Snapshot** *(mặc định)* | File `snapshot.json` commit trong repo | Zero credential CI; validate offline |
| **Manual** | Attribute `[ExpectedColumn]` / `[ExpectedSpParameter]` | Chỉ attribute, không cần DB |

Tầng IDE (`DataGuard.Analyzers`) đánh dấu lời gọi SQL chưa validate bằng incremental generator siêu nhẹ; tầng CI (`dataguard validate`) chạy toàn bộ diff engine với ground truth từ database.

## Tài liệu

- [Tổng quan giải pháp](docs/SOLUTION.md) · [Sản phẩm](docs/PRODUCT.md) · [Cách dùng](docs/USAGE.md) · [Kiến trúc](docs/architecture/architecture.md) · [Bảo mật](SECURITY.md)

## Giấy phép

[MIT](LICENSE)
