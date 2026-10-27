# Chuẩn hóa độ bao phủ nhánh

`scripts/coverage_manifest.py` nhận cách viết boolean của Coverlet và XML
(`true`/`false`, `1`/`0`), kiểm tra số nhánh và xuất cờ chữ thường. Cờ mâu thuẫn,
thiếu số nhánh hoặc phần trăm không hợp lệ làm kiểm tra thất bại trước khi tạo
danh sách tải lên. Coverlet cắt phần trăm đến hai chữ số thập phân; tỷ lệ được
tính từ số nguyên chính xác.

`reports[].branches` chỉ đếm một lần các bản ghi dòng nguồn lớp được giữ lại.
Bản sao trong phương thức được kiểm tra nhưng không cộng lại. Chính sách loại
lớp sinh tự động trong `/obj/` không đổi. `reports[].input_branches` giữ tổng
nhánh đầu vào (null nếu không có); `sha256` xác định báo cáo gốc không bị sửa.
Khi chuẩn hóa lần hai, tổng đầu vào thuộc báo cáo đã chuẩn hóa đó.

Tổng gốc Coverlet có thể gồm nhánh không có bản ghi dòng nguồn. Không được tự
bịa vị trí cho các nhánh này. Tỷ lệ sau chuẩn hóa có phạm vi hẹp hơn, được ghi
rõ; phải giữ cả hai báo cáo, không coi chênh lệch tỷ lệ là cải thiện kiểm thử và
không cộng các bộ kiểm thử chồng lặp thành tổng nhánh. Phạm vi dòng, danh sách
nguồn, chính sách loại trừ và ngưỡng kiểm tra dòng không đổi.

Fixture `scripts/tests/fixtures/coverlet-10.1.0-branches.xml` giữ phần tử dòng
thật trong khung tổng hợp có chú thích. Kiểm thử bao gồm kiểu chữ, số nhánh,
dữ liệu sai, loại nguồn sinh tự động và chuẩn hóa lặp lại.

Xem [kế hoạch và bằng chứng](../../plans/2026-10-15-branch-normalization.md).
