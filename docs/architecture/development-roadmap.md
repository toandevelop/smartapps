# Phân tích khoảng trống và kế hoạch phát triển

Ngày đánh giá: **17/11/2026**. Đây là backlog đề xuất từ đọc mã và kết quả kiểm tra, chưa phải danh sách công việc đã triển khai. Chưa có số liệu người dùng, dung lượng dữ liệu, tải đồng thời hoặc yêu cầu doanh nghiệp được xác nhận nên không đưa ra cam kết thời gian hay quy mô chịu tải.

**Tiến độ:** đã triển khai export toàn bộ kết quả lọc, chat paging và thời hạn phiên theo cấu hình, kèm integration tests PostgreSQL/HTTP và frontend tests. Xem [đợt 1](priority-features-implementation.md). DEV-08 đã có nền kiểm thử tích hợp cho ba chức năng này; các luồng nghiệp vụ khác và browser E2E vẫn cần bổ sung.

## 1. Những gì đã có

| Nhóm | Đã quan sát trong mã | Phần cần làm rõ/hoàn thiện |
|---|---|---|
| Low-code runtime | App/module/table/column, form và bảng động, dashboard, Kanban/lịch | Quản lý thay đổi schema, kiểm thử liên đường ghi và hiệu năng dữ liệu lớn |
| Quyền | Hệ thống/App/bảng/dòng/trường, nhiều role | Ma trận kiểm thử HTTP và chính sách dữ liệu dùng chung |
| Nghiệp vụ | CRUD, công thức, import/export, lookup, 360°, audit, phê duyệt | Transaction, concurrency, khóa bản ghi xuyên các luồng |
| Điều phối | Automation, dry-run, scheduler, log, webhook | Outbox, retry, idempotency và job locking |
| Cộng tác | Chat, task, nhắc việc, SSE, kênh ngoài | Kiểm thử end-to-end, reconnect và độ tin cậy khi gián đoạn |
| Tạo App | Templates, Excel, blueprint, tạo theo prompt | Blueprint remap/rollback; phân biệt tạo theo từ khóa và AI thật |
| Desktop | Cửa sổ native, tray, snapshot và queue | Khởi động offline, phân vùng dữ liệu người dùng, xử lý xung đột |
| Delivery | Dockerfiles, Compose, unit tests | Pipeline, lint, integration/E2E, backup và giám sát |

## 2. Nhóm P0 — trước khi dùng dữ liệu nghiệp vụ thật

P0 ở đây là mức ưu tiên sản phẩm/kỹ thuật trong đợt đánh giá, không phải mức độ của một kết luận kiểm thử bảo mật chuyên sâu.

### DEV-01 — Một quy tắc ghi record cho mọi kênh

**Bằng chứng:** `RecordService.UpsertRecordAsync` thực hiện nhiều bước nhưng `BulkImportAsync` có vòng SQL riêng; `AutomationService.ExecuteRuleActionAsync` tạo/cập nhật record bằng SQL trực tiếp.

**Tác động:** công thức, khóa bản ghi đã duyệt, audit, trigger và validation có thể khác nhau tùy nguồn thao tác. Import hiện tính lỗi nhưng vẫn lưu dòng lỗi; cần thống nhất ý nghĩa này với sản phẩm.

**Bổ sung:** dịch vụ ghi record chung hỗ trợ batch và actor/context; quy định lưu bản nháp lỗi hay từ chối dữ liệu, chính sách sửa bản ghi đã duyệt, cùng hiệu ứng được phép từ từng kênh.

**Nghiệm thu:** cùng một bộ dữ liệu qua form/import/automation/API cho kết quả quy tắc tương đương; người dùng không có quyền không sửa được bản ghi khóa qua bất kỳ kênh nào; mọi thay đổi cần audit đều có lịch sử.

### DEV-02 — Định danh duy nhất và mã tự tăng an toàn

**Bằng chứng:** auto-number dùng `COUNT(*) + 1`; kiểm tra trùng bằng SELECT trước INSERT; index trên `table_id, record_key` chưa unique.

**Bổ sung:** sequence hoặc bảng counter cập nhật nguyên tử; xác định phạm vi số theo bảng/prefix/kỳ; constraint/index unique có điều kiện phù hợp với quy tắc định danh và xóa mềm. Rà soát trùng hiện hữu trước migration.

**Nghiệm thu:** tạo đồng thời không sinh mã trùng; xóa vĩnh viễn không làm dùng lại mã ngoài chính sách; lỗi trùng trả về thông báo thống nhất; có test tại PostgreSQL.

### DEV-03 — Chống ghi đè và duyệt đồng thời

**Bằng chứng:** DTO upsert/patch chưa có expected version; approval đọc bản ghi rồi UPDATE theo ID mà không so sánh bước/phiên bản trước đó.

**Bổ sung:** optimistic concurrency cho record, cập nhật trạng thái có điều kiện và chặn chuyển trạng thái không hợp lệ. UI hiển thị xung đột và cho tải lại/thực hiện lại có chủ ý.

**Nghiệm thu:** hai phiên cùng sửa không âm thầm mất dữ liệu; hai người duyệt một bước không tạo lịch sử sai hay bước bị ghi đè; request lặp có kết quả được quy định rõ.

### DEV-04 — Transaction, audit và outbox

**Bằng chứng:** upsert thường ghi record rồi ghi audit và chạy automation bằng các thao tác không nằm trong một transaction bao trùm. Webhook chạy trong request.

**Bổ sung:** commit dữ liệu, audit và sự kiện cần xử lý trong một transaction; worker xử lý tác vụ ngoài với retry, log, idempotency và nơi giữ tác vụ hết số lần thử.

**Nghiệm thu:** lỗi ghi audit không để lại thay đổi không có lịch sử; crash sau commit không mất tác vụ; replay không tạo hiệu ứng trùng; webhook chậm không kéo dài thời gian lưu tương ứng timeout từng rule.

### DEV-05 — Cấu hình môi trường thật và xác minh ranh giới quyền

**Bằng chứng:** Auth có secret fallback, Compose có secret/password mặc định; initializer seed tài khoản demo; login cookie chưa đặt `Secure`; quyền được kiểm tra thủ công ở nhiều controller. Callback SSO có giải mã payload ID token và pending state trong RAM.

**Bổ sung:** bắt buộc secret riêng khi Production, cơ chế khởi tạo admin rõ ràng, seed demo tách biệt, HTTPS/cookie và origin theo môi trường. Hoàn thiện xác minh OIDC chuẩn bằng thành phần chuyên dụng hoặc validation đầy đủ. Lập ma trận kiểm thử quyền cho record, audit, comments, saved views, tệp, lookup, analytics, public form và external API.

**Nghiệm thu:** Production từ chối chạy nếu thiếu secret bắt buộc; không tự tạo tài khoản demo; các vai trò ngoài phạm vi nhận từ chối nhất quán; SSO từ chối token sai chữ ký/issuer/audience/nonce/hết hạn và callback lặp. Nhận xét này cần kiểm thử riêng, không được coi là kết luận khai thác đã xác nhận.

### DEV-06 — Migration và khôi phục dữ liệu

**Bằng chứng:** DDL/seed tồn tại trong cả `schema.sql` và `DbInitializer`; khởi động API chạy initializer. Chưa thấy migration ledger, script backup/restore hoặc runbook.

**Bổ sung:** baseline schema và migration tuần tự có phiên bản; phân tách quyền runtime với quyền nâng cấp schema; backup database cùng uploads; quy trình restore và kiểm tra tương thích phiên bản.

**Nghiệm thu:** database trống và database phiên bản trước đều nâng cấp được; restart không seed lại dữ liệu ngoài ý muốn; phục hồi sang môi trường tách biệt và mở được tệp gắn record. Chủ dự án cần chốt RPO/RTO để đặt lịch backup phù hợp.

## 3. Nhóm P1 — hoàn thiện bản dùng nội bộ ổn định

| ID | Hạng mục và bằng chứng | Kết quả/tiêu chí nghiệm thu |
|---|---|---|
| DEV-07 | Lint/CI/build sạch: ESLint chưa khai báo, chưa thấy pipeline hay ignore files; Docker frontend COPY `public` chưa tồn tại, healthcheck API gọi curl chưa được bảo đảm | Checkout sạch chạy được lint/typecheck/tests/build; container healthy; không đưa node_modules/bin/obj/.next vào build context |
| DEV-08 | Integration/E2E: 29 test hiện có chủ yếu kiểm tra hàm và helper | Bộ test HTTP + PostgreSQL chứng minh CRUD, quyền, import, audit và workflow; E2E login → tạo dữ liệu → duyệt → xuất; thêm chat → task |
| DEV-09 | Chuẩn hóa lỗi: frontend fetch phân tán, response/error khác nhau; catch ở vài đường xử lý bỏ qua chi tiết | API error contract có mã lỗi và trace ID; UI phân biệt lỗi mạng/401/403/409/validation; không đưa lỗi sau ghi thành tạo offline trùng |
| DEV-10 | SSE: subscriber in-memory; vòng stream tạo `WaitForNextTickAsync` và chờ cùng reader | Kiểm thử event liên tục trước chu kỳ ping để kiểm chứng rủi ro nhiều lần chờ timer chồng nhau; duy trì một tác vụ ping/read hợp lệ; reconnect tải lại dữ liệu |
| DEV-11 | Blueprint: import sao chép RefAppId/RefTableId cũ và ghi nhiều bước | Preview diff, remap lookup nội bộ, phân biệt tham chiếu ngoài, validate trước áp dụng, transaction/khôi phục khi lỗi; test clone rồi xóa nguồn vẫn dùng được lookup nội bộ |
| DEV-12 | Công thức: 2 lượt tính, một số lỗi trả null hoặc bị bỏ qua | Đồ thị phụ thuộc, phát hiện chu trình, thông báo lỗi theo trường; test chuỗi phụ thuộc dài, chia 0, ngày, sub-table và cross-app |
| DEV-13 | Giám sát: `/health` trả tĩnh; chưa thấy metrics/tracing tích hợp | Liveness/readiness riêng, readiness kiểm tra DB; log có trace ID; theo dõi độ trễ, lỗi API, queue và tác vụ thất bại; thử cảnh báo có tín hiệu |
| DEV-14 | Offline: replay tuần tự theo HTTP status, queue chưa gắn user, remote frontend | Idempotency phía server, queue theo user/workspace, trạng thái retry/xung đột rõ; thử mất mạng sau khi server đã commit và đổi tài khoản trước sync |
| DEV-15 | Khả năng bảo trì: Studio page 2.108 dòng, StudioModals 2.304 dòng; RecordService 1.754 dòng, MetadataService 1.399 dòng tại thời điểm đọc | Tách theo năng lực: designer, blueprint, record commands/query, approval; giữ contract và regression tests; giảm phụ thuộc chéo có đo đếm |

DEV-10 là nghi vấn cụ thể từ mã `NotificationsController.Stream`, chưa được tái hiện bằng phiên SSE thực tế trong khảo sát này.

## 4. Nhóm P2 — mở rộng theo nhu cầu đã xác nhận

Đợt phân tích tiếp theo đã bổ sung [20 nhóm chức năng còn thiếu hoặc chưa hoàn chỉnh](feature-gap-analysis.md) và [thiết kế triển khai đề xuất](functional-extension-design.md). Ưu tiên A/B/C trong tài liệu chức năng là thứ tự theo giá trị sản phẩm và phụ thuộc; P0/P1/P2 ở đây vẫn là ưu tiên hoàn thiện kỹ thuật. Một phần công việc có thể cần làm trước phát hành, như export đủ kết quả, import preview, form rules, vòng đời phê duyệt và publish metadata, dù chủ đề mở rộng tổng quát được liệt kê ở P2 bên dưới.

| Hạng mục | Khi nên làm | Kết quả cần có |
|---|---|---|
| Metadata có draft/publish/version | Nhiều người cùng chỉnh App hoặc thay schema đang dùng | Preview thay đổi, đánh giá tác động dữ liệu, lịch sử và đường rollback |
| Workflow nâng cao | Có quy trình cần ủy quyền, song song, thay người duyệt, escalation | Quy tắc rõ về trách nhiệm, SLA, lịch sử và hiệu lực phiên bản quy trình |
| Hiệu năng/truy vấn lớn | Có dataset và mục tiêu độ trễ cụ thể | Benchmark PostgreSQL, query plan, index theo cột quan trọng, phân trang phù hợp; không thêm cache trước khi đo |
| Đa instance | Có mục tiêu sẵn sàng hoặc lưu lượng buộc scale | Shared SSO state, fan-out realtime, scheduler lock và kiểm thử rolling restart |
| Đa công ty/tenant | Có khách hàng độc lập cần cô lập dữ liệu | Tenant model xuyên DB/API/tệp/queue/audit và test cô lập; App không thay thế tenant |
| App Copilot dùng mô hình | Tạo App bằng ngôn ngữ tự nhiên là nhu cầu thực | Provider abstraction, schema output, preview, validation, giới hạn chi phí và bộ đánh giá kết quả |
| Mobile/PWA và onboarding | Có người dùng trên điện thoại hoặc người mới gặp khó khăn | Kịch bản responsive đã kiểm chứng, hướng dẫn theo vai trò và truy cập phù hợp |
| Báo cáo và template in | Có yêu cầu biểu mẫu thực tế ngoài A4/barcode hiện có | Bộ mẫu có phiên bản, lựa chọn dữ liệu, phân quyền xuất và ví dụ đã nghiệm thu |
| Vòng đời tệp | Upload tăng hoặc cần nhiều máy API | Storage abstraction, quota, chính sách lưu/xóa, liên kết record và dọn file mồ côi |

Không mặc định bổ sung tất cả P2. Mỗi hạng mục cần tình huống sử dụng, chủ sở hữu và thước đo đạt trước khi đưa vào kế hoạch.

## 5. Thứ tự triển khai đề xuất

1. **Thiết lập nền kiểm chứng:** Git/ignore, sửa lint và Docker, tạo test database, đóng băng baseline hành vi và xác định chính sách dữ liệu lỗi/khóa.
2. **Bảo đảm dữ liệu:** DEV-01, DEV-02, DEV-03; sau đó DEV-04 và migration cho các thay đổi tương ứng. DEV-05 thực hiện song song trong cùng giai đoạn trước dữ liệu thật.
3. **Chứng minh một luồng hoàn chỉnh:** user đúng quyền tạo record → công thức đúng → gửi duyệt → duyệt đúng bước → audit/outbox → thông báo → tải lại/xuất dữ liệu.
4. **Hoàn thiện vận hành:** backup/restore, readiness, giám sát, lỗi chuẩn, blueprint, SSE và offline; kiểm thử lỗi gián đoạn.
5. **Mở rộng sản phẩm:** chọn hạng mục P2 theo phản hồi của nhóm dùng thử và dữ liệu đo thực tế.

## 6. Luồng tối thiểu dùng để nghiệm thu nền tảng

Chuẩn bị một App hai bảng có lookup, một bảng có cột số, công thức, mã tự tăng và workflow hai bước. Có admin, người tạo, người duyệt và người không có quyền.

Nghiệm thu gồm: tạo metadata; thêm record qua UI và Excel; kiểm tra quyền và trường ẩn; tạo đồng thời; sửa với dữ liệu cũ; gửi và duyệt hai bước; thử sửa bản ghi đã duyệt từ mọi kênh; kiểm tra audit/notification; ngắt mạng rồi gửi lại; restart dịch vụ; khôi phục backup và mở lại record/tệp. Ghi ID dữ liệu và kết quả cho từng bước.

## 7. Các yêu cầu cần chủ dự án chốt

- Dùng trong một doanh nghiệp hay cung cấp cho nhiều khách hàng độc lập?
- Nhóm nghiệp vụ và App nào là phạm vi phát hành đầu tiên?
- Có cho phép lưu dữ liệu lỗi dưới dạng bản nháp không? Khi nào chặn gửi duyệt?
- Bao nhiêu user đồng thời, bản ghi/bảng, dung lượng import và thời gian phản hồi chấp nhận được?
- Có bắt buộc làm việc hoàn toàn offline hay chỉ cần giữ yêu cầu chưa gửi?
- Mức mất dữ liệu và thời gian phục hồi tối đa chấp nhận được là bao nhiêu?
- SSO/notification provider nào phải hoạt động ở bản đầu? Có môi trường thử riêng không?

Các câu trả lời này điều chỉnh thứ tự và phạm vi; không thay thế những lỗi/vướng mắc đã có bằng chứng trong mã và lệnh kiểm tra.
