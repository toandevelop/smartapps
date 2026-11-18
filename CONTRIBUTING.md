# Hướng dẫn Đóng góp (Contributing Guide)

Cảm ơn bạn đã quan tâm và muốn đóng góp cho **SmartApps Platform**! Tài liệu này áp dụng cho cả thành viên phát triển nội bộ lẫn cộng tác viên mã nguồn mở.

## Tài liệu Cộng tác Chi tiết

Trước khi bắt đầu code, vui lòng xem qua bộ tài liệu dành cho nhóm phát triển tại [`docs/collaboration/`](docs/collaboration/README.md):

- **[Hướng dẫn Onboarding](docs/collaboration/onboarding.md)** — Cài đặt PostgreSQL 16, .NET 9, Node.js, biến môi trường (`.env.example`) và xử lý lỗi local.
- **[Quy trình Git & Code Review](docs/collaboration/git-and-review-workflow.md)** — Chiến lược nhánh, chuẩn commit, tách PR nhỏ, Definition of Done và xử lý CI.
- **[Chuẩn Mã nguồn & Kiểm thử](docs/collaboration/coding-standards.md)** — Quy ước code cho Backend (.NET 9), Frontend (Next.js 16), PostgreSQL, Desktop (Rust/Tauri 2) và kiểm thử.
- **[Tài liệu Kiến trúc & API](docs/architecture/README.md)** — Thiết kế hệ thống, danh mục API và luồng nghiệp vụ chi tiết.

## Quy trình Đóng góp Nhanh (Pull Request Workflow)

1. **Tạo nhánh làm việc** từ `main` mới nhất (hoặc **Fork** nếu đóng góp từ bên ngoài):
   ```powershell
   git checkout main
   git pull origin main
   git checkout -b feat/ten-tinh-nang
   # hoặc: fix/mo-ta-loi, docs/mo-ta, refactor/mo-ta, test/mo-ta
   ```
2. **Cấu hình & chạy thử cục bộ** theo [Hướng dẫn Onboarding](docs/collaboration/onboarding.md) (tham khảo [`.env.example`](.env.example)):
   - **PostgreSQL 16**: `docker compose up -d postgres` (hoặc PostgreSQL local với `db/schema.sql`).
   - **Backend (.NET 9 - Port `5200`)**: `dotnet run --project ./backend-dotnet/SmartApps.Api.csproj`
   - **Frontend (Next.js 16 - Port `4100`)**: `cd frontend-next && npm ci && npm run dev`
   - **Desktop (Rust / Tauri 2)** *(tùy chọn)*: `cd desktop-rust && cargo check`
3. **Chạy kiểm thử & kiểm tra chất lượng trước khi commit**:
   ```powershell
   # Unit & Integration tests Backend (.NET 9 xUnit)
   dotnet test backend-dotnet.Tests/SmartApps.Api.Tests.csproj

   # Type-check, Lint và Unit tests Frontend
   cd frontend-next
   npx tsc --noEmit
   npm run lint
   npm test
   ```
   *(Nếu thay đổi luồng Form có điều kiện hoặc Nhập/Xuất Excel, chạy thêm `npm run test:e2e` với database test riêng `SMARTAPPS_E2E_POSTGRES`.)*
4. **Commit & Push**:
   - Tuân thủ **Conventional Commits** (`feat(scope): ...`, `fix(scope): ...`, `docs(scope): ...`, `refactor(scope): ...`, `test(scope): ...`, `chore(scope): ...`).
   - **Tuyệt đối không commit** `.env`, mật khẩu thật, API keys, hoặc dữ liệu trong `backend-dotnet/uploads/` và `backend-dotnet/backups/`.
5. **Tạo Pull Request (PR)** vào nhánh `main`:
   - Điền đầy đủ [`.github/PULL_REQUEST_TEMPLATE.md`](.github/PULL_REQUEST_TEMPLATE.md).
   - Giữ PR gọn, tập trung một mục tiêu và theo dõi kết quả CI ([`.github/workflows/conditional-form-e2e.yml`](.github/workflows/conditional-form-e2e.yml)).

## Báo lỗi & Đề xuất Tính năng

- Sử dụng mục **Issues** trên GitHub với mẫu **Bug Report** ([`.github/ISSUE_TEMPLATE/bug_report.md`](.github/ISSUE_TEMPLATE/bug_report.md)) hoặc **Feature Request** ([`.github/ISSUE_TEMPLATE/feature_request.md`](.github/ISSUE_TEMPLATE/feature_request.md)).
- Đính kèm các bước tái hiện, log lỗi (đã che thông tin nhạy cảm) và phiên bản môi trường (OS, .NET, Node, PostgreSQL).

## Quy tắc Cộng đồng

Vui lòng tuân thủ [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) để giữ môi trường trao đổi tôn trọng, cởi mở và mang tính xây dựng.
