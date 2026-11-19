# Tài liệu phát triển và kỹ thuật SmartApps

Ngày cập nhật gần nhất: **18/11/2026** (khảo sát nền tảng ban đầu ngày 17/11/2026). Nguồn: mã nguồn thực tế trong `D:/smartapps`.

## Cách đọc

| Tài liệu | Nội dung | Người đọc chính |
|---|---|---|
| [Kiến trúc và luồng xử lý](technical-design.md) | Thành phần, dữ liệu, API, phân quyền, workflow, automation và desktop | Lập trình viên, người thiết kế hệ thống |
| [Hướng dẫn phát triển](development-guide.md) | Cài đặt, chạy, quy trình thay đổi và kiểm thử | Lập trình viên mới và người bảo trì |
| [Phân tích và kế hoạch bổ sung](development-roadmap.md) | Khoảng trống, thứ tự ưu tiên, phụ thuộc và tiêu chí nghiệm thu | Chủ sản phẩm, trưởng nhóm, lập trình viên |
| [Kết quả xác minh](verification.md) | Lệnh đã chạy, kết quả và giới hạn đánh giá | Người tiếp nhận dự án, QA, vận hành |
| [Danh mục API từ controller](api-inventory.md) | HTTP method, đường dẫn, action và vị trí nguồn | Frontend, backend, tích hợp |
| [Nghiên cứu chức năng cần bổ sung](feature-gap-analysis.md) | 20 nhóm khoảng trống chức năng, hiện trạng, mức cần thiết và nghiệm thu | Chủ sản phẩm, phân tích nghiệp vụ, lập trình viên |
| [Thiết kế chức năng mở rộng](functional-extension-design.md) | Mô hình và API đề xuất cho import/export, form rules và phát hành App | Lập trình viên, người thiết kế hệ thống |
| [Triển khai ưu tiên đợt 1](priority-features-implementation.md) | Export đủ kết quả, chat paging, thời hạn phiên đã triển khai và kết quả kiểm thử mới | Lập trình viên, QA, người dùng thử |
| [Triển khai nhập Excel](excel-import-implementation.md) | Sheet, mapping, preview, lỗi theo dòng, xác nhận nhập và giới hạn | Người dùng, lập trình viên, QA |
| [Form có điều kiện](form-rules-implementation.md) | Cấu hình Studio, runtime và kiểm tra quy tắc ở backend | Người thiết kế App, lập trình viên, QA |
| [Kiểm thử giao diện form điều kiện](form-rules-ui-verification.md) | Kịch bản Chrome, lỗi đã sửa, kiểm tra HTTP/backend và ảnh bằng chứng | QA, lập trình viên, người thiết kế App |
| [E2E và CI form, Excel & Ecosystem](conditional-form-e2e.md) | 22 Playwright E2E, môi trường PostgreSQL cô lập, báo cáo, chạy local và GitHub Actions | QA, lập trình viên, người quản lý CI |
| [E2E nhập và xuất Excel](excel-e2e.md) | 8 kịch bản mapping, preview, lỗi dòng, nhập và xuất toàn bộ kết quả lọc | QA, lập trình viên, người quản lý CI |

## Kết luận hiện trạng

**Cập nhật triển khai (19/11/2026):** Toàn bộ nền tảng đã mở rộng qua các đợt tối ưu dữ liệu, workflow 360°, OpenAPI/MCP/Metrics, PKCS#11 & Offline bridge, Dark Mode (bao gồm tương phản trang in A4/K80), đồng bộ [`db/schema.sql`](../../db/schema.sql) đủ 14 migration (`0001..0014`), ESLint clean (`0` lỗi typecheck/lint), nâng cấp toàn bộ **4 Core Apps + 15 App Template lên phiên bản 2.0.0** (`AppUpgradeService` không mất dữ liệu) và hoàn thiện **Kho Apps Online (`/store`)** gồm **20 gói v2.0** tải về dạng `.smartapp.json` (SHA-256), cài hai lượt hỗ trợ lookup chéo & rollback bù trừ, Đóng gói Xuất App, Lịch sử Kho và đồng bộ/bật-tắt Remote Registry (`manifest.json`): **137/137** backend tests (gồm live PostgreSQL), **65/65** frontend unit tests và **22/22** Playwright Chromium E2E đã vượt qua. Xem [biên bản xác minh](verification.md) và [danh mục API (234 controller actions + 5 platform routes)](api-inventory.md) để nắm hiện trạng mới nhất; các nhận xét thiếu (như 29 tests, chưa có lint/Git/proxy runbook) ở các báo cáo khảo sát gốc ngày 17/11/2026 là baseline lịch sử.

SmartApps đã có nền tảng low-code đa ứng dụng khá rộng: metadata, bảng dữ liệu JSONB, phân quyền App/bảng/dòng/trường, phê duyệt nhiều bước, automation, dashboard, chat, công việc, SSO và shell desktop. Cấu trúc backend hiện là một ứng dụng ASP.NET Core với controller và service truy cập PostgreSQL trực tiếp bằng Npgsql; frontend dùng Next.js App Router và React context.

Trạng thái các nhóm kỹ thuật ưu tiên từ đợt khảo sát đầu (`17/11/2026`): hợp nhất đường ghi `RecordWriteService` + `expectedVersion` + `Idempotency-Key`, migration (`DatabaseMigrationRunner` / `--migrate-only`), kiểm thử PostgreSQL & Playwright E2E, ESLint clean (`0` lỗi), runbook proxy SSE & backup/restore đã được triển khai và xác minh.

Các bước còn lại trước khi Go-Live trên hạ tầng đích gồm: kiểm chứng `docker compose up --build` trên Docker Engine thật, thiết lập biến môi trường/secret Production, diễn tập restore định kỳ, đo tải thực tế và biên dịch `desktop-rust/` trên máy có sẵn toolchain Rust/Cargo. Xem phạm vi đã xác minh trong [verification.md](verification.md).

## Quy ước

- **Hiện có:** đã quan sát thấy phần triển khai trong nguồn; không mặc nhiên đã kiểm thử đầu cuối.
- **Xác minh bằng lệnh:** có kết quả thực thi ghi trong tài liệu xác minh.
- **Cần bổ sung/đề xuất:** công việc tương lai, chưa được triển khai bởi đợt khảo sát này.
- **Cần kiểm chứng:** nhận xét từ đọc mã hoặc kịch bản chưa được tái hiện thực tế.

Các tài liệu mô tả hiện trạng và đề xuất tách biệt. Không bổ sung hay thay đổi mã nghiệp vụ trong đợt viết tài liệu này.
