# Triển khai chức năng ưu tiên đợt 1

Ngày: **17/11/2026**. Đã triển khai trong mã, khác với các contract đề xuất ở tài liệu nghiên cứu trước.

## 1. Xuất Excel toàn bộ kết quả lọc

**Cập nhật kiểm thử:** đã có [E2E Excel tự động và CI](excel-e2e.md), xác minh file đủ 1.057 dòng theo search/bộ lọc/trạng thái/sort khi UI ở trang 2, cùng phạm vi sở hữu, cột ẩn và quyền export.

Nút **Xuất toàn bộ kết quả lọc (.xlsx)** trong tiện ích bảng gọi `GET /api/records/export`. Server dùng chung đường xử lý query, kiểm tra quyền App, quyền xem/xuất bảng, row scope, personal mode và cột bị ẩn theo role.

Tham số: `tableId` bắt buộc; `search`, `status`, `filters`, `sortField`, `sortDir`, `personalMode` như truy vấn danh sách. `filters` vẫn là JSON dictionary hiện hành, chưa phải cây AND/OR đề xuất. Không nhận page/limit cho export; service dùng một SELECT không có LIMIT/OFFSET để lấy toàn bộ kết quả và thêm ID làm khóa sắp xếp khi giá trị sort bằng nhau.

Response thành công là tệp `.xlsx`, không phải JSON. Mã từ chối gồm 401 khi chưa đăng nhập, 403 khi thiếu quyền, 404 khi bảng không tồn tại. Tệp dùng mã cột làm header để tương thích import; giá trị người dùng được ghi thành giá trị ô, không thực thi thành công thức. Trường ngoài metadata hoặc bị ẩn không được xuất. Không có kết quả vẫn có header.

`ExcelExportService` tạo Open XML bằng thư viện có sẵn của .NET; không cài dependency mới. Dữ liệu quá giới hạn dòng một sheet được chia sheet; chuỗi dài tuân giới hạn ô Excel. Các kiểu văn bản/ngày/lookup giữ giá trị chuỗi; number/currency hợp lệ được ghi dạng số.

Nút **Tải file Excel mẫu** trong import đã tách riêng, chỉ tạo header cột thay vì xuất các record đang hiển thị.

**Giới hạn:** đây là xuất đồng bộ; dữ liệu và workbook vẫn giữ trong bộ nhớ trong request. Chưa có job/progress/cancel hoặc benchmark cho tập rất lớn. Chính sách schema/quyền được đánh giá lúc xử lý request, chưa có hệ thống snapshot release metadata. Không cắt kết quả ở 500 dòng. FUN-05 còn phần chọn IDs/phạm vi, job và cột tùy chọn cho đợt sau.

## 2. Phân trang lịch sử chat

Endpoint hiện có `GET /api/chat/channels/{id}/messages` bổ sung `before` dạng cursor không trong suốt, nhận từ response trước. `limit` mặc định 80, giới hạn 1–200; frontend dùng 100 tin mỗi lần.

Giữ tương thích `data` là mảng tin nhắn; bổ sung:

```json
{
  "success": true,
  "data": [],
  "pagination": { "hasMore": false, "nextCursor": null }
}
```

Mỗi trang sắp xếp tăng dần `(created_at, id)` để hiển thị. Truy vấn cursor lấy các tin trước cặp thời điểm/ID của tin cũ nhất trên trang trước; giữ độ chính xác microsecond, không round-trip timestamp qua JavaScript Date. Lấy dư một tin để tính hasMore, không cần đếm toàn lịch sử.

Cursor gắn với channel, dữ liệu cursor không hợp lệ trả 400. Cursor chỉ dùng phân trang, không cấp quyền đọc; service kiểm tra kênh PUBLIC hoặc membership và trạng thái archived trước đọc. Kênh riêng không cho người ngoài đọc hoặc tự tạo membership qua endpoint đánh dấu đã đọc. Đọc trang cũ không tự đánh dấu những tin mới vừa tới là đã đọc.

UI có nút **Tải tin nhắn cũ hơn**, trạng thái đang tải, thông báo lỗi có thể thử lại, ghép trang loại trùng và giữ vị trí cuộn khi thêm tin cũ ở đầu. Refresh realtime không xóa phần lịch sử đã tải; kết quả request từ kênh cũ không ghi đè kênh đang xem. Tin xóa trong cửa sổ refresh hoặc qua sự kiện xóa được loại khỏi danh sách.

Index `idx_chat_msg_history(channel_id, created_at DESC, id DESC) WHERE deleted_at IS NULL` đã thêm vào schema SQL và initializer. Database đang chạy nhận index khi API khởi động lại theo cơ chế initializer hiện hành.

FUN-14 còn tìm kiếm nội dung, deep-link và sửa tin nhắn; các phần này chưa nằm trong đợt triển khai.

## 3. Thời hạn phiên từ cấu hình

`AuthService.CreateTokenAsync` đọc `workspace_config.sessionTimeoutDays` từ database mỗi lần cấp token. Giá trị được giới hạn 1–30 ngày, mặc định 7 khi chưa cấu hình. Đã áp dụng cho login mật khẩu, demo switch và các nhánh cấp token sau xác thực SSO.

Cookie lấy chính `exp` đã ký của token, tránh cookie và token hết hạn khác nhau. Logout giữ token trong bảng thu hồi đến đúng ngày hết hạn của token đó; không còn chỉ giữ 7 ngày đối với token 30 ngày.

**Hiệu lực:** thay đổi cấu hình áp dụng cho lần đăng nhập mới. Phiên đã phát hành giữ ngày hết hạn đã ký. Settings UI đã có hướng dẫn này. Chưa có UI thu hồi mọi phiên hoặc ép rút ngắn toàn bộ phiên đang tồn tại; đây vẫn là phần mở rộng của FUN-12.

## 4. Kiểm thử đã thực hiện

| Kiểm tra | Kết quả |
|---|---|
| .NET tests với PostgreSQL 16 riêng | **38/38 đạt**, không bỏ qua; gồm 3 integration tests qua HTTP/Kestrel và database thật |
| Frontend `npm test` | **4/4 đạt**, ghép trang, refresh, xóa và timestamp có microsecond |
| `npx tsc --noEmit` | Đạt |
| `npm run build` | Đạt production build |

Integration fixture tạo database tên ngẫu nhiên, chạy initializer, khởi động HTTP host rồi dọn database khi xong. Không dùng database nghiệp vụ. Các ca chính:

- Tập 1.230 record: danh sách 20 dòng, export đủ **599** dòng theo search/category/status/owner, đúng thứ tự, không có record xóa mềm hoặc cột ẩn; thiếu quyền export nhận 403, chưa đăng nhập nhận 401.
- **304** tin hợp lệ với cùng timestamp microsecond: lấy hết qua **4** trang, không trùng; tin mới giữa hai trang không làm lệch lịch sử; cursor sai nhận 400; người ngoài nhóm riêng nhận 403 cả read và mark-read.
- Cấu hình 30 ngày: login cookie khớp token; demo và nhánh cấp phiên SSO cùng thời hạn; logout lưu revocation đúng hạn. Đổi về 1 ngày chỉ tác động token mới, token cũ vẫn hợp lệ.
- Workbook ghi đủ hơn 500 dòng, giữ chuỗi bắt đầu bằng `=` thành văn bản, escape XML và ký tự Unicode đúng.

SSO test dùng nhánh cấp phiên nội bộ sau ánh xạ user, chưa đăng nhập qua nhà cung cấp SSO thật. Chưa smoke test giao diện bằng browser hoặc đo tải export lớn trong đợt này. Lint vẫn là vướng mắc cũ chưa được giải quyết ở phạm vi ba chức năng.

## 5. Chạy lại kiểm thử

Unit tests không cần database:

```powershell
Set-Location D:/smartapps
dotnet test ./backend-dotnet.Tests/SmartApps.Api.Tests.csproj
```

Ba integration tests được bỏ qua nếu chưa khai báo biến bên dưới. Chỉ trỏ tới PostgreSQL thử nghiệm có quyền tạo/xóa database; fixture tự tạo database riêng:

```powershell
$env:SMARTAPPS_TEST_POSTGRES = 'Host=127.0.0.1;Port=15432;Database=postgres;Username=postgres;Password=<mat-khau-test>'
dotnet test ./backend-dotnet.Tests/SmartApps.Api.Tests.csproj
```

Frontend:

```powershell
Set-Location D:/smartapps/frontend-next
npm test
npx tsc --noEmit
npm run build
```
