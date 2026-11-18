# Thiết kế đề xuất cho các chức năng bổ sung

Ngày: **17/11/2026**. Trạng thái: **đề xuất chưa triển khai**. Tên bảng, endpoint, payload và mã lỗi bên dưới là contract dự kiến, cần điều chỉnh cùng migration và API compatibility khi bắt đầu làm.

Nguồn yêu cầu: [feature-gap-analysis.md](feature-gap-analysis.md). Nền tảng kỹ thuật bắt buộc đi cùng: DEV-01/03/04/05/06 trong [development-roadmap.md](development-roadmap.md).

## 1. Nguyên tắc tích hợp

Giữ một backend ASP.NET Core; tách module nội bộ theo năng lực, chưa cần service triển khai riêng. Dùng một đường kiểm tra dữ liệu/quyền cho form, import, automation và external API. Mọi thao tác ghi cần actor, source, correlation ID và kiểm soát dữ liệu cũ khi phù hợp.

Các chức năng mới dùng ID ổn định và phiên bản. Không dùng `defaultValue` để mã hóa cả cấu trúc bảng con hoặc dùng JSON tùy ý không có schema cho rule nghiệp vụ.

| Module dự kiến | Trách nhiệm | Không nên gộp vào |
|---|---|---|
| RecordCommandService | Kiểm tra quyền, dữ liệu, concurrency và transaction ghi | UI hoặc từng adapter import/automation |
| RecordQueryService | Query contract, row/field policy, filter/sort | Từng nút export/chart |
| FormDefinition/RuleService | Schema form, điều kiện, dependency và validation | Evaluator tùy ý có thể chạy mã người dùng |
| MetadataReleaseService | Draft, validate, publish, compatibility | Hàm upsert cột đơn lẻ |
| Import/ExportService | Mapping, preview, commit, result và phạm vi file | Vòng fetch trong trang React |
| JobService/worker | Trạng thái tác vụ, retry, checkpoint và kết quả | Log chuỗi không có trạng thái rõ |

## 2. FUN-04: Import preview và commit

### Dữ liệu dự kiến

`app_import_jobs`: ID, table/app, actor, schema version, file reference/hash, sheet/header, mapping, options, trạng thái, counters, thời gian hết hạn và idempotency key.

`app_import_row_results`: job ID, số dòng gốc, operation dự kiến, record ID/version nếu cập nhật, normalized data hoặc tham chiếu staging, lỗi theo field và trạng thái commit. Chọn lưu bản ghi staging hoặc file staging theo kích thước đo thực tế; không bắt buộc lưu mỗi ô thành một dòng SQL.

Trạng thái: `UPLOADED → VALIDATING → PREVIEW_READY → COMMITTING → COMPLETED/PARTIAL/FAILED`; `EXPIRED` và `CANCELLED` cho tác vụ chưa hoàn tất. Tách lỗi toàn job với lỗi từng dòng.

### API dự kiến

| API | Ý nghĩa |
|---|---|
| `POST /api/import-jobs` | Upload/tạo job, chưa ghi record nghiệp vụ |
| `PUT /api/import-jobs/{id}/mapping` | Chọn sheet/header, mapping và chính sách nhập |
| `POST /api/import-jobs/{id}/validate` | Kiểm tra và tạo preview |
| `GET /api/import-jobs/{id}` | Trạng thái, counters và cảnh báo |
| `GET /api/import-jobs/{id}/rows` | Preview có phân trang |
| `POST /api/import-jobs/{id}/commit` | Ghi theo chính sách đã chọn, có idempotency |
| `GET /api/import-jobs/{id}/errors` | Tải lỗi theo số dòng/cột gốc |
| `POST /api/import-jobs/{id}/cancel` | Dừng phần có thể hủy |

Commit phải kiểm tra lại quyền, schema version và phiên bản record đích. Preview không giữ chỗ bản ghi vô thời hạn. Schema hoặc quyền thay đổi sau preview phải dẫn tới validate lại hoặc lỗi rõ, không dùng dữ liệu đánh giá cũ để ghi.

Ví dụ lỗi dự kiến:

```json
{
  "row": 7,
  "field": "nhan_vien",
  "code": "LOOKUP_OUT_OF_SCOPE",
  "message": "Nhân viên không thuộc đơn vị của phiếu.",
  "operation": "UPDATE"
}
```

### Quyết định nghiệp vụ trước triển khai

- Cho lưu dòng lỗi thành bản nháp, bỏ dòng lỗi hay chặn toàn bộ file?
- Dòng lặp trong chính file: báo lỗi hay ưu tiên dòng đầu/cuối?
- Cập nhật merge theo cột được map hay thay toàn bộ dữ liệu? Ô rỗng nghĩa là giữ cũ hay xóa?
- Phát automation theo từng dòng hay event batch? Không tự bỏ trigger chỉ để tăng tốc.
- Khi hủy sau khi đã ghi một phần, phần đã commit giữ nguyên hay có nghiệp vụ hoàn tác riêng?

### Nghiệm thu

Mapping cột tên Việt; ngày/số theo format khai báo; lookup ngoài phạm vi; file trùng; quyền bị thu hồi trước commit; record vừa được người khác sửa; record đã duyệt; restart giữa job; gửi commit lặp; tải lỗi không lộ dữ liệu ngoài quyền.

## 3. FUN-05: Xuất toàn bộ kết quả lọc

### Query contract dự kiến

```json
{
  "tableId": "tbl_demo",
  "scope": "FILTERED",
  "columns": ["ma_phieu", "so_tien"],
  "filter": {
    "and": [
      { "field": "so_tien", "op": "gte", "value": 1000000 },
      { "field": "ngay_lap", "op": "between", "value": ["2026-11-01", "2026-11-30"] }
    ]
  },
  "sort": [{ "field": "ngay_lap", "dir": "desc" }],
  "format": "xlsx"
}
```

Đây là version mới của query, không phải filter dictionary đang dùng. Cần schema validation, operator theo kiểu và giới hạn độ phức tạp. Server tự gắn policy; client không gửi SQL hoặc biểu thức quyền tùy ý.

API đề xuất: `POST /api/export-jobs`, `GET /api/export-jobs/{id}`, `GET /api/export-jobs/{id}/download`. Bản đầu có thể xử lý đồng bộ cho tập nhỏ sau khi đo; vẫn dùng cùng export query/policy.

Phải định nghĩa xuất dữ liệu tại thời điểm nào: tạo file trong snapshot database phù hợp hoặc ghi rõ thời điểm/phiên bản dữ liệu. Không giữ transaction kéo dài không giới hạn để hứa snapshot cho file rất lớn; cần đánh giá chi phí.

File kết quả có owner, query hash, row count, thời điểm tạo/hết hạn và cột đã chọn. Download kiểm tra quyền hiện tại; giảm quyền sau tạo job phải vô hiệu hóa file cũ hoặc tạo lại theo policy được chọn. Dữ liệu người dùng được xuất dưới dạng giá trị, không vô tình chuyển thành công thức spreadsheet.

**Nghiệm thu:** số dòng xuất khớp tập dữ liệu kiểm thử có nhiều trang; thứ tự ổn định khi trùng sort value; chọn IDs không lấy record ngoài quyền; trường ẩn không vào file; file lỗi/job hết hạn có thông báo rõ.

## 4. FUN-01/02/03: Schema form và quy tắc

### Cấu trúc đề xuất

`sys_form_definitions`: table ID, version, layout, rule definitions và child schemas. Có thể gộp metadata vào release payload thay vì tạo nhiều bảng ngay; quyết định dựa trên truy vấn và lịch sử cần lưu.

Ví dụ rule dự kiến:

```json
{
  "id": "rule_giai_trinh",
  "when": { "field": "so_tien", "op": "gt", "value": 10000000 },
  "effects": [
    { "kind": "visible", "field": "giai_trinh", "value": true },
    { "kind": "required", "field": "giai_trinh", "value": true }
  ],
  "message": "Phiếu vượt ngưỡng phải có giải trình."
}
```

Mỗi rule phải có hiệu ứng khi điều kiện không thỏa hoặc quy tắc reset thuộc tính về baseline được định nghĩa rõ. Không để UI giữ trạng thái required từ lần tính trước. Không dùng `eval` hoặc cho rule thực thi mã tùy ý.

Child schema phải khai báo kiểu từng cột, lookup, validation, công thức và row ID. Đối với lookup phụ thuộc, server nhận context field được allowlist, tính điều kiện theo metadata và kiểm tra quyền; không tin filter gửi từ browser.

### Thứ tự đánh giá

1. Nạp published schema và policy; chuẩn hóa dữ liệu theo kiểu.
2. Gán default khi được phép, đánh giá công thức theo dependency đã kiểm tra.
3. Tính required/validation nghiệp vụ theo điều kiện và validation bảng con.
4. Kiểm tra tham chiếu/unique theo transaction và constraint phù hợp.
5. Ghi qua RecordCommandService cùng audit/outbox; trả lỗi theo field path khi không hợp lệ.

Không cho phép formula/rule vòng hoặc rule thay đổi dữ liệu vô hạn. Khi có nhiều rule cùng ảnh hưởng thuộc tính, phải có quy tắc ưu tiên hoặc báo xung đột lúc publish. Quyền deny/field policy không bị rule form nới rộng.

**Nghiệm thu:** form preview và backend có cùng kết quả nghiệp vụ; field ẩn không tự mất giá trị trừ khi được khai báo; xung đột required/readOnly được phát hiện; rule không hợp lệ bị chặn publish; schema cũ vẫn đọc được dữ liệu cũ.

## 5. FUN-09: Phát hành metadata

### Dữ liệu và trạng thái đề xuất

`sys_app_releases`: app ID, release ID/version, metadata snapshot, checksum, trạng thái, creator/publisher, validation result và thời điểm publish. App trỏ `published_release_id`; draft có revision riêng để chống hai designer ghi đè.

Luồng: `DRAFT → VALIDATING → READY → PUBLISHED`; bản cũ thành `SUPERSEDED`. Không chuyển release cũ thành draft để sửa tại chỗ.

API dự kiến:

- `POST /api/apps/{id}/drafts` tạo draft từ release hiện tại.
- `POST /api/apps/{id}/drafts/{draftId}/validate` kiểm tra dependency, rule, lookup và ảnh hưởng dữ liệu.
- `GET /api/apps/{id}/drafts/{draftId}/diff` xem thay đổi so với release nền.
- `POST /api/apps/{id}/drafts/{draftId}/publish` kiểm tra revision/hash và quyền publish trước chuyển con trỏ.
- `GET /api/apps/{id}/releases` xem lịch sử.
- `POST /api/apps/{id}/releases/{releaseId}/activate` chỉ cho bản tương thích hoặc có migration được phê duyệt.

### Phân loại thay đổi

| Thay đổi | Xử lý đề xuất |
|---|---|
| Nhãn, màu, layout | Có thể publish sau validate; vẫn lưu lịch sử |
| Thêm cột tùy chọn | Kiểm tra default và khả năng đọc record cũ |
| Đổi kiểu, thêm bắt buộc hoặc unique | Chạy preflight dữ liệu, kế hoạch chuyển đổi, báo record lỗi |
| Xóa/đổi mã cột | Kiểm tra formula, view, widget, workflow, automation, import mapping và App khác |
| Đổi workflow | Hồ sơ đang chạy giữ snapshot/version của lần nộp |
| Đổi connection/secret | Mapping theo môi trường; không nhúng credential trong blueprint |

**Nghiệm thu:** draft không tác động runtime; hai designer publish cùng baseline được phát hiện xung đột; mất kết nối giữa publish không để App nửa phiên bản; clone remap lookup nội bộ; rollback không xóa dữ liệu mới ngoài ý muốn.

## 6. Thay đổi nhỏ có thể làm trước các module lớn

1. **Export:** ghi rõ phạm vi trang hiện tại trên UI; sau đó thêm lấy toàn bộ kết quả qua backend dùng đúng policy. Đổi nhãn chỉ giải quyết hiểu nhầm, chưa hoàn thành FUN-05.
2. **Chat:** thêm cursor `(created_at,id)` và tải tiếp lịch sử, giữ kiểm tra thành viên trước đọc. Tránh chỉ tăng LIMIT vì vẫn không có đường xem toàn bộ lịch sử.
3. **Session:** đưa cấu hình thời hạn vào lúc phát token và thiết kế hiệu lực đối với phiên đã tồn tại; bổ sung test. Không chỉ đổi giá trị mặc định trên màn hình settings.
4. **Import:** thêm chọn sheet/mapping và preview có validation server. Preview client riêng không chứng minh dữ liệu commit hợp lệ.

## 7. Phạm vi tài liệu

Chưa tạo migration, endpoint hay module nào trong tài liệu này. Trước khi code, chọn lát cắt và chốt những chính sách còn mở; không triển khai toàn bộ danh mục cùng lúc. Kiểm thử và định lượng hiệu năng phải được thực hiện trong thay đổi chức năng tương ứng.
