# Hướng dẫn Onboarding cho Thành viên Mới

Tài liệu này hướng dẫn từng bước để cài đặt môi trường phát triển **SmartApps Platform** trên máy cá nhân, chạy đủ các thành phần và xác minh trước khi bắt đầu code.

## 1. Công cụ cần chuẩn bị

| Công cụ | Phiên bản khuyến nghị | Bắt buộc? | Ghi chú |
|---|---|---|---|
| **Git** | Mới nhất | Có | Dùng quản lý nhánh & Pull Request |
| **.NET SDK** | **9.0** (`net9.0`) | Có | Build & chạy `backend-dotnet`, `backend-dotnet.Tests` |
| **Node.js & npm** | **Node 22+** (CI dùng Node 24) | Có | Build, test & chạy `frontend-next` |
| **PostgreSQL** | **16** (cài local hoặc Docker) | Có | Lưu metadata & dữ liệu JSONB (`smartapps_db`) |
| **Docker Engine / Desktop** | Mới nhất | Khuyến nghị | Tiện chạy nhanh PostgreSQL 16 qua `docker compose` |
| **Rust / Cargo + Tauri prerequisites** | Stable | Tùy chọn | Chỉ cần nếu phát triển `desktop-rust` |

## 2. Lấy mã nguồn & cấu hình môi trường

```powershell
git clone <url-repository-cua-ban>
cd smartapps
```

Tham khảo mẫu biến môi trường tại [`.env.example`](../../.env.example).

> **Quy tắc bảo mật:** Không bao giờ commit tệp `.env`, `.env.local`, mật khẩu thật, API key hoặc dữ liệu trong `backend-dotnet/uploads/`, `backend-dotnet/backups/` lên Git.

## 3. Khởi chạy từng thành phần ở local

### Bước 1: Khởi động PostgreSQL 16

Nếu dùng Docker Compose (khuyến nghị cho môi trường mới):

```powershell
# Tại thư mục gốc d:\smartapps
$env:POSTGRES_PASSWORD = 'change_me_local_postgres_password'
docker compose up -d postgres
```

Hoặc nếu dùng PostgreSQL 16 cài sẵn trên máy, hãy tạo database `smartapps_db` và nạp [`db/schema.sql`](../../db/schema.sql) (lưu ý `DbInitializer` của backend cũng sẽ kiểm tra/khởi tạo bảng và dữ liệu mẫu khi chạy Development).

### Bước 2: Chạy Backend (.NET 9 — Port `5200`)

```powershell
# Tại thư mục gốc d:\smartapps
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=smartapps_db;Username=postgres;Password=change_me_local_postgres_password'
$env:Auth__Secret = 'local_dev_secret_key_please_change_min_32_chars'
$env:ASPNETCORE_ENVIRONMENT = 'Development'

dotnet restore ./backend-dotnet/SmartApps.Api.csproj
dotnet run --project ./backend-dotnet/SmartApps.Api.csproj
```

Kiểm tra nhanh Backend đã sẵn sàng:

```powershell
Invoke-RestMethod http://localhost:5200/health
Invoke-RestMethod http://localhost:5200/openapi/v1.json
```

### Bước 3: Chạy Frontend (Next.js 16 — Port `4100`)

Mở một terminal mới:

```powershell
cd d:\smartapps\frontend-next
npm ci
$env:NEXT_PUBLIC_API_ORIGIN = 'http://localhost:5200'
npm run dev
```

Mở trình duyệt tại `http://localhost:4100`.
- Next.js tự động rewrite các request `/api/*` sang `NEXT_PUBLIC_API_ORIGIN`.
- Nếu thay đổi `NEXT_PUBLIC_API_ORIGIN`, cần khởi động lại `npm run dev`.
- Ở chế độ `Development`, trang `/login` có sẵn nút chọn nhanh tài khoản demo để kiểm thử các vai trò RBAC.

### Bước 4: Desktop App (Tùy chọn — Rust / Tauri 2)

Nếu bạn phát triển shell desktop trong `desktop-rust/`:

```powershell
cd d:\smartapps\desktop-rust
cargo check
```

## 4. Checklist xác minh môi trường sau khi cài đặt

Chạy các lệnh sau để chắc chắn máy của bạn sẵn sàng phát triển và gửi PR:

```powershell
# 1. Backend unit tests (.NET 9 xUnit)
dotnet test d:\smartapps\backend-dotnet.Tests\SmartApps.Api.Tests.csproj

# 2. Frontend type-check, lint và unit tests
cd d:\smartapps\frontend-next
npx tsc --noEmit
npm run lint
npm test
```

Nếu muốn chạy **PostgreSQL/HTTP Integration Tests** hoặc **Playwright E2E** tại local, hãy trỏ `SMARTAPPS_TEST_POSTGRES` / `SMARTAPPS_E2E_POSTGRES` tới một database **tách biệt hoàn toàn** với database phát triển:

```powershell
# Cài Chromium cho Playwright (chỉ cần chạy lần đầu)
cd d:\smartapps\frontend-next
npx playwright install chromium

# Chạy E2E (tự build & chạy môi trường test riêng)
$env:SMARTAPPS_E2E_POSTGRES = 'postgresql://postgres:change_me_local_postgres_password@127.0.0.1:5432/postgres'
npm run test:e2e
```

## 5. Xử lý các lỗi thường gặp khi cài đặt

1. **Cổng `5432`, `5200`, hoặc `4100` đã bị chiếm:**
   - Kiểm tra tiến trình đang dùng cổng (`Get-NetTCPConnection -LocalPort 5200` trên PowerShell) hoặc tắt container Docker trùng lặp (`docker ps`).
2. **Backend báo lỗi kết nối PostgreSQL:**
   - Kiểm tra container `smartapps_postgres` đang `healthy` (`docker compose ps`).
   - Đảm bảo `ConnectionStrings__Postgres` khớp `Username`, `Password`, `Port` và `Database`.
3. **Frontend gọi `/api/*` bị lỗi `ECONNREFUSED`:**
   - Đảm bảo Backend đang chạy ở `http://localhost:5200` và biến `NEXT_PUBLIC_API_ORIGIN` đã được đặt trước khi chạy `npm run dev`.
4. **Lỗi `npm ci` hoặc thiếu quyền trên Windows:**
   - Đóng các tiến trình Node/IDE đang khóa `node_modules`, sau đó chạy lại `npm ci`.
