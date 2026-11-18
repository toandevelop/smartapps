# Nhập Excel có ánh xạ, xem trước và báo lỗi

Ngày triển khai: **17/11/2026**. Phạm vi FUN-04: nhập đồng bộ có xem trước trên backend, chính sách không nhập khi còn lỗi. Không phải import job nền hay hệ thống staging lưu tệp lâu dài.

## 1. Cách sử dụng

1. Mở một bảng dữ liệu → Tiện ích → Import dữ liệu Excel.
2. Chọn `.xlsx`, `.xls` hoặc `.csv`. Chọn sheet và số dòng tiêu đề nếu file có phần giới thiệu phía trên.
3. Kiểm tra ánh xạ từng cột Excel sang trường của bảng. Có thể bỏ qua cột; không ánh xạ nhiều cột nguồn vào cùng một trường đích. Hệ thống gợi ý theo mã cột, `excelHeader` và tên hiển thị, có bỏ dấu tiếng Việt khi so khớp.
4. Chọn định dạng số/ngày **dạng văn bản** và tùy chọn cập nhật khi trùng mã. Ô số Excel giữ giá trị số; ô ngày dạng serial được đọc theo hệ ngày 1900/1904 của workbook.
5. Bấm **Kiểm tra và xem trước**. Xem số dòng tạo mới/cập nhật/hợp lệ/lỗi, dữ liệu sau chuẩn hóa và lỗi ở đúng số dòng Excel gốc.
6. Có thể lọc chỉ dòng lỗi, chuyển trang preview hoặc tải báo cáo lỗi `.xlsx` gồm dòng nguồn, trường, mức độ và nội dung. Sửa file rồi chọn lại để kiểm tra lại.
7. Nút **Xác nhận nhập** chỉ bật khi preview không có dòng lỗi. Thay đổi file, sheet, header, mapping, định dạng hoặc chế độ cập nhật đều làm mất hiệu lực preview hiện tại.

**Quy tắc cập nhật:** trường không được ánh xạ giữ dữ liệu cũ; ô trống ở cột đã ánh xạ nghĩa là xóa giá trị. Các trường bắt buộc sau khi merge phải hợp lệ. Mã có số 0 đầu nên được lưu dưới dạng văn bản trong Excel. Lookup và danh mục dùng mã, chưa tự resolve từ tên hiển thị.

**Giới hạn bản đầu:** tệp tối đa 10 MB trên UI, tối đa 2.000 dòng dữ liệu/lần; sheet nguồn tối đa 10.000 dòng và 200 cột để xử lý trên trình duyệt. Dòng nguồn trống được bỏ qua, không cắt âm thầm dữ liệu vượt giới hạn. Backend giới hạn request 20 MB và tự kiểm tra số dòng.

## 2. API đã triển khai

### Xem trước

`POST /api/records/bulk-import/preview`

```json
{
  "appId": "app_demo",
  "moduleId": "mod_demo",
  "tableId": "tbl_demo",
  "upsertByIdentityKey": true,
  "numberFormat": "vi-VN",
  "dateFormat": "dd/MM/yyyy",
  "rowNumbers": [5, 9],
  "rows": [
    { "code": "NEW-01", "amount": "1.234,50", "date": "17/11/2026" },
    { "code": "OLD-01", "amount": "2.000,00" }
  ]
}
```

Mapping theo vị trí cột được áp dụng trên frontend trước khi gửi; backend chỉ nhận dictionary theo mã trường và kiểm tra allowlist metadata. Header trùng nhau vẫn phân biệt được theo vị trí cột. Khi API client không gửi `rowNumbers`, server dùng số thứ tự từ dòng 2; khi có gửi phải đủ số lượng, dương và không trùng.

Response: `{ success: true, data: { rows, totalRows, errorRows, validRows, inserted, updated, canImport, fingerprint } }`. Mỗi dòng có `sourceRow`, `operation` (`CREATE`/`UPDATE`), `recordKey`, `data` đã tính/chuẩn hóa và `errors` theo trường. Một preview có lỗi dữ liệu vẫn trả HTTP 200 với `canImport=false`; lỗi request/quyền trả status tương ứng.

### Xác nhận

`POST /api/records/bulk-import`

Gửi lại đúng payload đã kiểm tra cùng `previewFingerprint` nhận từ bước xem trước. Endpoint này nay yêu cầu preview; caller cũ gửi trực tiếp không có fingerprint sẽ nhận 400 và cần cập nhật luồng gọi.

Backend tạo lại toàn bộ kế hoạch nhập, xác minh lại quyền và quy tắc rồi so fingerprint. Fingerprint là hash phục vụ phát hiện thay đổi, không phải token cấp quyền. Hash bao gồm request, schema, metadata bảng, danh mục, quyền hiệu lực, bản ghi đích và kết quả preview. Không lưu bản preview trong RAM server nên restart không làm mất một registry preview; nếu trạng thái đã đổi, client phải kiểm tra lại.

| Status | Ý nghĩa |
|---|---|
| 200 | Xem trước hoặc nhập thành công |
| 400 | Payload sai, thiếu preview hoặc còn dòng lỗi; phản hồi validation có preview chi tiết khi khả dụng |
| 401 | Chưa đăng nhập |
| 403 | Thiếu quyền đọc/nhập hoặc quyền hiện tại không còn cho phép |
| 404 | Bảng không tồn tại/không hoạt động |
| 409 | Preview cũ, xung đột đồng thời hoặc constraint; cần kiểm tra lại |

Thành công commit giữ response counters tương thích: `data.inserted`, `data.updated`, `data.errorRows` (bằng 0 vì lô lỗi không được nhập).

## 3. Validation và quyền

- App/module/table phải khớp metadata thực; không dùng App ID do client gửi để vượt quyền của bảng khác.
- Cần quyền xem và tạo tại bảng. Dòng upsert cần thêm quyền sửa và phải thuộc phạm vi dòng hiện tại.
- Chặn sửa bản ghi APPROVED với user không phải admin, như luồng upsert thông thường.
- Trường không có trong metadata, bị ẩn, chỉ đọc theo role hoặc có công thức không được nhập trực tiếp. Preview không trả dữ liệu ẩn hoặc dữ liệu cũ của bản ghi ngoài phạm vi.
- Kiểm tra trường bắt buộc, NUMBER/CURRENCY theo định dạng đã chọn, DATE chính xác, BOOLEAN, email, regex và SUB_TABLE là mảng JSON đối tượng.
- Danh mục phải dùng mã hợp lệ; lookup phải tồn tại và thuộc bảng/phạm vi được phép đọc. Có cache trong mỗi lần kiểm tra để tránh tra lại cùng giá trị.
- Mã định danh trùng trong file đánh dấu lỗi cho các dòng liên quan. Mã đã có nhưng tắt upsert hoặc thiếu quyền sửa cũng là lỗi.
- Cột AUTO_NUMBER được sinh và hiển thị ở preview nếu bỏ trống; việc sinh lại tại commit phải cho cùng kế hoạch để fingerprint còn hợp lệ. Đây chưa thay thế yêu cầu sequence/constraint toàn nền tảng trong DEV-02.
- Công thức được tính sau merge, sau đó áp dụng công thức liên App của RecordService. Đã sửa chuyển đổi số trong DynamicComputeService sang invariant để tránh locale máy làm sai decimal, có regression test `vi-VN`.

## 4. Transaction và khả năng nhập lại

Preview chỉ đọc. Commit dùng transaction SERIALIZABLE, advisory lock theo bảng và khóa các record đích trước khi đánh giá lại. Ghi record và audit trong cùng transaction. Có một dòng lỗi hoặc fingerprint lệch thì không ghi dòng nào. Sau commit phát realtime để runtime tải lại.

Không tự gửi lại khi kết nối gián đoạn: client làm mất trạng thái cho phép commit và yêu cầu đối chiếu/kiểm tra lại. Replay cùng preview cũ và hai commit đồng thời đã được kiểm thử để không tạo thêm một lô trùng trong các kịch bản thử. Đây không phải job bền vững có idempotency key/lịch sử retry; không hứa đảm bảo exactly-once cho mọi client/network pattern.

Import hiện không dispatch automation theo từng dòng, phù hợp hành vi bulk-import trước đó. Để thống nhất hoàn toàn mọi đường ghi và tác vụ ngoài cần tiếp tục DEV-01/DEV-04; không thêm gọi HTTP hay gửi thông báo ngoài vào giữa transaction nhập.

## 5. Kiểm thử và vận hành

Bộ test bổ sung nằm ở `ExcelImportTests.cs`, `ExcelImportPostgresTests.cs`, regression decimal trong `DynamicComputeServiceTests.cs` và `frontend-next/tests/excel-import.test.mjs`.

Các kịch bản: mapping trùng/không hợp lệ, nhiều sheet, header không ở dòng 1, dòng trống, giữ số dòng nguồn, số có locale, ngày không hợp lệ, boolean, email, required, danh mục/lookup sai, identity trùng, bản ghi khóa, dữ liệu ngoài phạm vi, readonly/hidden, giữ trường không map, audit, preview không ghi, toàn bộ lô bị chặn, dữ liệu/schema/quyền thay đổi sau preview, preview bắt buộc, AUTO_NUMBER và commit đồng thời.

Kết quả: **53/53 backend tests đạt, 0 skipped**, **10/10 frontend tests đạt**. Có thêm regression cho mã mặc định trùng, App/module giả và CSV UTF-8 giữ mã `001`, ngày/số dạng văn bản trước chuẩn hóa. TypeScript đã đạt; trạng thái production build cuối cùng nằm trong [verification.md](verification.md).

Chạy backend với PostgreSQL thử riêng theo hướng dẫn `SMARTAPPS_TEST_POSTGRES` ở [đợt 1](priority-features-implementation.md); frontend dùng `npm test`, `npx tsc --noEmit`, `npm run build`. Kết quả hoàn tất của đợt này được ghi ở mục cập nhật trong [verification.md](verification.md).

Đợt triển khai ban đầu đã thử mở UI trên môi trường riêng nhưng gặp timeout/lỗi resolve Next.js qua junction, nên khi đó chưa xác minh browser đầu cuối. Hiện đã bổ sung [5 E2E nhập và 3 E2E xuất Excel](excel-e2e.md), chạy bằng Chromium thật trên production build và đưa vào workflow CI cùng bộ form.

Đối chiếu cách dùng array/header/raw với [SheetJS Array utilities](https://docs.sheetjs.com/docs/api/utilities/array/) và hệ ngày với [SheetJS Dates and Times](https://docs.sheetjs.com/docs/csf/features/dates/). Repo vẫn dùng dependency hiện tại; các API đọc sheet/serial đã được kiểm tra bằng bản ESM được cài trong dự án.
