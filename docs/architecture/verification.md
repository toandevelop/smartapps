# Biên bản khảo sát và xác minh

Ngày thực hiện: **17/11/2026**, workspace `D:/smartapps`, PowerShell trên Windows.

**Kết quả mới sau triển khai:** [biên bản ưu tiên đợt 1](priority-features-implementation.md) ghi nhận 38/38 backend tests (bao gồm PostgreSQL/HTTP), 4/4 frontend tests, typecheck và production build đạt. Các phần dưới là kết quả khảo sát ban đầu, trước khi triển khai ba chức năng.

**Đợt nhập Excel:** backend hiện đạt **53/53 tests**, không bỏ qua (có PostgreSQL/HTTP); frontend đạt **10/10 tests** cho chat và nhập Excel; TypeScript và production build cuối cùng đều đạt. Xem [triển khai nhập Excel](excel-import-implementation.md) cho contract và giới hạn. Đã thử browser trên môi trường riêng nhưng gặp timeout/lỗi khởi động Next.js qua junction trên Windows, chưa xác minh browser E2E. Database UI thử riêng đã được xóa, PostgreSQL tạm đã được dừng.

**Đợt form có điều kiện:** backend đạt **78/78 tests**, 0 lỗi, 0 bỏ qua (bao gồm PostgreSQL/HTTP); frontend đạt **31/31 tests**, production build (bao gồm TypeScript) đạt. Bộ test dùng chung dữ liệu điều kiện giữa C# và TypeScript; kiểm tra hiện/ẩn, bắt buộc, chỉ đọc, giữ giá trị cũ/mặc định và trường máy chủ tự gắn. Xem [triển khai form có điều kiện](form-rules-implementation.md) cho cấu hình Studio, contract và giới hạn. Chưa xác minh giao diện bằng browser E2E trong đợt này. PostgreSQL tạm phục vụ kiểm thử đã được dừng.

**Đợt kiểm thử giao diện form điều kiện:** đã thao tác Chrome thực tế trên Studio, form nhập liệu runtime và public form; kiểm tra trực tiếp HTTP/backend, sửa mất mặc định khi gửi phiếu mới, chuẩn hóa mặc định, liên kết nhãn public form, trạng thái tải cột trong Studio và lỗi chờ timer trùng làm ngắt SSE thông báo. Kết quả cuối: **94/94 backend tests** (PostgreSQL/HTTP, không bỏ qua), **42/42 frontend tests**, TypeScript và production build đạt. Test SSE tái hiện lỗi trước sửa và nhận được sự kiện cả trước/sau heartbeat sau sửa. Xem [biên bản và ảnh bằng chứng](form-rules-ui-verification.md). API/frontend kiểm thử riêng đã dừng; database UI thử riêng đã xóa và PostgreSQL tạm đã dừng. Kết quả khảo sát ban đầu bên dưới được giữ làm lịch sử.

**Đợt E2E tự động và CI:** đã thêm [9 Playwright E2E cho Studio/runtime/public form](conditional-form-e2e.md) và workflow GitHub Actions cho push/PR. Lượt đầu đạt **9/9**; lượt lặp với `CI=true` đạt **18/18**, không retry, không bỏ qua. Đã tạm tái đưa lỗi mất mặc định ở public form và xác nhận test thất bại đúng assertion, có screenshot/video/trace; đã khôi phục mã trước lượt đạt cuối. Backend đạt **94/94**, frontend unit **42/42**, TypeScript/production build và actionlint đạt. Runner đã dọn database sau cả pass/fail; không còn database E2E sau lượt cuối. Chưa chạy hosted GitHub Actions vì workspace chưa có Git repository/remote; required merge check là thiết lập trên repository.

**Đợt E2E Excel và CI:** bổ sung [5 test nhập và 3 test xuất Excel](excel-e2e.md), nâng bộ lên **17 kịch bản**. Lượt cuối `CI=true`, hai worker, lặp hai lần đạt **34/34**, không retry, không bỏ qua; 150,5 giây gồm build/startup. Kiểm tra workbook thật đủ **1.057 dòng** theo search/bộ lọc/trạng thái/sort khi UI ở trang 2 (10 dòng/trang), file rỗng, FLS/ownership và thu hồi quyền. Nhập kiểm tra mapping, chuẩn hóa, preview không ghi, lỗi đúng dòng, report tải xuống, upsert, invalidation và fingerprint cũ. Đã mô phỏng cap 1.000 dòng và xác nhận E2E báo lỗi đúng, rồi khôi phục mã trước lượt đạt cuối. Backend **94/94**, frontend unit **42/42**, TypeScript/production build và actionlint đạt. Video chuyển sang lần retry đầu; frontend startup có 5 phút cho build/typecheck, test 90 giây và assertion 10 giây. Report cuối giữ 8 workbook tải về. Đã xác nhận không còn database E2E, hai server E2E đã dừng và PostgreSQL tạm đã dừng. Workflow push/PR đã cập nhật; chưa có hosted Actions vì workspace chưa có Git repository/remote.

## 1. Phạm vi đã đọc

- README, cấu hình frontend/backend/desktop, Dockerfiles và Compose.
- Toàn bộ danh mục controller/route; đọc sâu luồng xác thực, records, public forms, attachments và notifications/SSE.
- Các service metadata/blueprint/tạo App, record/query/import/approval, compute, workflow, automation/scheduler, auth/SSO, chat/task và kênh thông báo.
- Schema SQL và initializer, DTO record, React auth/platform/settings providers, runtime save/offline, ThemeRegistry và Tauri bridge/native commands.
- Các file test xUnit và tên trường hợp kiểm thử hiện có.

Đây là khảo sát kỹ thuật các đường chính, không phải tuyên bố đã kiểm chứng mọi dòng mã hoặc mọi màn hình. Không sửa mã nghiệp vụ, không chạy migration mới và không khởi động API để seed database trong đợt này.

## 2. Kết quả thực thi

| Kiểm tra | Lệnh | Kết quả |
|---|---|---|
| Backend tests | `dotnet test backend-dotnet.Tests/SmartApps.Api.Tests.csproj --no-restore --verbosity minimal` | **Đạt 29/29**, 0 fail, 0 skipped; test runner báo thời lượng bộ test 510 ms |
| Frontend types | `npx tsc --noEmit` tại frontend | **Đạt**, exit code 0 |
| Frontend production build | `npm run build` tại frontend | **Đạt**, compile, TypeScript, sinh trang và tối ưu xong |
| Frontend lint | `npm run lint` tại frontend | **Không chạy được**: `'eslint' is not recognized`; package chưa khai báo dependency ESLint |
| Compose syntax | `docker compose config --quiet` | Không báo lỗi cấu hình; chưa chứng minh image build/chạy được |
| Docker Engine | `docker info --format '{{.ServerVersion}}'` | Không kết nối được pipe `dockerDesktopLinuxEngine` |
| Cargo toolchain | `Get-Command cargo` trong kiểm tra công cụ | Không tìm thấy Cargo trong PATH; chưa build/test desktop |
| Git workspace | `git status --short` | Thư mục hiện không phải Git repository |

Build Next liệt kê các route `/`, `/admin`, `/apps/[appCode]`, `/chat`, `/forms/[tableId]`, `/login`, `/notifications`, `/settings`, `/studio`, `/tasks` và trang not-found.

## 3. Bằng chứng từ cấu hình/mã

| Quan sát | Nguồn kiểm tra | Mức xác nhận |
|---|---|---|
| Dockerfile frontend COPY thư mục public chưa có | `frontend-next/Dockerfile`, `Test-Path frontend-next/public` trả False | Xác nhận tĩnh; Docker build chưa chạy |
| Backend healthcheck dùng curl nhưng Dockerfile không cài | `docker-compose.yml`, `backend-dotnet/Dockerfile` | Cần xác minh binary trong image; chưa chạy container |
| Auto-number dùng đếm số dòng | `RecordService.UpsertRecordAsync` | Xác nhận mã; chưa stress test |
| Import và automation ghi SQL khác luồng upsert | `BulkImportAsync`, `ExecuteRuleActionAsync` | Xác nhận mã; chưa chạy so sánh đầu cuối |
| Request chưa có expected version | `UpsertRecordRequest`, `PatchCellRequest`, SQL upsert/approval | Xác nhận mã; chưa tái hiện race |
| Prompt-to-App phân nhánh từ khóa | `MetadataService.GenerateAppFromPromptAsync` | Xác nhận mã; không thấy model call trong luồng này |
| Queue replay chưa có idempotency contract | `tauri-bridge.ts`, DTO/controller record | Xác nhận luồng; chưa thử ngắt mạng sau commit |
| Realtime và pending SSO state trong RAM | `RealtimeEventService`, `SsoService` | Xác nhận mã; chưa chạy đa instance |
| Blueprint giữ tham chiếu cột từ nguồn khi clone | `MetadataService.ImportAppBlueprintAsync` | Xác nhận thao tác copy; cần test lookup nội bộ và liên App |

## 4. Ý nghĩa của bộ test hiện có

29 test nằm trong 9 file: AuthService, DynamicComputeService, AutomationService, WorkflowService, TaskService, RecordLookup, SsoService, ExternalNotificationService và AppTemplateCatalog. Chúng xác minh nhiều helper/quy tắc hữu ích: hash mật khẩu, quyền, row-scope resolver, công thức, điều kiện ngày, mapping template, parser lệnh, PKCE, masking và template catalog.

Chưa thấy test host HTTP, fixture PostgreSQL hoặc browser E2E trong bộ test được cung cấp. Vì vậy, chưa có cơ sở từ kết quả này để kết luận controller/database, phân quyền xuyên các endpoint, transaction, workflow đồng thời, gửi thông báo ngoài hay offline sync hoạt động đúng đầu cuối.

## 5. Chưa thực hiện

- Chưa smoke test hệ thống với browser và dữ liệu thật.
- Chưa kiểm thử tải, race condition, nhiều instance hay phục hồi sau crash.
- Chưa xác minh đăng nhập nhà cung cấp SSO, SMTP/Telegram/Zalo và webhook bằng tài khoản thật.
- Chưa Docker build/up hoặc build/bundle desktop.
- Chưa diễn tập backup/restore.
- Chưa audit dependency theo advisory hiện hành hay thực hiện security scan chuyên sâu.

Các đề xuất kỹ thuật trong backlog xuất phát từ mã nguồn và kiểm tra local, không dựa trên khẳng định các phiên bản thư viện đang an toàn/còn được hỗ trợ ở thời điểm hiện tại.

## 6. Tác động của việc kiểm tra lên workspace

`dotnet test`, TypeScript và Next build có thể cập nhật các đầu ra sinh tự động như `bin`, `obj`, `.next`, `tsconfig.tsbuildinfo` và declaration do Next quản lý. Bộ tài liệu được thêm dưới `docs/architecture/` và README gốc được bổ sung liên kết tra cứu. Không cài dependency mới hoặc sửa chức năng ứng dụng.
