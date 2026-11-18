# Hướng dẫn phát triển SmartApps

Đối chiếu mã và chạy kiểm tra ngày **17/11/2026**. Các lệnh dưới đây dùng PowerShell tại `D:/smartapps`.

**Cập nhật:** đã thêm `npm test` và integration tests PostgreSQL/HTTP cho ba chức năng ưu tiên. Xem [hướng dẫn chạy và kết quả đợt 1](priority-features-implementation.md). Những mô tả “chưa có frontend test/integration” bên dưới phản ánh baseline trước đợt này; lint vẫn chưa được cấu hình.

Luồng Excel mới và API preview bắt buộc được mô tả trong [triển khai nhập Excel](excel-import-implementation.md). Khi thay đổi quy tắc nhập, phải kiểm tra cả preview và commit; không phục hồi cách gọi bulk-import trực tiếp bỏ qua preflight.

Quy tắc form được mô tả ở [form có điều kiện](form-rules-implementation.md). Khi thêm toán tử hoặc đổi ngữ nghĩa, cập nhật evaluator C#/TypeScript và bộ fixture dùng chung; kiểm tra cả CRUD, inline, public, external, import và automation.

Đã có [9 E2E form điều kiện và CI](conditional-form-e2e.md). Chạy `npm run test:e2e` tại frontend với `SMARTAPPS_E2E_POSTGRES` để tự tạo database, build/chạy server riêng và dọn sau test. Workflow chạy backend, frontend unit và E2E khi push/PR, lưu report và bằng chứng thất bại. Khi đổi giao diện hoặc ngữ nghĩa form, cập nhật assertion về dữ liệu đã lưu và chạy lại bộ này.

Bộ E2E hiện có **17 kịch bản**, gồm thêm [8 test nhập/xuất Excel](excel-e2e.md). Khi đổi mapping, preflight/fingerprint, phân trang hoặc query export, chạy cả hai file `excel-import.spec.ts` và `excel-export.spec.ts`; kiểm tra workbook tải về và số bản ghi thực, không chỉ trạng thái nút hoặc mã HTTP.

## 1. Chuẩn bị

- .NET SDK có khả năng build target `net9.0`.
- Node.js và npm; Dockerfile frontend dùng Node 22 làm mốc môi trường.
- PostgreSQL 16 hoặc Docker Engine để chạy service PostgreSQL.
- Nếu phát triển desktop: Rust/Cargo và các công cụ build Windows cần cho Tauri. Chưa xác minh toolchain desktop trong đợt khảo sát.
- IDE tùy chọn. Chưa có Git repository ở thư mục được cung cấp; cần thiết lập quản lý phiên bản trước khi dùng quy trình branch/PR.

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

`/health` hiện chỉ trả trạng thái tĩnh, không chứng minh database hoặc dịch vụ bên ngoài đang hoạt động.

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
npm run build
```

Các lệnh trên đã đạt. `npm test` chạy kiểm thử helper lịch sử chat; integration tests PostgreSQL/HTTP chạy khi có `SMARTAPPS_TEST_POSTGRES` như hướng dẫn trong [đợt 1](priority-features-implementation.md). `npm run lint` hiện thất bại vì không có ESLint khả dụng; phải bổ sung dependency và cấu hình rồi mới dùng làm điều kiện CI. Chưa có bộ browser E2E.

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
3. Nếu đổi schema, xác định cách nâng cấp database hiện có và cách rollback. Hiện chưa có migration runner; không chỉ sửa `schema.sql` rồi coi triển khai đã xong.
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

Compose đã có PostgreSQL, API, frontend và volume cho database/uploads. Tuy nhiên cấu hình hiện còn hai vấn đề được xác định từ file:

- Dockerfile frontend `COPY /app/public` nhưng `frontend-next/public` chưa có trong cây nguồn; cần tạo asset directory có chủ ý hoặc chỉnh COPY theo output thực tế.
- Healthcheck backend gọi `curl`, trong khi Dockerfile runtime không cài hoặc bảo đảm có lệnh này; cần cách healthcheck phù hợp image được dùng.

Chưa thực thi Docker build vì Docker Engine không kết nối được ở môi trường khảo sát. Sau khi sửa, cần kiểm tra build từ context sạch, chạy healthcheck và truy cập web/API qua địa chỉ sử dụng thực tế.

Môi trường thật cần cấu hình riêng secret, tắt seed demo, HTTPS/reverse proxy, giới hạn origin, readiness database, backup cả database lẫn uploads và theo dõi lỗi. SSE cần tắt buffering và có timeout phù hợp trên proxy. Chưa có file cấu hình proxy hoặc runbook backup/restore trong cây nguồn khảo sát.

## 8. Hoàn thiện quy trình bàn giao

Checklist phát hành đề xuất:

- Có Git, `.gitignore`, `.dockerignore`, quy ước môi trường và mẫu cấu hình không chứa credential thật.
- CI chạy backend tests, TypeScript, lint khi đã cấu hình, production build và kiểm thử tích hợp trọng yếu.
- Có migration version, bản sao lưu và kết quả diễn tập restore vào môi trường tách biệt.
- Có hướng dẫn vận hành, tài khoản quản trị khởi tạo riêng và lịch sử thay đổi phiên bản.
- Tài liệu ghi rõ cấu hình hỗ trợ một instance hay nhiều instance, cùng giới hạn offline và tích hợp.
