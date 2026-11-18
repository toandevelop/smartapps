# Tài liệu Cộng tác & Phát triển Nhóm — SmartApps Platform

Tài liệu trong thư mục này giúp nhiều thành viên (Backend, Frontend, Fullstack, QA, Desktop) làm việc song song trên **SmartApps Platform** một cách nhất quán, hạn chế xung đột mã nguồn và giữ chất lượng ổn định trước khi merge vào `main`.

## 1. Bản đồ tài liệu cộng tác

| Tài liệu | Mục đích | Đối tượng |
|---|---|---|
| [Hướng dẫn Onboarding](onboarding.md) | Cài đặt môi trường từ đầu, chạy local, xác minh và xử lý lỗi thường gặp | Thành viên mới tham gia dự án |
| [Quy trình Git & Code Review](git-and-review-workflow.md) | Chiến lược nhánh, chuẩn commit, tách PR nhỏ, checklist review và CI | Tất cả thành viên phát triển |
| [Chuẩn mã nguồn & Kiểm thử](coding-standards.md) | Quy ước code cho .NET 9, Next.js 16, PostgreSQL, Rust/Tauri và kiểm thử | Backend, Frontend, QA, Desktop |
| [CONTRIBUTING.md](../../CONTRIBUTING.md) | Tóm tắt quy trình đóng góp và gửi Pull Request | Cộng tác viên & thành viên nhóm |
| [Bộ tài liệu Kiến trúc & Kỹ thuật](../architecture/README.md) | Thiết kế kỹ thuật, danh mục API, luồng nghiệp vụ và kết quả xác minh | Toàn đội ngũ |

## 2. Lộ trình đọc nhanh theo vai trò

- **Thành viên mới (ngày đầu tiên):**
  1. Đọc [Hướng dẫn Onboarding](onboarding.md) để dựng DB, Backend, Frontend và chạy thử test.
  2. Đọc [Kiến trúc và luồng xử lý](../architecture/technical-design.md) để nắm bức tranh tổng thể 4 thành phần.
  3. Đọc [Quy trình Git & Code Review](git-and-review-workflow.md) trước khi tạo nhánh đầu tiên.
- **Lập trình viên Backend (.NET 9 / PostgreSQL):**
  - Đọc mục Backend & Database trong [Chuẩn mã nguồn](coding-standards.md).
  - Tham khảo [Danh mục API](../architecture/api-inventory.md) trước khi thêm hoặc sửa endpoint.
- **Lập trình viên Frontend (Next.js 16 / TypeScript / MUI 7):**
  - Đọc mục Frontend trong [Chuẩn mã nguồn](coding-standards.md).
  - Tham khảo [Form có điều kiện](../architecture/form-rules-implementation.md) và [Nhập Excel](../architecture/excel-import-implementation.md) khi chạm các luồng dùng chung.
- **QA / Kiểm thử & CI:**
  - Đọc [E2E và CI form/Excel](../architecture/conditional-form-e2e.md), [E2E nhập/xuất Excel](../architecture/excel-e2e.md) và [Kết quả xác minh](../architecture/verification.md).

## 3. Điểm giao thoa khi nhiều người làm việc song song

Khi nhiều người phát triển đồng thời các tính năng khác nhau, các tệp dưới đây là **điểm hội tụ dễ xung đột hoặc dễ lệch hợp đồng** — cần trao đổi trước trên Issue/PR và giữ thay đổi gọn, tương thích ngược:

| Khu vực / Tệp dùng chung | Rủi ro khi sửa song song | Nguyên tắc phối hợp |
|---|---|---|
| `db/schema.sql`, `backend-dotnet/Data/DbInitializer.cs`, `backend-dotnet/Data/DatabaseMigrationRunner.cs` | Xung đột DDL, thiếu cột khi kéo nhánh khác | Dùng DDL idempotent (`IF NOT EXISTS`); tách PR schema nếu nhiều tính năng cùng phụ thuộc |
| `backend-dotnet/Models/PlatformModels.cs` ↔ `frontend-next/src/lib/types.ts` | Lệch tên trường/kiểu DTO giữa API và UI | Cập nhật đồng thời cả 2 tệp trong cùng PR (hoặc giữ optional fields khi tách PR) |
| `backend-dotnet/Services/RecordWriteService.cs` & `RecordService.cs` | Lệch quy tắc ghi dữ liệu giữa CRUD, inline, Excel, public form, external API, automation | Mọi luồng ghi record phải đi qua quy tắc kiểm tra/tính toán chung ở backend |
| `backend-dotnet/Services/FormRuleService.cs` ↔ `frontend-next/src/lib/form-rules.ts` | Toán tử/ngữ nghĩa rule khác nhau giữa server và client | Cập nhật cả C# và TypeScript cùng bộ fixture test |
| `frontend-next/src/app/studio/` & `frontend-next/src/components/` | Xung đột UI trong `StudioModals.tsx` hoặc layout dùng chung | Chia component theo tính năng mới thay vì dồn hết vào một file lớn |
| `docs/architecture/api-inventory.md` | Tài liệu API bị lỗi thời khi thêm endpoint | Cập nhật tài liệu API khi thêm/đổi route hoặc DTO công khai |
