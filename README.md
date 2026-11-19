# SmartApps Platform — Hệ thống Xây dựng Nhiều Apps Doanh Nghiệp Dữ liệu Động

Nền tảng Low-Code / Metadata-Driven Multi-App kế thừa giao diện **Google Material Design 3 & Bảng động (`DynamicDataTable`)** từ `D:\HEMIS`, kết hợp **Next.js 16 (Web Portal & Studio) + C# .NET 9 (Core API & Automation) + Rust (Tauri 2 Desktop App) + PostgreSQL JSONB**.

## Kiến trúc 4 Thành phần

1. **PostgreSQL 16 (`smartapps_db` - Port `5432`)** — [schema.sql](file:///d:/smartapps/db/schema.sql):
   - `sys_apps` → `sys_modules` → `sys_tables` → `sys_columns`: Cấu trúc động đa App, hỗ trợ kiểu cột **`LOOKUP` liên kết chéo App**, **`SUB_TABLE` (Master–Detail)**, **`AUTO_NUMBER`**, **`FILE`/`IMAGE`**, công thức tự tính (`formula` gồm `IF`, `ROUND`, `DATEDIF`, `SUM(sub.field)`, `COUNT(sub)`, `CROSS_COUNT`/`CROSS_SUM`), `row_scope`, `hidden_for_roles`, `approval_enabled`, `public_form_enabled`.
   - `sys_categories` & `sys_category_items`: Danh mục dùng chung toàn công ty.
   - `app_records`: Lưu dữ liệu động bằng `JSONB` + `GIN (jsonb_path_ops)` index, hỗ trợ Soft Delete (`deleted_at`), Approval Workflow (`DRAFT` / `PENDING` / `APPROVED` / `REJECTED`).
   - `app_automations`, `app_automation_logs`, `app_widgets`, `app_audit_logs`, `app_record_comments`, `sys_notifications`, `sys_saved_views`, `sys_api_keys`, `sys_revoked_tokens`.
2. **C# .NET 9 Core API (`backend-dotnet/` - Port `5200`) & xUnit Tests (`backend-dotnet.Tests/`)**:
   - Xác thực HMAC Cookie/Bearer, băm mật khẩu PBKDF2-SHA256 kèm salt ngẫu nhiên, thu hồi token khi đăng xuất, chống brute-force, kiểm tra RBAC/FLS/RLS và phân cấp Đơn vị (`OrgUnitsController`), cùng cờ `--migrate-only`, `/health/ready` và `/metrics`.
   - Động cơ công thức & kiểm tra dữ liệu (`DynamicComputeService`), `RecordWriteService` hợp nhất đường ghi kèm `expectedVersion` & `Idempotency-Key`, Cross-App Lookups & 360° Relations, Approval Workflow (gửi duyệt, thu hồi `recall`, hủy `cancel`, ủy quyền `delegations`, ngày nghỉ `holidays`), Audit chọn lọc (`restore-fields`), Thùng rác khôi phục, Bộ lọc đa cột / Sắp xếp SQL & Saved Views.
   - Automation Dispatcher + Outbox Worker (`CREATE_RECORD`, `UPDATE_RECORD`, `SEND_NOTIFICATION`, `CALL_WEBHOOK`, tích hợp hệ thống & Retry/Dead-letter) và nhắc việc định kỳ (`ScheduledReminderService`).
   - Tích hợp hệ sinh thái & Việt Nam: Per-App OpenAPI (`/api/apps/{appCode}/openapi.json`), MCP JSON-RPC (`/api/mcp/{appCode}`), Hóa đơn điện tử/Thanh toán/IPN và ký số từ xa + hoàn tất ký số USB Token cục bộ (`complete_local_sign` -> `SIGNED_LOCAL_TOKEN`).
   - AI App Copilot/OCR/Triage (`/api/apps/ai-generate`, `/api/records/ai-*`), Public Intake Forms (`/api/public/forms/{tableId}` + upload/track), Inbound API Keys (`X-Api-Key`), Publish/Rollback Release, Công cụ nâng cấp Apps lên **v2.0.0** không mất dữ liệu (`AppUpgradeService` — nâng cấp **4 Core Apps + 15 App Mẫu Template** kèm Typed JSON `SUB_TABLE`, `FormRules`, `Workflows`, `Automations`, Mẫu in A4/K80, `SavedViews` và Plugin VN), **Kho Apps Online (`AppStoreService`, `AppStoreController`)** với 20 gói chuẩn `.smartapp.json` (kèm checksum SHA-256, cài từ file/URL, đồng bộ Remote Registry `manifest.json` & đóng gói xuất App), Backup tự động (`BackupsController`) & S3/Local Storage (`AttachmentsController`), Real-time SSE (`/api/notifications/stream`).
3. **Next.js 16 + MUI 7 + Google M3 (`frontend-next/` - Port `4100`)**:
   - Giao diện hỗ trợ đầy đủ **Light / Dark / System Theme** với hệ thống semantic CSS tokens, chuẩn TypeScript nghiêm ngặt và ESLint sạch (`0` lỗi).
   - `/login` — Đăng nhập tài khoản, OIDC SSO & chọn nhanh vai trò kiểm thử RBAC.
   - `/` — **Cổng Ứng dụng & Executive Portal**: Khối *Việc cần tôi phê duyệt*, *Chỉ số Điều hành Đa Ứng dụng*, khởi chạy Apps.
   - `/store` — **Kho Apps Online (SmartApps Marketplace)**: Duyệt & tìm kiếm 20 gói ứng dụng chuẩn v2.0.0 (15 template + 5 App chuyên sâu độc quyền: `ECOMMERCE_VN`, `REAL_ESTATE`, `CLINIC_HIS`, `EVENT_CHECKIN`, `MANUFACTURE_MES`), xem chi tiết schema/SHA-256, tải file `.smartapp.json` về máy, cài đặt 1-Click hoặc từ file/URL, và kết nối Nguồn Kho Từ Xa (`manifest.json`).
   - `/apps/[appCode]` — **Dynamic App Runtime**: Dashboard Widget tùy biến (`KPI`, `DONUT_CHART`, `BAR_CHART`, `LINE_CHART`, `RECENT_LIST` + lọc ngày + drill-down + AI Insights), Bảng động (`DynamicDataTable` inline edit, `StructuredFilterBuilder` AND/OR, Sắp xếp, Saved Views, Import Excel Preview/Upsert & Export `.xlsx`), Kanban Board, Lịch, Thùng rác, Hồ sơ 360° (`Record360Drawer` + Thảo luận `@mention` + Khôi phục từng trường + Ký USB Token cục bộ PKCS#11), Phiếu In Động A4 & Tem Mã vạch Code-39 SVG.
   - `/forms/[tableId]` — **Public Intake Form** thu thập dữ liệu, lookup & đính kèm không cần đăng nhập.
   - `/studio`, `/admin`, `/chat`, `/tasks`, `/settings`, `/notifications` — Thiết kế App/Bảng/Cột/Quy tắc, AI App Copilot, Excel-to-App, Blueprint/Publish/Rollback/Export gói Store `.smartapp.json`, Automation + Outbox, Quản trị User/RBAC/Đơn vị/Backup/Quota, Chat Real-time & Chuyển tin nhắn thành Task.
4. **Rust Desktop App (`desktop-rust/` - Tauri 2) & Offline/PKCS#11 Web Bridge ([`tauri-bridge.ts`](frontend-next/src/lib/tauri-bridge.ts))**:
   - Shell Desktop Rust hỗ trợ cửa sổ App độc lập, System Tray menu, cấu hình Server URL động (`get_server_config`/`set_server_config`), snapshot offline và hàng đợi cục bộ (`queue_offline_mutation`).
   - Bridge TypeScript đồng bộ tự động khi `online`, hỗ trợ xử lý xung đột `409` (`Ghi đè` / `forceOverwrite` / `Thử lại` / `Xóa` trong `Navbar`) và gọi ký số PKCS#11 cục bộ (`requestLocalTokenSign` -> `.NET` `complete_local_sign`). *(Lưu ý: build crate Rust native trong `desktop-rust/` yêu cầu cài Rustup/Cargo + MSVC Build Tools trên Windows).*

## Tài liệu phát triển, cộng tác và kỹ thuật

- **Dành cho thành viên mới & vận hành:** Xem [docs/collaboration/README.md](docs/collaboration/README.md), gồm [Hướng dẫn Onboarding](docs/collaboration/onboarding.md), [Quy trình Git & Code Review](docs/collaboration/git-and-review-workflow.md), [Chuẩn Mã nguồn & Kiểm thử](docs/collaboration/coding-standards.md), [Vận hành Production, Reverse Proxy SSE & Backup/Restore](docs/collaboration/operations-and-backup.md) ([`deploy/nginx.conf.example`](deploy/nginx.conf.example)) và mẫu cấu hình [`.env.example`](.env.example).
- **Kiến trúc & đặc tả kỹ thuật:** Bộ tài liệu kỹ thuật cập nhật ngày **19/11/2026** nằm tại [docs/architecture/README.md](docs/architecture/README.md), gồm [Danh mục API (234 controller actions)](docs/architecture/api-inventory.md), [Hướng dẫn phát triển](docs/architecture/development-guide.md), [E2E & CI (22 kịch bản)](docs/architecture/conditional-form-e2e.md) và [Biên bản xác minh](docs/architecture/verification.md).

## Lệnh chạy & Kiểm thử

```powershell
# 1. Chạy Unit + PostgreSQL Integration Tests (.NET 9 xUnit — 137 tests)
$env:SMARTAPPS_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres'
dotnet test d:\smartapps\backend-dotnet.Tests\SmartApps.Api.Tests.csproj -c Release

# 2. Chạy Backend .NET 9 (Port 5200)
cd d:\smartapps\backend-dotnet
dotnet run

# 3. Chạy Frontend Next.js (Port 4100), Unit Tests (65 tests), Typecheck, Lint & Playwright E2E (22 tests)
cd d:\smartapps\frontend-next
npm run dev
npx tsc --noEmit
npm test
npm run lint
$env:SMARTAPPS_E2E_POSTGRES = 'postgresql://postgres:postgres@127.0.0.1:5432/postgres'
npm run test:e2e

# 4. Chạy Production toàn bộ qua Docker Compose (cần đặt AUTH_SECRET tối thiểu 32 ký tự)
docker compose up --build -d
```

## Đóng góp & Giấy phép (Contributing & License)

- Dự án phát hành theo giấy phép mã nguồn mở **[MIT License](LICENSE)** — tự do sử dụng, chỉnh sửa và phân phối.
- Hoan nghênh mọi đóng góp qua Issues và Pull Requests! Xem hướng dẫn tại [CONTRIBUTING.md](CONTRIBUTING.md) và quy tắc cộng đồng tại [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
