# Tài liệu phát triển và kỹ thuật SmartApps

Ngày khảo sát: **17/11/2026**. Nguồn: mã trong `D:/smartapps`, không chỉ dựa trên README gốc.

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
| [E2E và CI form/Excel](conditional-form-e2e.md) | 17 Playwright E2E, môi trường riêng, báo cáo, chạy local và GitHub Actions | QA, lập trình viên, người quản lý CI |
| [E2E nhập và xuất Excel](excel-e2e.md) | 8 kịch bản mapping, preview, lỗi dòng, nhập và xuất toàn bộ kết quả lọc; mở rộng CI lên 17 test | QA, lập trình viên, người quản lý CI |

## Kết luận hiện trạng

**Cập nhật triển khai:** export toàn bộ kết quả lọc, phân trang lịch sử chat và thời hạn phiên từ cấu hình đã được triển khai sau khảo sát ban đầu. Xem [biên bản đợt 1](priority-features-implementation.md) để biết phạm vi chính xác và kiểm thử mới; các nhận xét thiếu trong tài liệu khảo sát ban đầu là baseline lịch sử.

SmartApps đã có nền tảng low-code đa ứng dụng khá rộng: metadata, bảng dữ liệu JSONB, phân quyền App/bảng/dòng/trường, phê duyệt nhiều bước, automation, dashboard, chat, công việc, SSO và shell desktop. Cấu trúc backend hiện là một ứng dụng ASP.NET Core với controller và service truy cập PostgreSQL trực tiếp bằng Npgsql; frontend dùng Next.js App Router và React context.

Ưu tiên phát triển tiếp theo là hoàn thiện tính nhất quán dữ liệu và khả năng triển khai: thống nhất các đường ghi dữ liệu; chống trùng mã và ghi đè đồng thời; migration có phiên bản; kiểm thử tích hợp; sửa các vướng mắc Docker/lint; bổ sung backup, giám sát và xử lý lỗi. Các tính năng mở rộng nên đi sau các điều kiện này.

Không coi việc build thành công và 29 kiểm thử đơn vị đạt là bằng chứng hệ thống đã sẵn sàng vận hành thực tế. Xem phạm vi đã xác minh trong [verification.md](verification.md).

## Quy ước

- **Hiện có:** đã quan sát thấy phần triển khai trong nguồn; không mặc nhiên đã kiểm thử đầu cuối.
- **Xác minh bằng lệnh:** có kết quả thực thi ghi trong tài liệu xác minh.
- **Cần bổ sung/đề xuất:** công việc tương lai, chưa được triển khai bởi đợt khảo sát này.
- **Cần kiểm chứng:** nhận xét từ đọc mã hoặc kịch bản chưa được tái hiện thực tế.

Các tài liệu mô tả hiện trạng và đề xuất tách biệt. Không bổ sung hay thay đổi mã nghiệp vụ trong đợt viết tài liệu này.
