# Hướng dẫn Vận hành Production, Reverse Proxy & Sao lưu/Khôi phục

Tài liệu này hướng dẫn đội ngũ vận hành và DevOps triển khai **SmartApps Platform** an toàn trên môi trường Production, kèm quy trình sao lưu & khôi phục dữ liệu.

## 1. Checklist trước khi Go-Live Production

- [ ] **Secret & Mật khẩu:** Đặt biến môi trường `AUTH_SECRET` (`Auth__Secret`) là chuỗi ngẫu nhiên tối thiểu 32 ký tự và `POSTGRES_PASSWORD` mạnh (không dùng giá trị mẫu).
- [ ] **CORS:** Cấu hình `Cors__AllowedOrigins` chứa đúng domain truy cập (ví dụ `https://smartapps.example.com`). Nếu để trống trong `Production`, API mặc định chặn các request cross-origin từ domain lạ.
- [ ] **Tắt Seed Demo Data:** Đảm bảo `ASPNETCORE_ENVIRONMENT=Production` (hoặc `Database__SeedDemoData=false`) để không khởi tạo tài khoản/dữ liệu mẫu trên môi trường thật.
- [ ] **HTTPS & Reverse Proxy:** Đặt Nginx/Caddy/Traefik phía trước, sử dụng mẫu [`deploy/nginx.conf.example`](../../deploy/nginx.conf.example) để:
  - Bắt buộc HTTPS (`443`).
  - Tắt `proxy_buffering` và tăng `proxy_read_timeout` cho `/api/notifications/stream` (SSE).
- [ ] **Healthcheck:** Kiểm tra định kỳ cả `/health` (liveness) và `/health/ready` (readiness kết nối PostgreSQL).

## 2. Khởi chạy Production bằng Docker Compose

```bash
export POSTGRES_PASSWORD="<mat-khau-postgres-manh>"
export AUTH_SECRET="<chuoi-ngau-nhien-it-nhat-32-ky-tu>"
docker compose up --build -d
```

Theo dõi trạng thái healthcheck:

```bash
docker compose ps
curl -f http://localhost:5200/health/ready
```

## 3. Quy trình Sao lưu (Backup)

Dữ liệu của SmartApps nằm ở **2 nơi** cần sao lưu đồng thời:
1. **PostgreSQL (`smartapps_db`)** — metadata, bản ghi `JSONB`, phân quyền, nhật ký, thông báo.
2. **Thư mục tệp đính kèm (`smartapps_uploads` / `backend-dotnet/uploads/`)** — các file/ảnh người dùng tải lên.

### Lệnh sao lưu thủ công / Cron hàng ngày

```bash
BACKUP_DIR="/var/backups/smartapps/$(date +%Y%m%d_%H%M%S)"
mkdir -p "$BACKUP_DIR"

# 1. Dump PostgreSQL (định dạng custom nén)
docker exec smartapps_postgres pg_dump -U postgres -Fc smartapps_db > "$BACKUP_DIR/smartapps_db.dump"

# 2. Nén thư mục uploads
docker run --rm -v smartapps_smartapps_uploads:/data:ro -v "$BACKUP_DIR":/backup alpine \
  tar czf /backup/uploads.tar.gz -C /data .
```

## 4. Quy trình Khôi phục & Diễn tập (Restore)

> **Nguyên tắc:** Định kỳ hàng tháng diễn tập khôi phục trên một database/môi trường tách biệt để chắc chắn bản backup dùng được.

```bash
BACKUP_DIR="/var/backups/smartapps/<timestamp>"

# 1. Khôi phục PostgreSQL vào database sạch
docker exec -i smartapps_postgres pg_restore -U postgres -d smartapps_db --clean --if-exists < "$BACKUP_DIR/smartapps_db.dump"

# 2. Khôi phục thư mục uploads
docker run --rm -v smartapps_smartapps_uploads:/data -v "$BACKUP_DIR":/backup alpine \
  sh -c "rm -rf /data/* && tar xzf /backup/uploads.tar.gz -C /data"
```

Sau khi khôi phục, kiểm tra lại `/health/ready` và đăng nhập thử để xác nhận dữ liệu và tệp đính kèm hoạt động bình thường.
