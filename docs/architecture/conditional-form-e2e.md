# E2E và CI cho form và Excel

Triển khai và chạy xác minh ngày **17/11/2026**. Bộ Playwright ban đầu có 9 kịch bản form, nay có **17 kịch bản** sau khi bổ sung [8 E2E nhập/xuất Excel](excel-e2e.md), chạy trên Chromium thật, Next production build, ASP.NET Core và PostgreSQL thật.

## 1. Phạm vi hồi quy

| File | Kịch bản |
|---|---|
| [studio.spec.ts](../../frontend-next/e2e/specs/studio.spec.ts) | Tạo ba loại quy tắc qua giao diện, lưu/mở lại AND/OR và kiểm tra form preview; cấu hình trùng bị từ chối và giữ bản nháp; chờ tải cột trước khi cho cấu hình |
| [runtime.spec.ts](../../frontend-next/e2e/specs/runtime.spec.ts) | Hiện/ẩn và khôi phục giá trị nháp; required đổi theo điều kiện; chỉ đọc theo trạng thái cũ/mới và mở khóa bằng lần lưu riêng; giữ dữ liệu đã lưu khi trường bị ẩn; mặc định boolean `1` và số tiền thập phân |
| [public-form.spec.ts](../../frontend-next/e2e/specs/public-form.spec.ts) | Public form không có cookie đăng nhập; required native và kiểm tra chuỗi trắng; hiện/ẩn/chỉ đọc; gửi liên tiếp giữ mặc định false và `1`; request vượt qua kiểm tra giao diện vẫn bị backend chặn 422 |
| [excel-import.spec.ts](../../frontend-next/e2e/specs/excel-import.spec.ts) | Mapping, chọn sheet/header, preview không ghi dữ liệu, lỗi đúng dòng, báo cáo lỗi, upsert, invalidation và preview cũ |
| [excel-export.spec.ts](../../frontend-next/e2e/specs/excel-export.spec.ts) | 1.057 dòng theo bộ lọc và sort khi đang ở trang 2; file rỗng; ownership, FLS và thu hồi quyền export |

Các assertion đối chiếu dữ liệu thật đọc lại từ API, số bản ghi sau request thất bại và định nghĩa quy tắc đã lưu. Không gọi evaluator trong mã ứng dụng để tạo kết quả mong đợi. Mỗi test có App, bảng, dữ liệu và browser context riêng; các test public không nhận cookie admin. Fixture đăng nhập admin qua API thật để tạo dữ liệu và cấu trúc ban đầu.

Không mock API lưu dữ liệu, metadata hoặc xác thực. Test trạng thái tải chỉ giữ request tải cột tại trình duyệt rồi cho request thật tiếp tục, để kiểm tra ổn định trạng thái chờ mà không dùng thời gian sleep cố định. Lỗi JavaScript không được xử lý trên trang sẽ làm test thất bại.

## 2. Môi trường riêng cho mỗi lượt chạy

[run.mjs](../../frontend-next/e2e/run.mjs) đọc `SMARTAPPS_E2E_POSTGRES`, một **PostgreSQL URL** tới database bảo trì `postgres` trên loopback. Tài khoản cần quyền tạo/xóa database. Runner tạo tên `smartapps_e2e_<UUID>`, truyền connection string tương ứng cho API, chạy Playwright rồi xóa đúng database mà lượt chạy đó đã tạo, cả khi test thành công hoặc thất bại.

[playwright.config.ts](../../frontend-next/playwright.config.ts) tự quản lý hai server:

- API tại `http://127.0.0.1:5520`: build cấu hình .NET `E2E`, chạy DLL riêng. Không thay file đang chạy của bản Debug/Release.
- Frontend tại `http://127.0.0.1:5510`: build production rồi chạy `next start`. Biến `SMARTAPPS_E2E=1` chọn output `.next-e2e`, proxy API sang 5520. Output `.next` của phiên phát triển được giữ riêng. Next có thể cập nhật các khai báo type sinh tự động và danh sách include trong tsconfig như khi build thông thường.

`reuseExistingServer=false`: lượt chạy không tái sử dụng server có sẵn ở hai cổng này. Hai script khởi động từ chối chạy nếu thiếu cấu hình database E2E, tránh rơi về database mặc định của ứng dụng. Mật khẩu và token chỉ dùng trong môi trường thử; không có auth state file trong mã nguồn.

Thiết kế dùng [webServer của Playwright](https://playwright.dev/docs/test-webserver) và [fixture tách biệt theo test](https://playwright.dev/docs/test-fixtures).

## 3. Chạy local

Cần Node.js 24, .NET SDK 9 và PostgreSQL 16 đang chạy. Các phiên bản này khớp cấu hình CI. Nếu đã có PostgreSQL thử riêng, dùng URL của instance đó.

Ví dụ khởi động PostgreSQL bằng Docker, khi máy đã có Docker Engine hoạt động:

```powershell
docker run --name smartapps-e2e-pg --rm -d -p 127.0.0.1:15432:5432 -e POSTGRES_PASSWORD=e2e_postgres postgres:16
```

Sau khi PostgreSQL sẵn sàng, tại `frontend-next`:

```powershell
npm ci
npx playwright install chromium
$env:SMARTAPPS_E2E_POSTGRES='postgresql://postgres:e2e_postgres@127.0.0.1:15432/postgres'
npm run test:e2e
```

Linux/macOS dùng biến môi trường tương đương:

```bash
SMARTAPPS_E2E_POSTGRES='postgresql://postgres:e2e_postgres@127.0.0.1:15432/postgres' npm run test:e2e
```

Lọc hoặc lặp test qua cùng runner để vẫn có database riêng và teardown:

```powershell
npm run test:e2e -- --grep "public form"
npm run test:e2e -- --repeat-each=2
npm run test:e2e -- --headed
npm run test:e2e:report
```

`npx playwright test --list` chỉ liệt kê test, không cần database. Khi chạy test, dùng `npm run test:e2e` để runner chuẩn bị môi trường. Không dùng `--ui` với runner này vì nó quản lý một lượt chạy có vòng đời đóng sau khi Playwright kết thúc.

Nếu đã tự tạo container ở ví dụ trên, dừng sau khi dùng:

```powershell
docker stop smartapps-e2e-pg
```

## 4. CI

Workflow [conditional-form-e2e.yml](../../.github/workflows/conditional-form-e2e.yml) chạy khi **push**, **pull request** hoặc **workflow_dispatch**, với quyền `contents: read`:

1. Khởi động service PostgreSQL 16 và chờ health check.
2. Cài Node 24, .NET 9, dependency theo `package-lock.json` và Chromium cùng thư viện Linux.
3. Chạy backend tests với biến `SMARTAPPS_TEST_POSTGRES` để các PostgreSQL/HTTP test không bị bỏ qua.
4. Chạy frontend unit tests.
5. Chạy 17 E2E form và Excel qua runner; bước này build và typecheck frontend production.
6. Luôn thử upload thư mục `artifacts/`, kể cả khi các bước kiểm tra thất bại; lưu 14 ngày.

Hai biến PostgreSQL có định dạng khác nhau: `SMARTAPPS_TEST_POSTGRES` dùng connection string Npgsql cho xUnit; `SMARTAPPS_E2E_POSTGRES` dùng URL cho runner Node. Workflow cung cấp cả hai, chỉ trỏ tới PostgreSQL service của job.

Cấu hình E2E dùng hai worker, timeout mỗi test 90 giây, timeout assertion 10 giây và timeout toàn lượt 10 phút. Trên CI, `.only` bị cấm; test được retry một lần để thu thêm bằng chứng nhưng `failOnFlakyTests=true` vẫn làm job thất bại nếu test chỉ đạt sau retry.

Thời hạn khởi động API là 3 phút; frontend là 5 phút để bao gồm production build và TypeScript. Chỉ thời gian khởi động được mở rộng sau khi quan sát một build local vượt 3 phút trong đợt Excel.

Artifacts:

- `artifacts/backend/backend.trx`: kết quả xUnit trên CI.
- `artifacts/e2e/results.xml`: kết quả JUnit.
- `artifacts/e2e/report/index.html`: báo cáo HTML.
- `artifacts/e2e/test-results/`: workbook tải xuống; screenshot, trace và error context khi test thất bại; video ở lần retry đầu trên CI.

Mở trace tải từ CI bằng `npx playwright show-trace <đường-dẫn-trace.zip>`. Cơ chế report/trace dựa trên [hướng dẫn CI chính thức](https://playwright.dev/docs/ci-intro).

Để các check trở thành điều kiện bắt buộc trước khi merge, quản trị repository có thể đặt job `conditional-forms` làm required status check trong branch protection/ruleset. Thay đổi này là thiết lập trên GitHub, không được thực hiện chỉ bằng file workflow.

## 5. Kết quả xác minh

| Kiểm tra | Kết quả |
|---|---|
| E2E lượt đầu, 2 worker | **9/9 đạt** |
| E2E lặp hai lần với `CI=true`, 2 worker | **18/18 đạt**, không retry, không bỏ qua; 39,7 giây gồm khởi động/build |
| Tái đưa lỗi mất mặc định sau submit | Test public form báo lỗi đúng: mong `normal`, nhận chuỗi rỗng ở phiếu thứ hai; exit code 1 và có screenshot/video/trace |
| Khôi phục mã sau kiểm tra lỗi | Đã khôi phục trước lượt 18/18; không giữ thay đổi mô phỏng lỗi trong ứng dụng |
| Dọn database sau pass và fail | Đã quan sát runner xóa database riêng trong cả hai trường hợp |
| Backend xUnit có PostgreSQL/HTTP | **94/94 đạt**, không bỏ qua |
| Frontend unit tests | **42/42 đạt** |
| TypeScript và production build | Đạt |
| Workflow actionlint 1.7.12 | Đạt |

**Cập nhật đợt Excel:** bộ hiện tại có 17 kịch bản. Lượt tổng hợp với `CI=true` và `--repeat-each=2` đạt **34/34**, không retry, không bỏ qua; 150,5 giây gồm startup/build. Đã xác nhận E2E bắt lỗi mô phỏng export bị cắt ở 1.000 dòng, rồi khôi phục mã và chạy đạt. Chi tiết ở [E2E nhập/xuất Excel](excel-e2e.md); các kết quả 9/9 và 18/18 ở bảng trên là đợt form trước đó.

Kiểm tra local dùng Windows, Node 24.20.0, Chromium được Playwright 1.65.1 cài và PostgreSQL 16 tại 15432. Workflow đã được kiểm tra cú pháp, nhưng **chưa có lượt chạy hosted GitHub Actions** vì workspace hiện chưa là Git repository và chưa có remote. Đưa các file này vào repository GitHub sẽ cho phép trigger workflow; required status check cần cấu hình sau khi job đã xuất hiện.

Bộ test hiện tập trung Chromium desktop. Chưa xác minh Firefox, WebKit hoặc toàn bộ thiết bị di động.

Sau xác minh, runner đã dừng hai server E2E; truy vấn PostgreSQL xác nhận không còn database `smartapps_e2e_*`. PostgreSQL tạm dùng cho lượt kiểm tra local cũng đã được dừng. Report cuối nằm ở `artifacts/e2e/`; bằng chứng lượt mô phỏng lỗi được giữ riêng tại `artifacts/regression-probe/` trên workspace, không nằm trong mã nguồn được commit.
