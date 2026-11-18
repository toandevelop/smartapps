# Quy trình Git, Pull Request & Code Review

Tài liệu này quy định cách nhóm phối hợp trên GitHub để nhiều người có thể làm việc song song mà không làm gãy nhánh `main`.

## 1. Chiến lược nhánh (Branching Strategy)

- Nhánh `main` là nhánh chính, luôn ở trạng thái **build được và vượt qua CI**. Không push trực tiếp lên `main`.
- Mỗi nhiệm vụ / lỗi / tài liệu tạo một nhánh riêng từ `main` mới nhất:

| Tiền tố nhánh | Mục đích | Ví dụ |
|---|---|---|
| `feat/` | Tính năng mới | `feat/backend-export-csv`, `feat/frontend-kanban-filter` |
| `fix/` | Sửa lỗi | `fix/records-rls-null-org`, `fix/login-redirect-loop` |
| `refactor/` | Tái cấu trúc mã không đổi hành vi | `refactor/record-write-service` |
| `docs/` | Bổ sung hoặc cập nhật tài liệu | `docs/collaboration-guides` |
| `test/` | Bổ sung unit / integration / E2E test | `test/excel-upsert-edge-cases` |
| `chore/` | Cấu hình CI, dependencies, tooling | `chore/update-eslint-rules` |

Cập nhật nhánh làm việc thường xuyên để giảm xung đột:

```powershell
git fetch origin
git rebase origin/main
```

## 2. Quy ước Commit Message (Conventional Commits)

Định dạng chuẩn: `<type>(<scope>): <mô tả ngắn gọn>`

- **Type:** `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `ci`
- **Scope (khuyến nghị):** `backend`, `frontend`, `db`, `desktop`, `e2e`, `docs`, `ci`

Ví dụ tốt:
- `feat(backend): add cursor paging to chat messages endpoint`
- `fix(frontend): prevent stale form rule state on record switch`
- `docs(collaboration): add developer onboarding and coding standards`
- `test(e2e): verify conditional required field on public intake form`

## 3. Chia nhỏ Pull Request khi làm tính năng lớn

Để tránh PR quá lớn khó review và dễ xung đột:
1. **Tách theo tầng (nếu tính năng lớn):**
   - PR 1: Database schema + Backend service/controller + Unit/Integration tests.
   - PR 2: Frontend UI + TypeScript types + E2E tests.
   - *(Nếu thay đổi nhỏ/vừa, có thể gộp Backend + Frontend trong 1 PR để kiểm thử đầu cuối cùng lúc.)*
2. **Mỗi PR chỉ giải quyết một mục tiêu rõ ràng.** Không gộp sửa lỗi lặt vặt hoặc format hàng loạt tệp không liên quan vào PR tính năng.
3. **Nếu cần sửa tệp dùng chung** (`db/schema.sql`, `PlatformModels.cs`, `frontend-next/src/lib/types.ts`, `StudioModals.tsx`), thông báo ngắn trên Issue/kênh nhóm để tránh trùng lặp.

## 4. Quy trình mở Pull Request & Definition of Done (DoD)

Trước khi chuyển PR từ Draft sang **Ready for Review**, tác giả cần tự kiểm tra:

- [ ] Code biên dịch sạch, không có lỗi TypeScript (`npx tsc --noEmit`) và pass lint (`npm run lint`).
- [ ] Pass unit tests Backend (`dotnet test backend-dotnet.Tests/SmartApps.Api.Tests.csproj`) và Frontend (`npm test`).
- [ ] Nếu sửa luồng form có điều kiện hoặc nhập/xuất Excel, đã chạy lại E2E (`npm run test:e2e`) hoặc kiểm tra CI pass.
- [ ] Đã cập nhật tài liệu liên quan (ví dụ [`docs/architecture/api-inventory.md`](../architecture/api-inventory.md) nếu thêm/đổi endpoint).
- [ ] Đã rà soát diff để chắc chắn **không commit** `.env`, mật khẩu thật, API keys, `uploads/`, `backups/`, hay file tạm.
- [ ] Điền đầy đủ mẫu [`.github/PULL_REQUEST_TEMPLATE.md`](../../.github/PULL_REQUEST_TEMPLATE.md) (mô tả thay đổi, cách kiểm thử, ảnh chụp màn hình nếu có thay đổi UI).

## 5. Hướng dẫn cho Code Reviewer

Khi review PR của đồng đội, kiểm tra theo thứ tự ưu tiên sau:

1. **Tính đúng đắn & Bảo mật:**
   - Endpoint mới/sửa có kiểm tra xác thực, RBAC, FLS (`hidden_for_roles`) và RLS (`row_scope`) đầy đủ không?
   - Truy vấn SQL trong Npgsql có dùng tham số (`@param`) thay vì nối chuỗi trực tiếp từ input người dùng không?
2. **Tính nhất quán đa kênh:**
   - Nếu thay đổi quy tắc dữ liệu / công thức / form rule, quy tắc đó có áp dụng đồng nhất cho cả **Form CRUD, Inline Edit, Import Excel, Public Form, External API và Automation** không?
3. **Database & Tương thích ngược:**
   - Thay đổi schema có idempotent và có đường nâng cấp cho DB đã tồn tại không (`DbInitializer.cs` / `DatabaseMigrationRunner.cs`)?
4. **Kiểm thử & Khả năng bảo trì:**
   - Logic mới có test phủ các case biên và case lỗi không?
   - Có trùng lặp logic hoặc làm phình to component/service không?

## 6. CI/CD & Xử lý khi CI thất bại

Workflow chính tại [`.github/workflows/conditional-form-e2e.yml`](../../.github/workflows/conditional-form-e2e.yml) tự động chạy trên mỗi `push` và `pull_request`:
1. Backend unit & PostgreSQL HTTP integration tests (`dotnet test --configuration Release`).
2. Frontend unit tests (`npm test`).
3. Forms & Excel Playwright E2E trên bản production build (`npm run test:e2e`).

Khi CI thất bại:
- Tải artifact `forms-excel-<run_id>-<attempt>` trong tab **Actions** của GitHub để xem log `backend.trx` và Playwright report/screenshot (`artifacts/e2e/report`).
- Tái hiện tại local bằng database test riêng trước khi push commit sửa lỗi.
