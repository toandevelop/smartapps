# Hướng dẫn phát triển SmartApps

Đối chiếu mã và chạy kiểm tra gần nhất ngày **18/11/2026**. Các lệnh dưới đây dùng PowerShell tại `D:/smartapps`.

**Cập nhật 19/11/2026:**
- Kiểm thử hiện đạt **137/137** backend `.NET` unit + PostgreSQL integration tests (`dotnet test -c Release`), **65/65** frontend unit tests (`npm test`), **22/22** Playwright E2E (`npm run test:e2e`), `npx tsc --noEmit` thoát `0` và `npm run lint` đạt **0 lỗi** (7 cảnh báo `@next/next/no-img-element` cho URL động/chữ ký; 0 cảnh báo React hook).
- Repository Git (`origin/main`) và CI (`.github/workflows/conditional-form-e2e.yml`) đã bật đủ Backend tests, `tsc --noEmit`, `npm run lint`, `npm test` và Playwright E2E; `db/schema.sql` đã đồng bộ đầy đủ theo 14 migration (`0001..0014`) kèm test kiểm tra parity tự động.
- Toàn bộ 4 Core Apps (`HRM`, `ASSET`, `OPS`, `TASKS`) và 15 built-in App Templates đều chuẩn hóa lên **v2.0.0** (`AppTemplateCatalog.V2.cs` + `AppUpgradeService.cs`), hỗ trợ nâng cấp 1-Click từ v1.0 không mất dữ liệu (`/api/apps/upgrade-v2/preview`, nút `Xem thử Nháp` `?previewDraft=true` & Studio `Nâng cấp Apps 2.0`), đồng thời hoàn thiện **Kho Apps Online (`/store`, `AppStoreService`, `AppStoreController`)** với 20 gói v2.0 tải về `.smartapp.json` (SHA-256), cài hai lượt lookup chéo + rollback bù trừ, Đóng gói Xuất App, Lịch sử Kho và đồng bộ/bật-tắt Remote Registry `manifest.json`.
- Đã bổ sung hướng dẫn vận hành production, reverse proxy SSE và backup/restore tại [operations-and-backup.md](../collaboration/operations-and-backup.md) cùng [`deploy/nginx.conf.example`](../../deploy/nginx.conf.example), đồng thời có chế độ chạy `--migrate-only`, `/health/ready` và `/metrics`.

Luồng Excel mới và API preview bắt buộc được mô tả trong [triển khai nhập Excel](excel-import-implementation.md). Khi thay đổi quy tắc nhập, phải kiểm tra cả preview và commit; không phục hồi cách gọi bulk-import trực tiếp bỏ qua preflight.

Quy tắc form được mô tả ở [form có điều kiện](form-rules-implementation.md). Khi thêm toán tử hoặc đổi ngữ nghĩa, cập nhật evaluator C#/TypeScript và bộ fixture dùng chung; kiểm tra cả CRUD, inline, public, external, import và automation.

Đã có **22 kịch bản Playwright E2E** ([chi tiết form/CI](conditional-form-e2e.md) và [E2E Excel](excel-e2e.md)), gồm thêm [`workflow-and-ecosystem.spec.ts`](../../frontend-next/e2e/specs/workflow-and-ecosystem.spec.ts) (Record 360° restore/recall/approve, Dark Mode semantic UI, OpenAPI/MCP/`Idempotency-Key`/`/metrics`, Apps v2.0 upgrade và Kho Apps Online `/store`). Chạy `npm run test:e2e` tại frontend với `SMARTAPPS_E2E_POSTGRES` để tự tạo database, build/chạy server riêng và dọn sau test. Khi đổi mapping, preflight/fingerprint, phân trang, query export, workflow 360°, Store packages hoặc giao diện dark theme, cập nhật assertion và chạy lại toàn bộ suite.

## 1. Chuẩn bị

- .NET SDK có khả năng build target `net9.0`.
- Node.js và npm; Dockerfile frontend dùng Node 22 làm mốc môi trường.
- PostgreSQL 16 hoặc Docker Engine để chạy service PostgreSQL.
- Nếu phát triển desktop native (`desktop-rust/`): cần cài Rustup/Cargo và MSVC Build Tools trên Windows. Trên host hiện tại chưa cài `cargo`/`rustc` nên phần Rust crate chỉ duy trì giao diện lệnh hiện có; bridge TypeScript (`tauri-bridge.ts`) và endpoint `.NET` (`complete_local_sign`) được kiểm thử độc lập qua `npm test` và `dotnet test`.
- IDE tùy chọn. Đọc [quy trình Git & Code Review](../collaboration/git-and-review-workflow.md) trước khi mở PR.

Không cần chuyển dependency sang phiên bản khác chỉ để chạy theo tài liệu này. Khi nâng cấp hãy làm thành thay đổi riêng có kiểm thử.

## 2. Chạy local

### Database

Có thể dùng PostgreSQL đang cài local hoặc khởi động riêng service qua Compose:

```powershell
Set-Location D:/smartapps
$env:POSTGRES_PASSWORD = '<mat-khau-local-rieng>'
docker compose up -d postgres
```

Docker Engine phải hoạt động. Nếu dùng instance PostgreSQL đã có dữ liệu, xác nhận connection string và sao lưu trước khi chạy API: initializer có DDL/seed tự động, không chỉ kiểm tra kết nối.

### Backend

```powershell
Set-Location D:/smartapps
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=smartapps_db;Username=postgres;Password=<mat-khau-local-rieng>'
$env:Auth__Secret = '<chuoi-ngau-nhien-rieng-cho-moi-truong>'
dotnet restore ./backend-dotnet/SmartApps.Api.csproj
dotnet run --project ./backend-dotnet/SmartApps.Api.csproj
```

Launch profile mặc định trong nguồn dùng Development và cổng `5200`. API gọi initializer trước khi bắt đầu phục vụ; tài khoản database hiện cần truy cập database `postgres` để kiểm tra/tạo database đích, cùng quyền thực thi DDL trong database ứng dụng.

Điểm kiểm tra:

```powershell
Invoke-RestMethod http://localhost:5200/health
Invoke-RestMethod http://localhost:5200/openapi/v1.json
```

`/health` trả trạng thái liveness tĩnh; dùng `/health/ready` để kiểm tra kết nối PostgreSQL thực tế.

### Frontend

Mở terminal thứ hai:

```powershell
Set-Location D:/smartapps/frontend-next
npm ci
$env:NEXT_PUBLIC_API_ORIGIN = 'http://localhost:5200'
npm run dev
```

Truy cập `http://localhost:4100`. API được gọi qua cùng origin `/api` nhờ rewrite. Khi đổi backend origin cần khởi động lại dev server; với bản production phải lưu ý giá trị được dùng trong giai đoạn build.

Development có chức năng chọn nhanh tài khoản demo. Initializer hiện seed mật khẩu demo chung và dữ liệu mẫu; không dùng cơ chế này để chuẩn bị môi trường thật.

## 3. Build và kiểm thử

```powershell
Set-Location D:/smartapps
dotnet test ./backend-dotnet.Tests/SmartApps.Api.Tests.csproj
```

```powershell
Set-Location D:/smartapps/frontend-next
npx tsc --noEmit
npm run lint
npm test
npm run build
```

Tất cả các lệnh trên (`tsc --noEmit`, `npm run lint`, `npm test` với 65 unit test và `npm run build`) đã đạt; `dotnet test` với `SMARTAPPS_TEST_POSTGRES` đạt **137/137** và `npm run test:e2e` với `SMARTAPPS_E2E_POSTGRES` đạt **22/22**. Xem thêm [biên bản xác minh](verification.md).

Đọc [verification.md](verification.md) để phân biệt lệnh đã chạy và các kịch bản mới đề xuất.

## 4. Bản đồ mã cho lập trình viên

| Nhu cầu | Điểm bắt đầu | Các nơi cần đối chiếu |
|---|---|---|
| Đổi cấu trúc App/bảng/cột | `MetadataController`, `MetadataService` | Models, Studio, runtime, blueprint và initializer |
| Thêm kiểu dữ liệu | `PlatformModels.cs`, `DynamicComputeService` | `types.ts`, `DynamicFieldInput`, `DynamicInlineCell`, bảng, import/export |
| Đổi lưu/chỉnh sửa record | `RecordsController`, `RecordService` | Import, automation SQL, external/public form và offline queue |
| Đổi phân quyền | `AuthService` | Controllers, row filter, `auth-context.tsx`, role UI và tests |
| Đổi phê duyệt | `WorkflowService`, approval trong `RecordService` | Form workflow, pending approvals, reminder và lịch sử |
| Thêm automation action | `AutomationService` | `StudioModals.tsx`, DTO, dry-run và execution logs |
| Đổi chat/task | `ChatService`, `TaskService` | Controllers, `/chat`, `TasksAppWorkspace`, `TaskCardInChat` |
| Đổi dashboard | `WidgetService`, `WidgetsController` | `AppDashboardView`, `WidgetConfigModal`, analytics |
| Đổi SSO | `SsoService`, `SsoController` | Login, panel cấu hình, provider credentials và callback |
| Đổi thông báo ngoài | `ExternalNotificationService` | Settings panel, user preferences, delivery logs |
| Đổi desktop/offline | `desktop-rust/src/lib.rs`, `tauri-bridge.ts` | Tauri config/capabilities, runtime save, Navbar sync |

Đường dẫn ở bảng là tên tương đối trong các thư mục backend/frontend; xem [technical-design.md](technical-design.md) để tra vai trò và [api-inventory.md](api-inventory.md) để tìm controller/action cụ thể.

## 5. Quy trình phát triển một tính năng

1. Ghi tình huống người dùng, kết quả mong muốn, quyền cần có và tình huống lỗi. Phân biệt tính năng dùng chung nền tảng với tính năng riêng App.
2. Xác định hợp đồng API và DTO trước khi sửa UI. Tính tới request sai, người không có quyền, thao tác lặp và cập nhật đồng thời nếu tính năng ghi dữ liệu.
3. Nếu đổi schema, bổ sung step idempotent vào `DatabaseMigrationRunner` (cùng `db/schema.sql`), xác định cách rollback và kiểm chứng bằng chế độ `dotnet run -- --migrate-only`; không chỉ sửa `schema.sql` rồi coi triển khai đã xong.
4. Đặt quy tắc nghiệp vụ ở backend. UI có thể kiểm tra sớm nhưng không thay thế kiểm tra API.
5. Kiểm tra mọi đường đi có liên quan: form, inline edit, Excel, automation, public form, external API và offline replay. Dùng chung quy tắc thay vì sao chép thêm nhánh SQL.
6. Viết kiểm thử cho invariant/rủi ro của thay đổi. Thay đổi data pipeline cần test database; thay đổi giao diện trọng yếu cần E2E; sửa nội dung nhỏ không cần test mô phỏng lại implementation.
7. Chạy kiểm tra phù hợp, cập nhật tài liệu hợp đồng và ghi giới hạn còn lại. Không chỉ cập nhật danh sách tính năng trong README.

### Ví dụ: thêm một kiểu cột

Phải xác định giá trị JSON chuẩn, empty/null, định dạng hiển thị, validation, phép sort/filter, import/export và công thức có dùng được hay không. Bổ sung vào editor Studio, form runtime, inline cell và hiển thị chỉ đọc. Kiểm tra dữ liệu cũ khi đổi kiểu cột; không chỉ thêm một input mới.

### Ví dụ: thêm automation action

Xác định payload, quyền thực thi, dữ liệu nguồn/đích, sự kiện, cơ chế chống lặp và thông tin log. Nếu action ghi record, cần đi qua quy tắc dữ liệu chung. Nếu gọi dịch vụ ngoài, thiết kế retry với idempotency; dry-run phải phân biệt xem trước với hành động tạo hiệu ứng thật.

## 6. Chuẩn kiểm thử cần bổ sung

| Lớp kiểm tra | Phạm vi | Tình huống quan trọng |
|---|---|---|
| Unit | Hàm thuần | Công thức, điều kiện workflow, template, quyền và parsing |
| Integration HTTP + PostgreSQL | Controller/service/database thật dùng cho test | Cookie/Bearer, App/bảng/dòng/trường, CRUD, import và audit |
| Đồng thời | Database và quy tắc ghi | Tạo mã, cùng sửa record, hai người duyệt, replay offline |
| E2E | Browser → web → API → DB | Login, tạo App, nhập record, duyệt, export, chat giao task |
| Khả năng khôi phục | Worker, database và uploads | Restart giữa chừng, webhook lỗi, backup/restore và reconnect SSE |

Không chạy test phá hủy dữ liệu trên database phát triển hoặc production. Tạo database/container test riêng và dữ liệu xác định được.

## 7. Docker và triển khai

Compose đã có PostgreSQL, API, frontend và volume cho database/uploads:

- `frontend-next/public` hiện đã có sẵn trong cây nguồn (`.gitkeep`, `manifest.json`), khớp bước `COPY --from=build /app/public ./public` trong `frontend-next/Dockerfile`.
- `backend-dotnet/Dockerfile` đã cài `curl` phục vụ healthcheck Compose, đồng thời hỗ trợ `AUTH_SECRET` bắt buộc ở `docker-compose.yml` và biến `Cors__AllowedOrigins`.
- Lưu ý: image build/up qua `docker compose up --build` vẫn cần chạy kiểm chứng trực tiếp trên môi trường có Docker Engine hoạt động.

Môi trường thật cần cấu hình riêng secret, tắt seed demo (`ASPNETCORE_ENVIRONMENT=Production` / `Database__SeedDemoData=false`), HTTPS/reverse proxy, giới hạn origin, readiness database (`/health/ready`), backup cả database lẫn uploads và theo dõi `/metrics`. Đã bổ sung mẫu Nginx hỗ trợ SSE tại [`deploy/nginx.conf.example`](../../deploy/nginx.conf.example) và runbook chi tiết tại [operations-and-backup.md](../collaboration/operations-and-backup.md).

## 8. Quy trình phát hành & bàn giao

Trạng thái nền tảng phát hành hiện có:

- Có Git, `.gitignore`, `.dockerignore`, `.env.example`, GitHub Actions CI chạy backend tests, `tsc --noEmit`, `npm run lint`, `npm test` và 22 kịch bản Playwright E2E.
- Có migration runner (`DatabaseMigrationRunner` / `--migrate-only`), endpoint backup/restore (`BackupsController`) và runbook sao lưu/diễn tập restore.
- Cần tiếp tục bổ sung ở môi trường vận hành thực tế: kiểm thử dựng Docker stack hoàn chỉnh, chứng thực biến môi trường Production, công cụ native Rust/Tauri nếu phát hành bản desktop trên Windows và nhật ký diễn tập restore định kỳ.
