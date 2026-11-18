# Hướng dẫn Đóng góp (Contributing Guide)

Cảm ơn bạn đã quan tâm và muốn đóng góp cho **SmartApps Platform**! Mọi đóng góp từ báo lỗi, đề xuất tính năng, cải thiện tài liệu đến gửi Pull Request đều được hoan nghênh.

## Quy trình Đóng góp (Pull Request Workflow)

1. **Fork** repository này về tài khoản GitHub của bạn.
2. **Clone** bản fork về máy và tạo nhánh mới từ `main`:
   ```bash
   git checkout -b feat/ten-tinh-nang
   # hoặc: git checkout -b fix/mo-ta-loi
   ```
3. **Cài đặt và chạy thử cục bộ**:
   - **PostgreSQL 16**: Khởi tạo database `smartapps_db` với `db/schema.sql` (hoặc dùng `docker compose up -d postgres`).
   - **Backend (.NET 9)**:
     ```bash
     cd backend-dotnet
     dotnet run
     ```
   - **Frontend (Next.js 16)**:
     ```bash
     cd frontend-next
     npm install
     npm run dev
     ```
   - **Desktop (Rust / Tauri 2)** *(tùy chọn)*:
     ```bash
     cd desktop-rust
     cargo check
     ```
4. **Chạy kiểm thử trước khi commit**:
   ```bash
   # Unit tests Backend (.NET 9 xUnit)
   dotnet test backend-dotnet.Tests/SmartApps.Api.Tests.csproj

   # Unit tests Frontend
   cd frontend-next
   npm test
   ```
5. **Commit & Push**:
   - Viết commit message rõ ràng (khuyến khích theo chuẩn Conventional Commits: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`).
   - Không commit các file cấu hình bí mật (`.env`, API keys, mật khẩu thật, dữ liệu `uploads/` hoặc `backups/`).
6. **Tạo Pull Request (PR)** vào nhánh `main` của repository gốc và mô tả rõ thay đổi, cách kiểm thử.

## Báo lỗi & Đề xuất Tính năng

- Sử dụng mục **Issues** trên GitHub và chọn mẫu **Bug Report** hoặc **Feature Request** tương ứng.
- Đính kèm các bước tái hiện, log lỗi (nếu có) và môi trường chạy (OS, phiên bản .NET/Node/Postgres).

## Quy tắc Cộng đồng

Vui lòng tuân thủ [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) để giữ môi trường trao đổi tôn trọng, cởi mở và mang tính xây dựng.
