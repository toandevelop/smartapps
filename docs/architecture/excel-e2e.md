# E2E nhập và xuất Excel

Bổ sung ngày **17/11/2026** trên cùng hạ tầng Playwright/PostgreSQL của [bộ E2E form và CI](conditional-form-e2e.md). Có **8 kịch bản Excel mới**: 5 nhập, 3 xuất; tổng bộ E2E là **17 kịch bản**.

## 1. Nhập Excel

Mã: [excel-import.spec.ts](../../frontend-next/e2e/specs/excel-import.spec.ts).

| Kịch bản | Assertion chính |
|---|---|
| Sheet, header và mapping thủ công | Tải workbook hai sheet, chọn sheet dữ liệu và header dòng 2; map các cột đã đổi thứ tự; bỏ qua cột thừa; giữ mã `001`/`002`, số dòng nguồn 3/5; chuẩn hóa số vi-VN, ngày dd/MM/yyyy và boolean |
| Preview, lỗi từng dòng và báo cáo lỗi | Workbook có header dòng 3, dòng trắng, 26 dòng hợp lệ và 5 dòng lỗi; lỗi đúng dòng 5/7/8/9/10, gồm số sai, ngày sai, thiếu mã và trùng mã trong file; lọc dòng lỗi, phân trang preview; tải và đọc lại workbook báo cáo lỗi |
| Mapping header trùng, cập nhật và invalidation | Hai header giống hệt được map theo vị trí A/B; map trùng đích làm nút kiểm tra bị khóa; thay mapping, định dạng số/ngày hoặc tùy chọn upsert làm mất hiệu lực preview; không cho cập nhật khi tắt upsert |
| Đổi file, sheet hoặc header | Mỗi thay đổi đều yêu cầu preview mới; chỉ workbook cuối được nhập, không ghi các sheet/file đã xem trước trước đó |
| Preview cũ khi dữ liệu đích đã thay đổi | Một write thật giữa preview và commit làm commit trả 409; giữ dữ liệu mới của write đó; UI khóa xác nhận; kiểm tra lại rồi nhập thành công |

Preview được đối chiếu bằng cả giao diện và response backend. Sau preview, API đọc lại xác nhận chưa ghi dữ liệu. Sau commit, test kiểm tra counters và record đã lưu. Khi cập nhật, trường không map giữ giá trị cũ, ô trống trong cột được map xóa giá trị cũ.

Lô có lỗi bị khóa trên giao diện; test còn gửi trực tiếp request commit cùng fingerprint của preview lỗi và xác nhận backend trả 400, không ghi cả các dòng hợp lệ trong lô. Không chỉ kiểm tra trạng thái nút.

## 2. Xuất toàn bộ kết quả lọc

Mã: [excel-export.spec.ts](../../frontend-next/e2e/specs/excel-export.spec.ts).

| Kịch bản | Assertion chính |
|---|---|
| Vượt phân trang và ngưỡng 1.000 dòng | Tạo 1.057 dòng mục tiêu cùng các dòng đối chứng sai search, sai bộ lọc cột, trạng thái ERROR và đã xóa; chọn search + bộ lọc + trạng thái + sort số; chuyển sang trang 2, 10 dòng/trang; file vẫn có đúng 1.057 dòng, đúng thứ tự |
| Kết quả lọc rỗng | File tải được và đọc được, có đầy đủ header, không có dòng dữ liệu |
| Quyền và phạm vi dữ liệu | Tài khoản thường có 25 dòng riêng, một dòng của người khác và cột ẩn; file chỉ có 25 dòng được phép, không có header/giá trị bí mật; thu hồi quyền export làm mất thao tác trên UI và request API bị chặn 403 |

Test nhận sự kiện download thật, lưu file `.xlsx` vào output của test và đọc nội dung workbook. Đối chiếu toàn bộ mã và số tiền thay vì chỉ đối chiếu số dòng hoặc response query. Kiểm tra MIME, tên file, header, ô số có kiểu numeric, chuỗi bắt đầu `=SUM(1,2)` vẫn là text và không có công thức. Request export không mang `page`/`limit` của bảng đang mở.

## 3. Dữ liệu và môi trường

[excel.ts](../../frontend-next/e2e/excel.ts) tạo workbook `.xlsx` trong bộ nhớ bằng dependency SheetJS đã có, đưa buffer vào file input thật và đọc lại file tải về. Không commit fixture nhị phân, không thêm dependency. Dữ liệu chuẩn bị cho export được nhập qua API preview/commit thật để tránh hàng nghìn request tạo riêng lẻ; chính các thao tác nhập cần nghiệm thu vẫn được chạy qua giao diện trong 5 test nhập.

Mỗi test dùng App/bảng/browser context riêng. Fixture đọc record đã được mở rộng để lấy đủ các trang, tránh chính assertion đọc thiếu dữ liệu trên bảng lớn. User/role thử quyền được tạo và xóa trong test, chỉ dùng database E2E tách biệt.

## 4. Chạy và CI

Chuẩn bị PostgreSQL và biến `SMARTAPPS_E2E_POSTGRES` theo [hướng dẫn môi trường](conditional-form-e2e.md), rồi tại `frontend-next`:

```powershell
npm run test:e2e -- excel-import.spec.ts excel-export.spec.ts
npm run test:e2e
npm run test:e2e -- --repeat-each=2
npm run test:e2e:report
```

Workflow [conditional-form-e2e.yml](../../.github/workflows/conditional-form-e2e.yml) nay mang tên **Forms and Excel regression**, tự khám phá đủ 17 test khi push/PR. Giữ job ID `conditional-forms` để tương thích cấu hình check đã có. Backend unit/PostgreSQL và frontend unit vẫn chạy trước E2E; frontend production được build/typecheck trong runner.

Report và các workbook đã tải ở `artifacts/e2e/`, được upload cùng artifacts của CI và lưu 14 ngày. Screenshot/trace được giữ từ lần thất bại đầu; video chỉ ghi ở lần retry đầu trên CI để giảm chi phí ghi video cho các test đạt, theo [cấu hình video Playwright](https://playwright.dev/docs/videos). `failOnFlakyTests=true` vẫn làm CI thất bại nếu test chỉ đạt sau retry; không che lỗi bằng retry.

Các assertion hiện tập trung workbook `.xlsx` và Chromium desktop. `.xls`, CSV và hệ ngày Excel 1904 chưa có browser E2E mới trong đợt này; các kiểm thử chuẩn hóa/đọc file hiện có vẫn được giữ.

## 5. Xác minh hồi quy

- Lượt tổng hợp cuối dùng `CI=true`, hai worker và `--repeat-each=2`: **34/34 đạt**, không retry, không bỏ qua; 150,5 giây gồm khởi động/build. Đây là 17 kịch bản được chạy hai lần, không phải 34 test khác nhau.
- Đã tạm mô phỏng lỗi export bị giới hạn 1.000 dòng. E2E báo lỗi đúng: mong 1.058 dòng workbook gồm header, nhận 1.001 dòng. Đã khôi phục mã trước lượt đạt cuối; file bị cắt, screenshot và trace của phép thử được giữ riêng tại `artifacts/excel-regression-probe/` trên workspace.
- Lượt cuối giữ 8 workbook tải về tại output test: file xuất và báo cáo lỗi cho hai lần lặp. Report JUnit ghi 0 failure, 0 error, 0 skipped.
- Trong lượt đầu có một timeout khi đóng browser context sau các assertion dữ liệu và một timeout build frontend. Cấu hình hiện ghi video ở lần retry đầu, chờ frontend production tối đa 5 phút; timeout test vẫn là 90 giây, assertion 10 giây. Lượt tổng hợp cuối đạt toàn bộ.

Kết quả các bộ test nền và dọn môi trường được ghi tại [verification.md](verification.md). Workflow có thể chạy sau khi đưa mã vào GitHub; workspace hiện chưa có Git repository/remote nên chưa có lượt hosted Actions được xác minh.

Kiểm tra nền cuối đạt **94/94 backend**, **42/42 frontend unit**, TypeScript/production build và actionlint đạt. Sau lượt chạy, database E2E đã được dọn, hai server E2E đã dừng; PostgreSQL tạm phục vụ kiểm tra local cũng đã dừng.
