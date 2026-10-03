<div align="center">
  <p>
    <a href="README.md">English</a> | <b>Tiếng Việt</b>
  </p>
</div>

# DataGuard — Kiểm tra hợp đồng (Contract) giữa Entity ↔ Stored Procedure / Raw SQL

[![License: GPL-3.0-only + Commercial](https://img.shields.io/badge/license-GPL--3.0--only%20%2B%20Commercial-blue.svg)](LICENSE)
[![OpenSSF Scorecard](https://api.securityscorecards.dev/projects/github.com/thanhnt-sm/eco_support_net_oracle/badge)](https://scorecard.dev/viewer/?uri=github.com/thanhnt-sm/eco_support_net_oracle)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/15184/badge)](https://www.bestpractices.dev/projects/15184)

**DataGuard** phát hiện lệch lạc (drift) giữa entity .NET và SQL mà chúng phụ thuộc — tham số stored procedure, hình dạng result set, nullability, ngữ nghĩa độ dài (CHAR/BYTE), lệch dialect — ngay tại thời điểm thiết kế và trong CI.

> **Vì sao tồn tại:** EF Core đã ghi nhận khoảng trống kiểm tra contract cho stored procedure từ [Microsoft EF issue #245 (2014)](https://github.com/dotnet/efcore/issues/245) và từ chối xây dựng. DataGuard đưa mẫu *model contracts* mà **dbt** đã chứng minh cho data engineering (preflight kiểm tra cột/tham số lúc compile, từ Core v1.5, 2023) vào thế giới stored-procedure/.NET.

## Bắt đầu nhanh

```bash
# Tải CLI dataguard cho OS của bạn từ GitHub Releases (dataguard-<version>-<rid>.zip) và kiểm tra
# SHA-256 với file .sha256 đi kèm (DataGuard.Cli chưa được publish lên nuget.org):
# https://github.com/thanhnt-sm/eco_support_net_oracle/releases
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

## Giấy phép kép & FAQ

Từ **v0.4.0**, DataGuard được phát hành theo hai giấy phép:

1. **GNU GPL phiên bản 3 (chỉ v3)** (`GPL-3.0-only`, xem [`LICENSE`](LICENSE)), kèm một quyền bổ sung (additional
   permission) cho driver cơ sở dữ liệu và IDE host mà DataGuard chạy cùng ([`docs/legal/ADDITIONAL-PERMISSIONS.md`](docs/legal/ADDITIONAL-PERMISSIONS.md)).
2. **Giấy phép thương mại** cho doanh nghiệp muốn nhúng DataGuard vào sản phẩm mã đóng mà họ phân phối. Liên hệ
   `<contact email placeholder>`. Điều khoản được thỏa thuận riêng với từng bên được cấp phép; không có bảng giá hay biểu mẫu công khai.

> **Tình trạng pháp lý:** văn bản quyền bổ sung và điều khoản thương mại **chưa được luật sư xem xét**. Nội dung ở đây
> không phải tư vấn pháp lý. Nếu vấn đề giấy phép quan trọng với bạn, hãy hỏi luật sư của bạn.

**GPL-3.0 có cấm doanh nghiệp dùng DataGuard thương mại không?** Không. GPL-3.0 cho phép dùng thương mại, sao chép và
sửa đổi. Chạy `dataguard validate` trong CI hoặc dùng analyzer khi build sản phẩm của bạn tự nó không đòi hỏi giấy phép thương mại.

**Copyleft áp dụng khi nào?** Khi bạn *phân phối* một tác phẩm dựa trên DataGuard, ví dụ đóng mã hoặc binary của
DataGuard vào sản phẩm. Khi đó bạn phải cung cấp tác phẩm đó theo GPL-3.0, hoặc mua giấy phép thương mại. Dùng một công cụ
nội bộ không phải là phân phối.

**Có thể làm repository không cho sao chép được không?** Không. Repository công khai và điều khoản GitHub cho phép fork.
Các bản đến hết v0.3.0 đã phát hành theo giấy phép MIT và **giữ MIT vĩnh viễn**; thay đổi này không hồi tố.

**Phần nào không thuộc GPL?**

- `DataGuard.Contracts` được cấp phép theo giấy phép MIT có chủ đích. Gói này nằm trong ứng dụng của bạn và được DataGuard đọc
  bằng reflection nên không được kéo mã của bạn vào GPL.
- `Oracle.ManagedDataAccess.Core` và `Microsoft.Data.SqlClient.SNI.runtime` đi kèm CLI, file zip theo RID, container image
  và extension IDE theo điều khoản riêng của nhà cung cấp. Văn bản giấy phép nằm ở
  [`docs/legal/THIRD-PARTY-NOTICES.md`](docs/legal/THIRD-PARTY-NOTICES.md), đi kèm mọi bản tải DataGuard.

**Gói analyzer để lại gì trong output của tôi?** `DataGuard.Analyzers` là development dependency chỉ chạy lúc build: assembly
analyzer nạp trong compiler từ `analyzers/dotnet/cs` và gói không có `lib/`, nên `DataGuard.Analyzers.dll` và
`DataGuard.SqlClassification.dll` không xuất hiện trong output build của bạn (kiểm bằng `scripts/verify-analyzer-packaging.sh`).
Phát biểu này chỉ áp dụng cho gói Analyzers. `DataGuard.Contracts` (MIT) được chép vào output khi bạn dùng attribute của nó;
các gói khác như `DataGuard.Build` hay `DataGuard.Core` đặt DLL của chúng vào đó và theo giấy phép riêng.

**Mã do AI hỗ trợ có được bảo hộ không?** Một số commit có đồng tác giả là trợ lý AI. Bảo hộ bản quyền cho tác phẩm có AI hỗ trợ
còn hạn chế và chưa ngã ngũ; đây là rủi ro cần biết, không phải khẳng định theo hướng nào.

**Văn bản đầy đủ:** [`LICENSE`](LICENSE) (GPL-3.0),
[`docs/legal/ADDITIONAL-PERMISSIONS.md`](docs/legal/ADDITIONAL-PERMISSIONS.md),
[`docs/legal/THIRD-PARTY-NOTICES.md`](docs/legal/THIRD-PARTY-NOTICES.md) và văn bản MIT của v0.1.0-v0.3.0
([`docs/legal/MIT-v0.1.0-v0.3.0.txt`](docs/legal/MIT-v0.1.0-v0.3.0.txt)).
