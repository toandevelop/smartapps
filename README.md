# SmartApps Platform — Hệ thống Xây dựng Nhiều Apps Doanh Nghiệp Dữ liệu Động

Nền tảng Low-Code / Metadata-Driven Multi-App kế thừa giao diện **Google Material Design 3 & Bảng động (`DynamicDataTable`)** từ `D:\HEMIS`, kết hợp **Next.js 16 (Web Portal & Studio) + C# .NET 9 (Core API & Automation) + Rust (Tauri 2 Desktop App) + PostgreSQL JSONB**.

## Kiến trúc 4 Thành phần

1. **PostgreSQL 16 (`smartapps_db` - Port `5432`)** — [schema.sql](file:///d:/smartapps/db/schema.sql):
   - `sys_apps` → `sys_modules` → `sys_tables` → `sys_columns`: Cấu trúc động đa App, hỗ trợ kiểu cột **`LOOKUP` liên kết chéo App**, **`SUB_TABLE` (Master–Detail)**, **`AUTO_NUMBER`**, **`FILE`/`IMAGE`**, công thức tự tính (`formula` gồm `IF`, `ROUND`, `DATEDIF`, `SUM(sub.field)`, `COUNT(sub)`, `CROSS_COUNT`/`CROSS_SUM`), `row_scope`, `hidden_for_roles`, `approval_enabled`, `public_form_enabled`.
   - `sys_categories` & `sys_category_items`: Danh mục dùng chung toàn công ty.
   - `app_records`: Lưu dữ liệu động bằng `JSONB` + `GIN (jsonb_path_ops)` index, hỗ trợ Soft Delete (`deleted_at`), Approval Workflow (`DRAFT` / `PENDING` / `APPROVED` / `REJECTED`).
   - `app_automations`, `app_automation_logs`, `app_widgets`, `app_audit_logs`, `app_record_comments`, `sys_notifications`, `sys_saved_views`, `sys_api_keys`, `sys_revoked_tokens`.
2. **C# .NET 9 Core API (`backend-dotnet/` - Port `5200`) & xUnit Tests (`backend-dotnet.Tests/`)**:
   - Xác thực HMAC Cookie/Bearer, băm mật khẩu PBKDF2-SHA256 kèm salt ngẫu nhiên, thu hồi token khi đăng xuất, chống brute-force, kiểm tra RBAC/FLS/RLS trên toàn bộ API.
   - Động cơ công thức & kiểm tra dữ liệu (`DynamicComputeService`), Cross-App Lookups & 360° Relations (`/api/records/{id}/relations`), Approval Workflow, Thùng rác khôi phục, Bộ lọc đa cột / Sắp xếp SQL & Saved Views.
   - Automation Dispatcher (`CREATE_RECORD`, `UPDATE_RECORD`, `SEND_NOTIFICATION`, `CALL_WEBHOOK` + Dry-run `/test`) và nhắc việc định kỳ (`ScheduledReminderService`).
   - Public Intake Forms (`/api/public/forms/{tableId}`), Inbound API Keys (`X-Api-Key` cho `/api/external/tables/{tableId}/records`), Blueprint Export/Import/Upgrade, AI App Copilot (`/api/apps/ai-generate`), Real-time SSE (`/api/notifications/stream`).
3. **Next.js 16 + MUI 7 + Google M3 (`frontend-next/` - Port `4100`)**:
   - `/login` — Đăng nhập tài khoản & chọn nhanh vai trò kiểm thử RBAC.
   - `/` — **Cổng Ứng dụng & Executive Portal**: Khối *Việc cần tôi phê duyệt*, *Chỉ số Điều hành Đa Ứng dụng*, khởi chạy Apps.
   - `/apps/[appCode]` — **Dynamic App Runtime**: Dashboard Widget tùy biến (`KPI`, `DONUT_CHART`, `BAR_CHART`, `LINE_CHART`, `RECENT_LIST` + lọc ngày + drill-down), Bảng động (`DynamicDataTable` inline edit, Bộ lọc đa cột, Sắp xếp, Saved Views, Import Excel Upsert / Export `.xlsx`), Kanban Board, Lịch, Thùng rác, Hồ sơ 360° (`Record360Drawer` + Thảo luận `@mention`), Phiếu In Động A4 & Tem Mã vạch Code-39 SVG.
   - `/forms/[tableId]` — **Public Intake Form** thu thập dữ liệu không cần đăng nhập.
   - `/studio` — **App Builder Studio**: Thiết kế App/Bảng/Cột, AI App Copilot, Excel-to-App, Blueprint Export/Import/Upgrade, Tự động hóa + Chạy thử, Quản lý User/RBAC, Nhật ký Hệ thống & Tích hợp API Key.
4. **Rust Desktop App (`desktop-rust/` - Tauri 2)**:
   - Shell Desktop Rust hỗ trợ cửa sổ App độc lập, System Tray menu, cấu hình Server URL động (`get_server_config`/`set_server_config`), lưu snapshot offline và hàng đợi đồng bộ offline (`queue_offline_mutation`).

## Tài liệu phát triển và kỹ thuật

Bộ tài liệu dựa trên khảo sát mã ngày 17/11/2026 nằm tại [docs/architecture/README.md](docs/architecture/README.md), gồm kiến trúc, luồng dữ liệu/API, hướng dẫn phát triển, backlog ưu tiên và kết quả xác minh. Xem các giới hạn triển khai trong tài liệu trước khi dùng lệnh Docker bên dưới.

## Lệnh chạy & Kiểm thử

```powershell
# 1. Chạy Unit Tests (.NET 9 xUnit)
dotnet test d:\smartapps\backend-dotnet.Tests\SmartApps.Api.Tests.csproj

# 2. Chạy Backend .NET 9 (Port 5200)
cd d:\smartapps\backend-dotnet
dotnet run

# 3. Chạy Frontend Next.js (Port 4100)
cd d:\smartapps\frontend-next
npm run dev

# 4. Chạy Production toàn bộ qua Docker Compose
docker compose up --build -d
```

## Đóng góp & Giấy phép (Contributing & License)

- Dự án phát hành theo giấy phép mã nguồn mở **[MIT License](LICENSE)** — tự do sử dụng, chỉnh sửa và phân phối.
- Hoan nghênh mọi đóng góp qua Issues và Pull Requests! Xem hướng dẫn tại [CONTRIBUTING.md](CONTRIBUTING.md) và quy tắc cộng đồng tại [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
