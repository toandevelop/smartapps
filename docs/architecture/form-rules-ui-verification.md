# Kiểm thử giao diện form có điều kiện

Ngày kiểm tra: **17/11/2026**. Kiểm tra bằng Chrome trên bản chạy riêng từ mã nguồn hiện tại: frontend `127.0.0.1:5510`, API `127.0.0.1:5520`, PostgreSQL tạm `127.0.0.1:15432`. App kiểm thử `FORMQA` và database `smartapps_form_ui_20261117` được tạo riêng; không sửa dữ liệu của bản đang chạy tại cổng 4100/5200.

## Cấu hình đã thao tác qua Studio

Phiếu gồm mã phiếu, chế độ, số tiền, chi tiết, lý do và xác nhận. Tạo ba quy tắc bằng giao diện, lưu và mở lại:

- Hiện `chi tiết` khi `chế độ = show`, ngược lại ẩn.
- Bắt buộc `lý do` khi `số tiền >= 100` **OR** `xác nhận = false`.
- Chỉ đọc `chi tiết` khi `xác nhận = true`.

Mặc định ban đầu: chế độ `normal`, số tiền `0`, chi tiết `DEFAULT`, xác nhận `false`. Sau đó đổi mặc định xác nhận thành `1` để kiểm tra chuẩn hóa boolean giữa hai phía.

## Kết quả trên trình duyệt

| Kịch bản | Kết quả |
|---|---|
| Studio thêm ba loại quy tắc, chọn toán tử theo kiểu dữ liệu, thêm điều kiện OR | Lưu thành công, mở lại giữ đúng trường và chế độ ANY |
| Studio lưu hai quy tắc trùng trường đích/thuộc tính | Hiện thông báo lỗi từ backend, giữ màn hình và các sửa đổi; xóa quy tắc trùng rồi lưu được |
| Studio đang tải danh sách cột | Hiện trạng thái tải; chưa hiển thị trình sửa quy tắc với các lựa chọn rỗng |
| Studio thêm bản ghi thử, bỏ trống lý do khi điều kiện thỏa | Chặn lưu, hiện thông báo bắt buộc; bổ sung lý do thì lưu được |
| Form nhập liệu: đổi chế độ normal → show | Chi tiết xuất hiện với giá trị mặc định |
| Form nhập liệu: sửa nháp chi tiết rồi chuyển ẩn → hiện | Khôi phục giá trị đã lưu; không giữ thay đổi nháp ở trường bị ẩn |
| Form nhập liệu: bật xác nhận | Chi tiết bị khóa, khôi phục giá trị đã lưu/mặc định |
| Bản ghi đã khóa: tắt xác nhận trong form sửa | Chi tiết vẫn khóa trong lần lưu này; lưu trạng thái mở khóa rồi mở lại mới sửa được |
| Runtime sửa trường đang được phép nhập | Lưu thành công; API đọc lại nhận đúng `RUNTIME-SAVED` |
| Runtime tạo mới với mặc định boolean `1` | Công tắc bật, chi tiết chỉ đọc; số tiền 150 làm lý do bắt buộc; bổ sung lý do rồi lưu được |
| Public form: hiện/ẩn, chỉ đọc, bắt buộc theo cùng cấu hình | Trạng thái và giá trị tương ứng form nội bộ |
| Public form gửi số tiền thập phân 100.5 | Lưu thành công khi đủ lý do |
| Public form gửi thành công rồi chọn “Gửi phiếu khác” | Giữ đúng mặc định cho phiếu mới; đổi xác nhận làm trạng thái bắt buộc cập nhật đúng |
| Public form gắn nhãn trường | DOM có tên truy cập của mã phiếu, chế độ, số tiền, chi tiết, lý do và xác nhận |

## Lỗi đã phát hiện và sửa

1. **Mất mặc định sau lần gửi public form đầu tiên.** Trước sửa, “Gửi phiếu khác” tạo dữ liệu rỗng, điều kiện boolean không thỏa trên client nhưng backend thêm mặc định rồi trả 422. Sau sửa, mỗi phiếu mới được khởi tạo bằng `formDefaultValues` như lần mở đầu.
2. **Chuẩn hóa mặc định khác backend.** Trước sửa, boolean `1` bị biến thành false trên client, trong khi backend hiểu true. Hàm khởi tạo nay xử lý các giá trị boolean được backend hỗ trợ, bỏ khoảng trắng, xử lý số có dấu phân nhóm và bỏ mặc định trắng; không đổi mặc định của cột tính/tự sinh thành dữ liệu nhập. Các mặc định số vượt ngưỡng nguyên an toàn của JavaScript được giữ dạng chuỗi.
3. **Trường public form thiếu liên kết với nhãn.** Gắn `htmlFor`/`id` và tên truy cập của checkbox; có thể truy cập các trường bằng nhãn trên DOM.
4. **Studio hiển thị lựa chọn rỗng khi đang tải cột.** Chỉ hiện trình cấu hình sau khi tải thành công, tránh hiển thị thông báo tạo cột mới hoặc lựa chọn sai trong lúc chờ.
5. **Kết nối thông báo realtime bị ngắt sau khi nhận sự kiện.** Log trong lượt kiểm thử cho thấy `PeriodicTimer` bị chờ đồng thời ở `NotificationsController.Stream`. Mỗi vòng lặp tạo lại cả hai tác vụ dù tác vụ còn lại chưa hoàn tất. Đã giữ lại tác vụ đọc và heartbeat đang chờ, chỉ tạo tác vụ mới sau khi tác vụ tương ứng đã hoàn tất; kiểm tra kênh đóng bằng `await` để dừng đúng. Test HTTP tái hiện lỗi trước sửa với `HttpIOException: The response ended prematurely`; sau sửa kiểm tra nhận liên tiếp ba sự kiện, heartbeat 25 giây và sự kiện tiếp theo.

## Kiểm tra backend

Gửi HTTP trực tiếp tới API riêng, không dựa vào việc input bị vô hiệu hóa trên trình duyệt:

- Thiếu lý do bắt buộc, sửa chi tiết đang ẩn, sửa chi tiết chỉ đọc, số nguồn sai kiểu: **422** với trường lỗi tương ứng.
- Đổi nguồn sang trạng thái khóa và sửa trường đích trong cùng request: **422**.
- Quy tắc trùng thuộc tính, phần tử quy tắc null, danh sách điều kiện null, điều kiện tham chiếu chính trường đích: **400**.
- Số bản ghi không đổi sau các request bị từ chối; cấu hình hợp lệ trước đó vẫn có ba quy tắc.
- Các kiểm thử PostgreSQL/HTTP tự động bổ sung xác nhận mặc định `1` được áp dụng trước required/readOnly trên cả lưu nội bộ và hai lần gửi public; sửa trái phép bị từ chối, giá trị mặc định do backend giữ nguyên.

## Kiểm tra tự động và bằng chứng

| Kiểm tra | Kết quả |
|---|---|
| Backend xUnit, có PostgreSQL/HTTP và SSE qua heartbeat | **94/94**, không lỗi, không bỏ qua |
| Frontend Node tests | **42/42**, không lỗi |
| TypeScript `npx tsc --noEmit` | Đạt |
| Next production build | Đạt |

Lệnh backend dùng cấu hình output riêng để tránh khóa file của API đang chạy kiểm thử:

```powershell
$env:SMARTAPPS_TEST_POSTGRES='Host=127.0.0.1;Port=15432;Database=postgres;Username=postgres;Pooling=false'
dotnet test backend-dotnet.Tests/SmartApps.Api.Tests.csproj --no-restore --verbosity minimal -c FormUiVerify -p:UseAppHost=false
```

Dữ liệu mặc định dùng chung cho C# và TypeScript ở [form-default-cases.json](../../backend-dotnet.Tests/Fixtures/form-default-cases.json), bên cạnh bộ tình huống điều kiện đã có.

- [Ảnh cấu hình Studio](evidence/form-rules/studio.jpg).
- [Ảnh runtime chặn thiếu lý do và khóa chi tiết](evidence/form-rules/runtime-validation.jpg).
- [Ảnh public form gửi lần thứ hai thành công](evidence/form-rules/public-success.jpg).

Biên bản này ghi nhận đợt kiểm thử Chrome desktop bằng công cụ trình duyệt ban đầu. Đợt tiếp theo đã bổ sung [9 Playwright E2E và workflow CI](conditional-form-e2e.md), chạy lặp đạt 18/18. Chưa kiểm thử đa trình duyệt, tải đồng thời hoặc toàn bộ thiết bị di động.

Sau kiểm thử đã dừng API/frontend riêng, xóa database `smartapps_form_ui_20261117` và dừng PostgreSQL tạm. Các ảnh bằng chứng ở trên là file tĩnh, không phụ thuộc môi trường thử còn chạy.
