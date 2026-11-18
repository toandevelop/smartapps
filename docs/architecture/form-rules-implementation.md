# Form có điều kiện — cấu hình và triển khai

Ngày: **17/11/2026**. Đã triển khai FUN-01 phần quy tắc hiện/ẩn, bắt buộc và chỉ đọc; chưa phải trình thiết kế layout hoặc hệ thống phát hành metadata có phiên bản.

## 1. Cấu hình trong Studio

Mở **Studio → App → Phân hệ → Bảng → Sửa Bảng Động → Quy tắc form có điều kiện**. Cần có ít nhất hai cột trước khi thêm quy tắc.

Mỗi quy tắc gồm:

- Trường đích và thuộc tính: **Hiển thị**, **Bắt buộc nhập** hoặc **Chỉ đọc**.
- Kết quả khi thỏa điều kiện: Có/Không.
- Kết quả nếu không thỏa: Có/Không hoặc **Mặc định của cột**.
- Từ 1 đến 10 điều kiện; chọn **Thỏa tất cả (AND)** hoặc **Thỏa một điều kiện (OR)**.

Ví dụ:

| Mục tiêu | Điều kiện | Thuộc tính | Khi thỏa | Nếu không |
|---|---|---|---|---|
| Chỉ hiện mã số thuế cho khách hàng doanh nghiệp | `loai_khach_hang EQUALS DOANH_NGHIEP` | Hiển thị `ma_so_thue` | Có | Không |
| Yêu cầu giải trình phiếu lớn | `so_tien GREATER_OR_EQUAL 10000000` | Bắt buộc `giai_trinh` | Có | Không |
| Khóa nội dung khi trạng thái đã chốt | `trang_thai EQUALS DA_CHOT` | Chỉ đọc `noi_dung` | Có | Không |

Để “chỉ hiện khi điều kiện đúng”, cần đặt **Nếu không = Không**. Nếu chọn Mặc định và cột vốn đang hiện, khi không thỏa cột vẫn hiện theo cấu hình gốc.

Giá trị SELECT/LOOKUP dùng mã lưu thực tế. Số so sánh dùng dấu chấm thập phân; ngày dùng `yyyy-MM-dd`; boolean chọn true/false. Không nhập biểu thức JavaScript hay công thức vào ô giá trị điều kiện.

## 2. Ngữ nghĩa khi nhập và sửa dữ liệu

- Trạng thái được tính lại từ dữ liệu và cấu hình cột mỗi lần giá trị thay đổi, không giữ trạng thái “bắt buộc/chỉ đọc” cũ khi điều kiện đã ngừng đúng.
- Cột tắt `ShowInForm` ở cấu hình tĩnh vẫn không hiện. Quyền ẩn/chỉ đọc theo vai trò và trường gắn tài khoản vẫn có hiệu lực.
- Khi cột chuyển sang ẩn hoặc chỉ đọc, form dùng giá trị **đã lưu** hoặc **mặc định máy chủ** cho cột đó. Thay đổi chưa lưu ở cột bị khóa/ẩn được bỏ; các quy tắc phụ thuộc tính lại trên giá trị được giữ.
- Lúc gửi, UI bỏ các trường bị ẩn/chỉ đọc khỏi payload thay vì xóa dữ liệu cũ. Backend merge với dữ liệu đã lưu; trường bị bỏ khỏi request cập nhật được giữ nguyên, kể cả khi Studio vừa gỡ quy tắc mà form đang mở vẫn dùng metadata cũ. Dùng null/chuỗi rỗng để xóa giá trị có chủ ý khi được phép.
- Trường đang ẩn không bị bắt buộc nhập. Trường vừa bắt buộc vừa chỉ đọc phải có giá trị hợp lệ; cần thiết kế default hoặc điều kiện phù hợp để người dùng hoàn tất form.
- Đối với sửa bản ghi, chỉ đọc được kiểm tra trên **cả trạng thái đã lưu và trạng thái mới**. Muốn mở khóa rồi sửa, lưu thay đổi điều kiện mở khóa trước, sau đó mở lại bản ghi để sửa trường. Không cho sửa đồng thời giá trị đang khóa và điều kiện để vượt quy tắc.
- Trường chủ sở hữu/nhân viên do backend gắn theo tài khoản giữ giá trị máy chủ quyết định, kể cả khi form ẩn trường đó. Client không được chỉ định danh sách trường đáng tin cậy này.

Quyền đọc dữ liệu vẫn do RBAC/FLS hiện có quyết định. Form runtime, form thêm/sửa trong Studio và public form đều dùng cùng bộ quy tắc khi hiển thị.

## 3. Ràng buộc cấu hình

Tối đa 50 quy tắc/bảng; ID không trùng. Mỗi trường chỉ có một quy tắc cho từng thuộc tính để tránh kết quả phụ thuộc thứ tự. Có thể có riêng một quy tắc hiện/ẩn và một quy tắc bắt buộc cho cùng trường.

Điều kiện phải dùng trường khác với trường đích. Trường điều kiện phải được bật hiển thị form, không bị ẩn theo vai trò, không phải cột công thức, AUTO_NUMBER, FILE/IMAGE hoặc SUB_TABLE. Các nguồn số, ngày, boolean và văn bản/SELECT/LOOKUP được hỗ trợ. Cột công thức/tự sinh không cần quy tắc chỉ đọc bổ sung.

Toán tử: EQUALS, NOT_EQUALS, CONTAINS cho văn bản, GREATER_THAN/GREATER_OR_EQUAL/LESS_THAN/LESS_OR_EQUAL cho số/ngày, IS_EMPTY và NOT_EMPTY. `false` và `0` là giá trị có dữ liệu, không phải ô trống. Điều kiện văn bản so sánh không phân biệt hoa/thường.

Backend từ chối cấu hình sai, tên trường không tồn tại, tự tham chiếu hoặc thuộc tính bị cấu hình trùng. Không cho xóa/đổi cột theo cách làm hỏng quy tắc còn dùng cột đó; cần sửa hoặc gỡ quy tắc liên quan trước.

## 4. Lưu trữ và API

Dùng cột JSONB hiện có `sys_tables.ui_config.formRules`; không thêm schema database. API trả thêm `formRules` trên DTO bảng, cây runtime và định nghĩa public form.

`POST /api/tables` nhận thêm `formRules`. Quyền sửa vẫn là `studio.manage`:

```json
{
  "id": "tbl_example",
  "appId": "app_example",
  "moduleId": "mod_example",
  "tableName": "example",
  "displayName": "Bảng ví dụ",
  "formRules": [
    {
      "id": "require-explanation",
      "targetField": "giai_trinh",
      "property": "required",
      "value": true,
      "elseValue": false,
      "match": "ALL",
      "conditions": [
        { "field": "so_tien", "operator": "GREATER_OR_EQUAL", "value": "10000000" }
      ]
    }
  ]
}
```

`formRules` bỏ qua/null giữ nguyên cấu hình cũ; `[]` xóa các quy tắc. Các key khác trong `ui_config` được giữ. Tạo cột trước rồi mới cấu hình quy tắc có tham chiếu cột. Import/clone/upgrade blueprint lưu quy tắc sau khi tạo/cập nhật các cột; đồng thời giữ `readOnlyForRoles` của cột.

Cấu hình có hiệu lực ngay cho request backend tiếp theo. Trang runtime đang mở cần tải lại metadata để thấy cấu hình mới. Chưa có draft/publish/version cho quy tắc; dữ liệu cũ không bị ghi lại hàng loạt khi lưu cấu hình. Các yêu cầu mới được kiểm tra ở lần lưu hoặc chuyển bước nghiệp vụ tiếp theo.

## 5. Kiểm tra backend

`FormRuleService` là bộ đánh giá/kiểm tra dùng chung. `DynamicComputeService` nhận quy tắc để tính trạng thái bắt buộc theo dữ liệu sau công thức. Backend kiểm tra kiểu của trường nguồn để giá trị số/boolean/ngày sai không biến điều kiện thành false nhằm bỏ qua yêu cầu.

| Đường ghi | Hành vi |
|---|---|
| Lưu record thông thường | Merge dữ liệu/default, tính công thức, kiểm tra điều kiện và từ chối thay đổi bị khóa/ẩn |
| Sửa một ô | Đi qua kiểm tra lưu record, không chỉ tắt input ở bảng |
| Public form | Trả định nghĩa quy tắc; giới hạn trường public được nhập; kiểm tra trước INSERT |
| External API key | Kiểm tra cùng service trước ghi |
| Excel preview/commit | Báo lỗi theo dòng; quy tắc thay đổi làm mất hiệu lực fingerprint cũ |
| Automation CREATE_RECORD/UPDATE_RECORD/UPDATE_SELF | Kiểm tra dữ liệu đích; cập nhật nhiều dòng trong một transaction; ghi FAILED nếu không hợp lệ |
| Gửi duyệt/phê duyệt | Kiểm tra yêu cầu dữ liệu ở trạng thái kết quả; trường đồng bộ trạng thái workflow do hệ thống cập nhật theo quyền phê duyệt |

Automation thất bại vì quy tắc được ghi log. Bản ghi nguồn đã lưu thành công vẫn được báo lưu thành công, không trả lỗi giả rằng bản ghi nguồn chưa lưu.

Sai định nghĩa quy tắc trả **400**. Lưu dữ liệu vi phạm trả **422** với `success=false`, `message` và `errors: [{field,severity,message}]`; không ghi record vi phạm. Các giới hạn quyền và status API khác giữ nguyên. Nhập Excel tiếp tục trả lỗi theo contract preview/commit đã có.

Các validation dữ liệu khác của nền tảng vẫn theo cơ chế hiện hành; đợt này tập trung kiểm tra quy tắc form. Bộ query/filter và quyền truy cập vẫn là các cơ chế riêng của nền tảng.

## 6. Mã nguồn và kiểm thử

- Backend: [FormRuleService](../../backend-dotnet/Services/FormRuleService.cs), [exception filter](../../backend-dotnet/Controllers/FormRuleExceptionFilterAttribute.cs), Metadata/Record/Import/Automation services.
- Frontend: [bộ đánh giá chung](../../frontend-next/src/lib/form-rules.ts), [trình cấu hình Studio](../../frontend-next/src/app/studio/FormRulesEditor.tsx), RecordEditorModal và public form.
- [Bộ tình huống dùng chung frontend/backend](../../backend-dotnet.Tests/Fixtures/form-rule-cases.json): số, số lớn, ngày, boolean, rỗng, văn bản tiếng Việt.
- Backend unit/integration: FormRuleTests, FormRulePostgresTests và toàn bộ các test cũ. Frontend: `tests/form-rules.test.mjs` cùng các test trước.

Kịch bản gồm reset về baseline, AND/OR, required khi trường ẩn, bảo vệ trạng thái chỉ đọc cũ/mới, dữ liệu nguồn sai kiểu, không nới quyền vai trò, giữ giá trị nhân viên máy chủ gắn, cấu hình sai/xóa cột phụ thuộc, clone blueprint, public/API key/import và automation có target không hợp lệ.

Đợt triển khai ban đầu chưa kiểm thử browser. Đợt tiếp theo đã thao tác thực tế trên Chrome cho Studio, form nhập liệu và public form, sửa các lỗi mặc định/khởi tạo lại form và bổ sung bằng chứng tại [kiểm thử giao diện](form-rules-ui-verification.md). Hiện đã có [9 Playwright E2E và workflow CI](conditional-form-e2e.md). Xem [verification.md](verification.md) cho kết quả tự động mới nhất.
