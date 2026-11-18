# Chuẩn Mã nguồn & Kiểm thử (Coding Standards)

Tài liệu này thống nhất quy ước viết code và kiểm thử cho 4 thành phần của **SmartApps Platform** nhằm giúp nhiều lập trình viên đọc, sửa và mở rộng mã nguồn dễ dàng.

## 1. Nguyên tắc chung toàn dự án

- **Quy tắc nghiệp vụ đặt ở Backend:** UI có thể validate sớm để tăng trải nghiệm, nhưng không bao giờ thay thế kiểm tra ở API.
- **Một nguồn sự thật cho luồng ghi dữ liệu:** Mọi thao tác tạo/sửa bản ghi (Form CRUD, Inline Edit, Excel Import, Public Form, External API, Automation) phải dùng chung quy tắc kiểm tra, tính công thức (`DynamicComputeService`) và form rules (`FormRuleService`).
- **Không để lộ thông tin nhạy cảm:** Không hardcode secret, connection string thật, hoặc token trong code. Dùng cấu hình môi trường (`Auth__Secret`, `ConnectionStrings__Postgres`).
- **Giữ thay đổi tập trung:** Không tự ý format lại toàn bộ tệp không liên quan đến nhiệm vụ của bạn.

## 2. Backend — C# .NET 9 (`backend-dotnet/`)

- **Phân tách trách nhiệm:**
  - `Controllers/`: Chỉ nhận request, kiểm tra auth/quyền cơ bản, gọi Service và trả HTTP status code + DTO rõ ràng. Không viết SQL trực tiếp trong Controller.
  - `Services/`: Chứa toàn bộ logic nghiệp vụ, transaction và truy vấn dữ liệu.
  - `Models/PlatformModels.cs`: Khai báo DTO/Entity dùng chung; khi đổi tên hoặc thêm thuộc tính công khai, đối chiếu với `frontend-next/src/lib/types.ts`.
  - `Data/`: `DbInitializer.cs` và `DatabaseMigrationRunner.cs` quản lý khởi tạo và nâng cấp schema.
- **Truy vấn PostgreSQL (Npgsql):**
  - **Bắt buộc** dùng `NpgsqlParameter` (`@param`) cho mọi giá trị đầu vào. Tuyệt đối không nối chuỗi input người dùng vào câu lệnh SQL.
  - Khi lọc/sắp xếp trên cột động (`JSONB`), phải kiểm tra hợp lệ tên cột/toán tử dựa trên metadata (`sys_columns`) trước khi dựng biểu thức SQL.
- **Phân quyền (RBAC / FLS / RLS):**
  - Mọi endpoint đọc/ghi bản ghi phải áp dụng kiểm tra quyền App/Table, ẩn trường theo vai trò (`hidden_for_roles`) và phạm vi dòng (`row_scope`).
  - Endpoint công khai (`/api/public/...`) hoặc tích hợp ngoài (`/api/external/...`) phải kiểm tra flag bật/tắt (`public_form_enabled`) hoặc `X-Api-Key` hợp lệ.

## 3. Frontend — Next.js 16, TypeScript & MUI 7 (`frontend-next/`)

- **TypeScript nghiêm ngặt:**
  - Tránh dùng `any` trừ trường hợp bất khả kháng (và cần comment lý do).
  - Luôn chạy `npx tsc --noEmit` và `npm run lint` trước khi mở PR.
- **Gọi API:**
  - Gọi API qua đường dẫn tương đối `/api/...` để tận dụng Next.js rewrite tới `NEXT_PUBLIC_API_ORIGIN`, tránh hardcode `http://localhost:5200` trong component.
- **Giao diện & Design System:**
  - Tái sử dụng token và theme tại `src/lib/design-tokens.ts` và `src/lib/theme.ts` (Google Material Design 3 + MUI 7).
  - Không dồn toàn bộ modal/logic mới vào một tệp lớn; tách thành component con theo miền nghiệp vụ trong `src/components/`.
- **Đồng bộ logic với Backend:**
  - Các bộ đánh giá công thức hoặc điều kiện form (`src/lib/form-rules.ts`) khi thay đổi phải đối chiếu với `backend-dotnet/Services/FormRuleService.cs` để tránh lệch kết quả giữa preview client và validation server.

## 4. Database — PostgreSQL 16 (`db/schema.sql` & `Data/`)

- **DDL Idempotent:** Mọi câu lệnh tạo bảng/cột/index mới trong `db/schema.sql`, `DbInitializer.cs` và `DatabaseMigrationRunner.cs` phải dùng `IF NOT EXISTS` hoặc kiểm tra tồn tại trước khi chạy để an toàn khi khởi động lại.
- **Dữ liệu động `app_records`:**
  - Tuân thủ cơ chế Soft Delete (`deleted_at IS NULL` cho truy vấn thông thường).
  - Tránh thay đổi cấu trúc khóa chính hoặc định dạng JSONB làm hỏng dữ liệu cũ mà không có bước chuyển đổi.

## 5. Desktop — Rust / Tauri 2 (`desktop-rust/`)

- Mọi lệnh Tauri mới trong `desktop-rust/src/lib.rs` phải khai báo quyền phù hợp trong `desktop-rust/capabilities/default.json` và bọc qua `frontend-next/src/lib/tauri-bridge.ts`.
- Khi chạy trên trình duyệt web thông thường (không phải Tauri), `tauri-bridge.ts` phải fallback an toàn, không gây crash runtime.

## 6. Chuẩn Kiểm thử (Testing)

| Loại kiểm thử | Vị trí | Lệnh chạy | Khi nào bắt buộc bổ sung |
|---|---|---|---|
| **Backend Unit & HTTP/DB Integration** | `backend-dotnet.Tests/` | `dotnet test backend-dotnet.Tests/SmartApps.Api.Tests.csproj` | Khi thêm/sửa service, công thức, form rules, phân quyền, API endpoint |
| **Frontend Unit Tests** | `frontend-next/tests/*.test.mjs` | `cd frontend-next && npm test` | Khi thêm/sửa helper thuần TypeScript/JS (paging, normalizer, rule evaluator...) |
| **Playwright E2E** | `frontend-next/e2e/` | `cd frontend-next && npm run test:e2e` | Khi thay đổi luồng trọng yếu (form rules, nhập/xuất Excel, login, CRUD chính) |

> **Lưu ý:** Không chạy test phá hủy dữ liệu trên database dev hoặc production. Luôn dùng biến `SMARTAPPS_TEST_POSTGRES` / `SMARTAPPS_E2E_POSTGRES` trỏ đến database test riêng.
