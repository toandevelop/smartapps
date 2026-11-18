# Nghiên cứu chức năng cần bổ sung

Ngày cập nhật: **17/11/2026**. Phạm vi: tiếp tục đối chiếu backend, frontend, DTO, schema và các API hiện có; nghiên cứu tài liệu chính thức của các nền tảng liên quan để xác định năng lực nên có ở SmartApps.

**Cập nhật sau nghiên cứu:** đã triển khai phần xuất đủ kết quả lọc của FUN-05, chat paging của FUN-14 và hiệu lực thời hạn phiên của FUN-12. Xem [triển khai đợt 1](priority-features-implementation.md). Bảng và bằng chứng dưới đây giữ baseline khảo sát để tra khoảng trống ban đầu; các phần mở rộng khác vẫn còn trong backlog.

**FUN-04:** đã triển khai nhập Excel với chọn sheet/header, mapping, preview backend, báo lỗi theo dòng và commit nguyên lô có kiểm tra lại. Xem [hướng dẫn nhập Excel](excel-import-implementation.md). Job nền, staging bền vững và policy nhập một phần vẫn là phần chưa làm.

**FUN-01:** đã triển khai hiện/ẩn, bắt buộc và chỉ đọc theo điều kiện, cấu hình qua Studio, dùng trong runtime/public form và kiểm tra ở backend. Xem [triển khai form có điều kiện](form-rules-implementation.md). Designer layout và phát hành metadata có phiên bản vẫn là các hạng mục tiếp theo.

## 1. Kết luận và cách xếp ưu tiên

Nền tảng có nhiều màn hình và chức năng cơ bản. Khoảng trống lớn là khả năng cấu hình một quy trình doanh nghiệp hoàn chỉnh: form có điều kiện, dữ liệu con có kiểu, quan hệ có ràng buộc, nhập/xuất có kiểm soát, vòng đời phê duyệt và phát hành App.

Đề xuất dưới đây tập trung vào **chức năng người dùng và người quản trị**. Các hạng mục bảo đảm transaction, concurrency, outbox, migration và CI đã có trong [development-roadmap.md](development-roadmap.md); không đánh số lại chúng như tính năng mới.

Quy ước:

- **A — cần bổ sung cho bản dùng nghiệp vụ rộng hơn:** có khoảng trống cụ thể trong luồng hiện tại hoặc là năng lực nền cho nhiều App.
- **B — bổ sung theo nghiệp vụ phát hành đầu tiên:** quan trọng nhưng phạm vi phụ thuộc quy trình thực tế.
- **C — chỉ thực hiện khi có nhu cầu xác nhận:** không đưa vào bản đầu theo mặc định.
- **Chưa thấy:** chưa tìm thấy một triển khai hoàn chỉnh trong các DTO, API và UI được khảo sát; không khẳng định mọi đoạn mã có tên gần giống đều không tồn tại.
- Tên bảng và API trong đề xuất là thiết kế tương lai, không phải contract đang chạy.

Giả định làm việc: SmartApps phục vụ nhiều phòng ban trong một doanh nghiệp, chưa mặc định là SaaS đa khách hàng. Ưu tiên có thể đổi khi phạm vi nghiệp vụ được chốt.

## 2. Danh mục 20 nhóm chức năng

| ID | Chức năng cần bổ sung | Hiện trạng có căn cứ trong mã | Ưu tiên |
|---|---|---|---|
| FUN-01 | Quy tắc biểu mẫu và bố cục form cấu hình được | Có bắt buộc/ẩn/chỉ đọc theo role; chưa thấy rule có điều kiện và designer bố cục | A |
| FUN-02 | Bảng con có schema và validation theo ô | SUB_TABLE lấy tên cột từ chuỗi defaultValue, render input chung | A |
| FUN-03 | Quan hệ dữ liệu, lookup phụ thuộc và khóa duy nhất cấu hình được | Có LOOKUP và identity key; chưa thấy bộ ràng buộc quan hệ/khóa ghép tổng quát | A |
| FUN-04 | Trình nhập Excel có mapping, preview và báo lỗi từng dòng | Đọc sheet đầu, gửi trực tiếp rows vào bulk-import, trả tổng số dòng lỗi | A |
| FUN-05 | Xuất toàn bộ dữ liệu theo bộ lọc và theo dõi tác vụ xuất | handleExportExcel xuất mảng records đang tải theo trang | A |
| FUN-06 | Bộ dựng truy vấn/view nâng cao | Có filters dạng dictionary chuỗi và saved views; chưa thấy cây điều kiện AND/OR | A |
| FUN-07 | Cơ cấu tổ chức và ownership cấu hình được | Có department, employeeCode, row-scope; logic owner có nhiều tên trường cố định | A |
| FUN-08 | Vòng đời hồ sơ phê duyệt và xử lý ngoại lệ | Có duyệt tuần tự, role/user, SLA và reject; chưa thấy recall/delegation/quorum | A cho thu hồi/version; B cho mở rộng |
| FUN-09 | Bản nháp, kiểm tra và phát hành phiên bản App | Có version/blueprint; chưa thấy luồng draft → validate → publish → rollback có quản lý | A khi Studio dùng trên dữ liệu thật |
| FUN-10 | Trung tâm vận hành automation và tác vụ | Có log, dry-run và scheduler; chưa có UI/job contract retry/cancel/progress tổng quát | A cho retry và trạng thái |
| FUN-11 | Phiên bản dữ liệu, so sánh và khôi phục có chọn lọc | Có audit trước/sau và thùng rác; chưa thấy API phục hồi một revision | B |
| FUN-12 | Quản lý vòng đời tài khoản và phiên đăng nhập | Có admin reset, đổi mật khẩu, SSO, thu hồi token hiện tại; chưa có recovery tự phục vụ/session UI | A cho hiệu lực cấu hình; B/C cho mở rộng |
| FUN-13 | Tìm kiếm toàn nền tảng có phân quyền | Có search theo bảng, lookup và tìm kênh/người; chưa có unified search | B |
| FUN-14 | Lịch sử chat có phân trang và tra cứu | Có reply/reaction/pin; ListMessages chỉ có limit, tối đa 200 | A cho paging; B cho tìm kiếm/chỉnh sửa |
| FUN-15 | Công việc định kỳ, việc con, phụ thuộc và giao việc theo nhóm | Có assignee, checklist, deadline, progress; chưa có model parent/dependency/recurrence | B |
| FUN-16 | Báo cáo nghiệp vụ và mẫu in có cấu hình | Có widget, analytics, in record A4/barcode; chưa thấy report designer và lịch gửi | B |
| FUN-17 | Quản lý tài liệu và vòng đời tệp | Có upload/download và metadata; chưa có file version, quota, cleanup và lifecycle UI | B |
| FUN-18 | Đồng bộ tích hợp có checkpoint và quản lý connector | Có API key read/write, webhook outbound; chưa có delta/cursor sync và connector lifecycle | B |
| FUN-19 | Cổng biểu mẫu ngoài có phiên gửi và theo dõi tiếp nhận | Có public form bật/tắt theo bảng; chưa có submit session/receipt/status link tổng quát | B khi mở form ngoài |
| FUN-20 | Múi giờ, lịch làm việc và chính sách tính hạn | Có DATE, timestamp, SLA giờ và job theo ngày; chưa thấy business calendar/timezone policy | A cho quy ước thời gian; B cho lịch làm việc |

## 3. Đặc tả bổ sung nhóm cần làm trước

### FUN-01 — Form và quy tắc nghiệp vụ

**Nguồn đối chiếu:** [SysTableDto/UpsertTableRequest và SysColumnDto](../../backend-dotnet/Models/PlatformModels.cs), [StudioModals](../../frontend-next/src/app/studio/StudioModals.tsx), [DynamicFieldInput](../../frontend-next/src/app/components/ui/DynamicFieldInput.tsx). `UiConfig` được đọc trong metadata nhưng request cập nhật bảng hiện chưa cung cấp một hợp đồng thiết kế form đầy đủ.

**Tình huống:** đề nghị mua sắm trên ngưỡng tiền phải có giải trình; chọn “nhà cung cấp mới” thì hiện nhóm thông tin đăng ký; tài khoản ngân hàng chỉ bắt buộc khi phương thức thanh toán là chuyển khoản.

**Phạm vi tối thiểu:** nhóm/section, số cột và thứ tự trường; điều kiện AND/OR có kiểu; visible/required/readOnly theo điều kiện; kiểm tra chéo trường; preview bằng dữ liệu mẫu và vai trò. Giá trị mặc định chỉ gán khi trường trống và không âm thầm ghi đè dữ liệu nhập.

Rule nghiệp vụ phải chạy tại backend cho form, import và API. Rule hiển thị chạy tại UI; ẩn trường không tạo ra quyền truy cập mới. Khi rule yêu cầu trường đang ẩn, designer phải cảnh báo xung đột.

**Nghiệm thu:** cùng request sai bị từ chối qua UI và API; đổi điều kiện cập nhật form ngay; thông báo lỗi trỏ đúng trường; preview không ghi dữ liệu thật. Cách phân biệt rule dữ liệu và rule hiển thị được tham khảo từ [Power Apps business rules](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/create-business-rules-recommendations-apply-logic-form).

### FUN-02 — Master–detail có kiểu

**Nguồn:** nhánh `SUB_TABLE` trong [DynamicFieldInput.tsx](../../frontend-next/src/app/components/ui/DynamicFieldInput.tsx), bắt đầu khoảng dòng 348. Cột chi tiết lấy từ chuỗi phân tách dấu phẩy và các ô dùng input chung.

**Tình huống:** phiếu mua hàng có sản phẩm LOOKUP, số lượng NUMBER dương, đơn giá CURRENCY, thuế SELECT và thành tiền tính tự động.

**Phạm vi tối thiểu:** schema con gồm code/type/required/formula/lookup; ID ổn định cho từng dòng; lỗi theo đường dẫn `chi_tiet[2].so_luong`; giới hạn số dòng; tính lại tổng; thêm/xóa/sắp xếp dòng theo quyền. Bản đầu có thể tiếp tục lưu JSONB nếu không cần truy vấn/quyền riêng từng dòng.

**Nghiệm thu:** nhập chữ vào số lượng bị báo lỗi đúng ô; lookup con có kiểm tra tham chiếu; sửa dòng cập nhật tổng; import/export giữ đúng kiểu và ID dòng. Chuyển sang bảng quan hệ riêng chỉ khi cần báo cáo hoặc quyền cấp dòng con.

### FUN-03 — Ràng buộc và lookup phụ thuộc

**Nguồn:** thuộc tính `RefTableId`, `RefValueField`, `IsIdentityKey` trong [Models](../../backend-dotnet/Models/PlatformModels.cs); [lookup-service.ts](../../frontend-next/src/lib/lookup-service.ts); `GetLookupOptionsAsync` và kiểm tra identity trong [RecordService](../../backend-dotnet/Services/RecordService.cs).

**Tình huống:** chọn phòng ban thì chỉ hiện nhân viên của phòng ban; không được chọn hợp đồng thuộc khách hàng khác; mã chứng từ duy nhất theo đơn vị và năm.

**Phạm vi tối thiểu:** lọc lookup theo context form, kiểm tra server cùng điều kiện, chính sách tham chiếu khi xóa, khóa duy nhất đơn hoặc ghép, cảnh báo trùng theo dữ liệu chuẩn hóa. Phải phân biệt constraint duy nhất với gợi ý dữ liệu nghi trùng; không tự gộp bản ghi khi chỉ gần giống.

**Nghiệm thu:** gửi trực tiếp ID ngoài phạm vi lookup cũng bị từ chối; xóa bản ghi đang được tham chiếu tuân chính sách; hai request tạo cùng khóa không cùng thành công. Tham khảo phân biệt nghiệp vụ dò trùng trong [Power Platform duplicate detection](https://learn.microsoft.com/en-us/power-platform/admin/detect-duplicate-data); xử lý constraint là thiết kế riêng của SmartApps.

### FUN-04 — Import có kiểm soát

**Nguồn:** `handleImportExcel` tại [runtime page](../../frontend-next/src/app/apps/[appCode]/page.tsx), `BulkImportAsync` tại [RecordService](../../backend-dotnet/Services/RecordService.cs). Hiện đọc sheet đầu, không có bước preview/mapping rõ ràng trước ghi.

**Phạm vi tối thiểu:** chọn sheet/header, mapping cột, chuẩn hóa ngày/số, resolve danh mục/lookup, preview kết quả; tải file lỗi theo dòng/cột; chọn create-only/upsert; xem trước ảnh hưởng các dòng cập nhật. Phải chốt chính sách all-or-nothing hoặc ghi các dòng hợp lệ; không âm thầm quyết định thay người dùng.

**Nghiệm thu:** file sai cột không ghi dữ liệu; người dùng biết số dòng tạo/sửa/bỏ qua/lỗi trước commit; lỗi trả về có dòng gốc và lý do; dùng lại cùng import request không tạo trùng; bản ghi đã duyệt vẫn chịu quy tắc khóa. File lớn có job/progress và giới hạn cấu hình, không ấn định ngưỡng khi chưa đo.

### FUN-05 — Export theo đúng phạm vi người dùng chọn

**Nguồn:** `loadRecords` dòng khoảng 226 tải một trang; `handleExportExcel` khoảng 447 duyệt chính `records` trong [runtime page](../../frontend-next/src/app/apps/[appCode]/page.tsx).

**Phạm vi tối thiểu:** tùy chọn “trang hiện tại”, “bản ghi đã chọn”, “toàn bộ kết quả lọc”; hiển thị rõ phạm vi và số dòng; giữ filter/sort; chọn cột và cách xuất mã/nhãn; snapshot truy vấn tại lúc bắt đầu job. Server luôn thực thi lại quyền App/bảng/dòng/trường và quyền export.

**Nghiệm thu:** dữ liệu có nhiều trang vẫn xuất đủ khi chọn toàn bộ; trường ẩn không lọt vào file; thu hồi quyền trước khi tải file được kiểm tra lại; file tạm hết hạn. Đây là khoảng trống đã xác nhận bằng đường dữ liệu trong mã, chưa cần benchmark để xác định nhu cầu sửa.

### FUN-06 — Query/view có ngữ nghĩa rõ

**Nguồn:** `QueryRecordsAsync` nhận `filtersJson` dạng `Dictionary<string,string>`; [saved view service](../../backend-dotnet/Services/RecordService.cs) đã lưu cấu hình view.

**Phạm vi tối thiểu:** operator theo kiểu (`between`, `in`, rỗng, khoảng ngày), nhóm AND/OR, nhiều mức sort, lựa chọn/thứ tự cột, group và tổng hợp cơ bản. View cá nhân hoặc chia sẻ có owner và quyền sửa rõ ràng. View không cấp thêm quyền đọc dữ liệu.

**Nghiệm thu:** UI preview, danh sách, count, dashboard drill-down và export cùng một query contract; operator sai kiểu bị báo lỗi; chuỗi tìm kiếm không bị dùng làm SQL thô; có giới hạn độ sâu và số điều kiện.

### FUN-07 — Cơ cấu tổ chức và phạm vi dữ liệu

**Nguồn:** user hiện có `department` và `employeeCode`; `OwnerFilterSqlClause` trong [RecordService](../../backend-dotnet/Services/RecordService.cs) gắn nhiều tên cột nhân sự cụ thể.

**Phạm vi tối thiểu:** đơn vị/phòng ban có ID ổn định, quan hệ cha–con, người quản lý và thành viên; bảng động cấu hình trường owner/đơn vị; chính sách own/team/department/subtree; đổi tên phòng ban không làm hỏng quyền.

**Nghiệm thu:** đổi mã/nhãn phòng ban không mở rộng quyền ngoài ý muốn; nhân viên chuyển phòng có chính sách dữ liệu cũ rõ; người quản lý chỉ duyệt trong phạm vi; có công cụ “xem quyền hiệu lực” giải thích quyền đến từ role và phạm vi nào. Phụ thuộc DEV-05 và migration; không đồng nhất org unit với tenant.

### FUN-08 — Vòng đời phê duyệt

**Nguồn:** [WorkflowStepDto](../../backend-dotnet/Models/PlatformModels.cs) có thứ tự, approver roles/users, SlaHours, RequireCommentOnReject; [WorkflowService](../../backend-dotnet/Services/WorkflowService.cs) và approval trong [RecordService](../../backend-dotnet/Services/RecordService.cs) xử lý bước tuyến tính.

**Bản tối thiểu cần:** thu hồi trước khi đã có quyết định theo chính sách, hủy hồ sơ có lý do, nộp lại hồ sơ bị từ chối, lưu phiên bản workflow và dữ liệu áp dụng tại lần nộp. Sửa workflow không đổi ngầm quy trình của hồ sơ đang chờ.

**Mở rộng theo nghiệp vụ:** ủy quyền có thời hạn/phạm vi, người quản lý trực tiếp, nhánh theo điều kiện, nhiều người cùng duyệt với chế độ ALL/ANY/quorum, escalations và người thay thế. SLA theo giờ làm việc đi cùng FUN-20.

**Nghiệm thu:** có sơ đồ ai đang giữ hồ sơ và lịch sử từng hành động; không tự duyệt nếu chính sách cấm; không duyệt ngoài thời hạn ủy quyền; mọi thu hồi/hủy/đổi người có audit. Các chế độ nhiều người duyệt được tham khảo từ [Power Automate approvals](https://learn.microsoft.com/en-us/power-automate/get-started-approvals); không mặc định đem toàn bộ vào bản đầu.

### FUN-09 — Quản lý phát hành App

**Nguồn:** [MetadataService](../../backend-dotnet/Services/MetadataService.cs) có export/import/upgrade blueprint và ghi cấu trúc App trực tiếp; chưa thấy model release riêng.

**Phạm vi tối thiểu:** working draft, preview, kiểm tra dependency và schema/data impact, publish thành phiên bản bất biến, phân biệt quyền thiết kế với quyền phát hành. Cấu hình URL/connection/secret tách theo môi trường; package không mang credential thật.

**Nghiệm thu:** runtime chỉ dùng bản published; draft lỗi không ảnh hưởng user; publish nửa chừng không để lẫn phiên bản; rollback metadata chỉ cho phép khi tương thích dữ liệu hoặc có migration khôi phục tương ứng. Tham khảo khái niệm package và phụ thuộc trong [Power Platform solutions/ALM](https://learn.microsoft.com/en-us/power-platform/alm/solution-concepts-alm).

### FUN-10 — Trung tâm tác vụ và automation

**Nguồn:** [AutomationService](../../backend-dotnet/Services/AutomationService.cs) có log/dry-run; [ScheduledReminderService](../../backend-dotnet/Services/ScheduledReminderService.cs) chạy chu kỳ 6 giờ; chưa có execution state machine chung cho import/export/automation.

**Phạm vi tối thiểu:** xem running/succeeded/failed/waiting/cancelled, tiến độ, đầu vào đã che thông tin nhạy cảm, lỗi, số lần thử và lịch sử. Retry có quyền riêng; chọn phiên bản rule gốc hoặc phiên bản mới có xác nhận phạm vi. Tác vụ nhiều bước lưu checkpoint; cancel chỉ dừng phần chưa commit.

**Nghiệm thu:** người vận hành biết bản ghi đã ghi hay chưa khi thông báo ngoài lỗi; retry không tạo bản ghi trùng; restart giữ trạng thái; tác vụ không có quyền xem không xuất hiện. Mô hình quan sát và retry tham khảo từ [n8n executions](https://docs.n8n.io/workflows/executions/all-executions/), với idempotency/outbox của SmartApps được thực hiện ở DEV-04.

## 4. Các nhóm bổ sung tiếp theo

| ID | Phạm vi tối thiểu đề xuất | Nghiệm thu và lưu ý |
|---|---|---|
| FUN-11 | Diff dữ liệu theo phiên bản, chọn trường khôi phục, tạo revision mới thay vì xóa lịch sử | Bản khôi phục qua validation/quyền hiện tại; hồ sơ đã duyệt phải theo chính sách; audit không đồng nghĩa undo đã có |
| FUN-12 | Danh sách thiết bị/phiên, đăng xuất từng phiên/tất cả, cấu hình thời hạn có hiệu lực; invite/đổi mật khẩu lần đầu | Hiện ForgotPasswordModal chỉ hướng dẫn liên hệ admin; recovery tự phục vụ chỉ cần nếu doanh nghiệp chọn local account. MFA có thể giao IdP khi dùng SSO bắt buộc. SessionTimeoutDays hiện được lưu nhưng CreateToken vẫn cố định 7 ngày |
| FUN-13 | Tìm record và task từ một ô search, có phạm vi App/loại, snippet và xếp hạng | Search phải lọc cả row/field trước khi tạo snippet/count. Có thể thử PostgreSQL trước; tiếng Việt có dấu/không dấu cần bộ dữ liệu đánh giá riêng |
| FUN-14 | Cursor `before` để xem tiếp tin cũ, tìm trong kênh, deep-link tin nhắn, sửa tin của mình kèm dấu đã sửa | Có reply/reaction/pin rồi; không gọi các phần này là thiếu. Tin cũ đọc theo quyền thành viên; cursor có tie-breaker ID; không coi lịch sử hiển thị đủ chỉ vì có LIMIT 200 |
| FUN-15 | Parent/subtask, dependency, recurrence, checklist có assignee, workload view | Sinh việc định kỳ không trùng; phát hiện vòng phụ thuộc; quy định phụ thuộc chặn chuyển trạng thái hay chỉ cảnh báo. Chỉ làm planning/Gantt khi nhóm thực sự cần |
| FUN-16 | Dataset/report definition có version, tham số, tổng hợp, mẫu in; lịch gửi theo quyền người nhận | Không thay thế widget hiện có. Kết quả report đối soát được với dữ liệu nguồn; phân biệt quyền người tạo lịch và người nhận file; thay template không sửa báo cáo lịch sử |
| FUN-17 | Gắn file với record/field/owner, quota, phiên bản file, thay thế, xóa và khôi phục theo chính sách | Xóa đường dẫn ở input hiện không đồng nghĩa xóa blob; tệp mồ côi phải được dọn có kiểm tra; quyền download gắn với record; storage adapter khi cần |
| FUN-18 | Service identity, scope chi tiết, rotation/expiry, integration logs, delta token có tombstone, connector cấu hình theo môi trường | API key hiện đã có read/write, không tạo lại tính năng đó. Delta không bỏ sót delete hoặc lộ record ngoài quyền; webhook inbound chỉ thêm khi có nguồn gửi thật |
| FUN-19 | Form public có thời gian hiệu lực, allowlist trường, quota/chống gửi lặp, biên nhận và liên kết trạng thái riêng | Không lộ dữ liệu hồ sơ khác qua tra cứu; bản nháp ngoài có token riêng; CAPTCHA/rate limit theo mức mở công khai và rủi ro thực tế |
| FUN-20 | Timezone hệ thống, date-only khác instant, lịch nghỉ/ca và SLA theo giờ làm việc | Hồ sơ trước/sau 00:00 Bangkok không bị tính nhầm ngày; công việc đến hạn vào ngày nghỉ có quy tắc; kiểm thử với clock có thể điều khiển |

Nền tảng tìm kiếm PostgreSQL hỗ trợ tài liệu/truy vấn và xếp hạng; đây là điểm khởi đầu có thể đánh giá cho FUN-13, chưa phải khẳng định tìm tiếng Việt sẽ đạt chất lượng mong muốn. Xem [PostgreSQL 16 Full Text Search](https://www.postgresql.org/docs/16/textsearch-intro.html).

## 5. Những tính năng chưa cần đưa vào ngay

AI dùng mô hình thật, PWA/offline đầy đủ, tenant SaaS, marketplace template, plugin SDK, chữ ký điện tử, kế toán/tính lương chuyên ngành và BPMN engine tổng quát chỉ nên lập dự án khi có nhu cầu rõ. Template “kho”, “CRM”, “nhân sự” hiện có không chứng minh các quy trình nghiệp vụ chuyên sâu như đối soát tồn kho hoặc tính lương đã hoàn thiện.

Không có số liệu để xếp mọi tính năng trong danh sách là bắt buộc cho mọi khách hàng. Nhóm A dựa trên khoảng trống trong năng lực nền hiện tại; nhóm B/C cần đối chiếu người dùng thật.

## 6. Lộ trình chức năng và phụ thuộc

| Đợt | Kết quả bàn giao | Phụ thuộc |
|---|---|---|
| 1 | Export đúng phạm vi; chat paging; session config có hiệu lực; import preview + file lỗi tối thiểu | DEV-01/05/08; dữ liệu đủ nhiều trang, ma trận quyền |
| 2 | Form rules, typed SUB_TABLE, lookup phụ thuộc và query contract | DEV-01/02/03/12; metadata version và migration rõ |
| 3 | Org/ownership, approval lifecycle, bản nháp/phát hành App | DEV-03/05/06/11; quyết định nghiệp vụ về người duyệt và dữ liệu cũ |
| 4 | Trung tâm jobs/retry; report/search/task nâng cao theo App phát hành | DEV-04/13; đo tải, chính sách retention và quyền tải kết quả |

Các đợt là thứ tự phụ thuộc, không phải ước lượng tuần/tháng. Chọn một quy trình thật để nghiệm thu xuyên suốt, ví dụ đề nghị mua sắm: nhập Excel → sửa form → lookup → trình duyệt → trả lại/nộp lại → duyệt → báo cáo/xuất đủ dữ liệu.

## 7. Thiết kế để có thể triển khai

Xem [functional-extension-design.md](functional-extension-design.md) để tra mô hình dữ liệu, API đề xuất và checklist cho bốn lát cắt đầu: import, export, form rules và phát hành metadata. Các contract trong tài liệu đó đều chưa được triển khai.

## 8. Phạm vi xác minh của đợt nghiên cứu này

Đã đọc bổ sung DTO bảng/cột/task/workflow, editor và runtime SUB_TABLE, lookup client/service, import/export runtime, chat paging, tài khoản và session config; tìm đối chiếu API/schema cho các khả năng mới. Đã nghiên cứu các nguồn chính thức được dẫn ngay tại từng đề xuất.

Không chạy lại build/tests vì thay đổi chỉ là tài liệu; kết quả thực thi trước vẫn ở [verification.md](verification.md). Chưa có phỏng vấn người dùng, benchmark mới hoặc xác minh browser đầu cuối. Danh sách thiếu là đánh giá từ phạm vi mã hiện có, không phải cam kết toàn bộ mục đã sẵn sàng triển khai mà không cần chốt nghiệp vụ.
