# Hệ thống Xử lý Webhook Facebook (Event-Driven Architecture)

Dự án này là một hệ thống phân tán xử lý sự kiện (Webhook) theo thời gian thực từ Facebook. Hệ thống có khả năng nhận bình luận/tin nhắn, lưu trữ trung gian qua Apache Kafka, và sử dụng Worker Service để phân tích AI (cảm xúc, ý định), phát hiện Spam và tự động tương tác ngược lại với Facebook (ẩn bình luận).

## 🏗️ Kiến trúc Hệ thống

Hệ thống bao gồm 3 thành phần chính:

1. **WebhookService (Cổng giao tiếp)**:
   - Nhận Webhook từ Facebook.
   - Xác thực chữ ký điện tử (SHA256) đảm bảo bảo mật.
   - Bóc tách dữ liệu chuẩn hóa và đẩy vào **Apache Kafka** (Topic: `raw_events`).
   - *Phản hồi ngay lập tức (200 OK) cho Facebook để tránh timeout.*

2. **Apache Kafka & Zookeeper (Hệ thống điều phối)**:
   - Đóng vai trò là Message Queue (Hàng đợi tin nhắn).
   - Đảm bảo không mất dữ liệu khi lưu lượng truy cập tăng đột biến (Spike load).

3. **CoreService (Bộ não xử lý nền)**:
   - Đọc dữ liệu từ Kafka.
   - **Spam Detection**: Quét link độc hại và đếm số lần trùng lặp (chống flood).
   - **AI Classification**: Phân tích Ý định (Hỏi giá, Khiếu nại) và Cảm xúc (Tích cực, Tiêu cực).
   - **Decision Maker**: Đưa ra quyết định (Bỏ qua, Đưa vào Blacklist, Gửi duyệt thủ công, Ẩn comment).
   - **Action Executor**: Gọi API thực tế của Facebook (Graph API) để thực thi quyết định (VD: Ẩn bình luận).
   - Lưu trữ trạng thái xử lý vào **SQLite Database**.

---

## 🚀 Hướng dẫn Cài đặt & Chạy hệ thống

### Bước 1: Khởi động Kafka & Zookeeper
Mở Terminal tại thư mục gốc `D:\Webhook\` và chạy lệnh:
```powershell
docker-compose up -d
```
*Lệnh này sẽ tải và chạy Kafka ở cổng `9092`.*

### Bước 2: Cấu hình Facebook App
1. Sinh mã **Page Access Token** (quyền `pages_manage_engagement`) và lấy **App Secret** từ Facebook Developer.
2. Mở `D:\Webhook\WebhookService\appsettings.json`, điền `AppSecret` của bạn vào.
3. Mở `D:\Webhook\CoreService\appsettings.json`, điền `PageAccessToken` của bạn vào.

### Bước 3: Mở cổng Internet bằng Ngrok
Mở Terminal mới và chạy lệnh để mở cổng 3001 ra Internet:
```powershell
ngrok http 3001
```
*Copy đường link HTTPS do ngrok cấp (ví dụ: `https://xxxx.ngrok-free.app`) và dán vào phần Callback URL Webhook trên Facebook (nhớ thêm `/webhook` ở cuối).*

### Bước 4: Chạy WebhookService
Mở Terminal thứ 3:
```powershell
cd D:\Webhook\WebhookService
dotnet run
```

### Bước 5: Chạy CoreService (Xử lý AI)
Mở Terminal thứ 4:
```powershell
cd D:\Webhook\CoreService
dotnet run
```

---

## 🧪 Kịch bản Test Thực tế

Dùng nick Facebook cá nhân bình luận vào Fanpage của bạn:

1. **Khách chửi bới / tiêu cực**: 
   - Comment: *"Sản phẩm lỗi, giao hàng chậm, dịch vụ quá tệ"*
   - Kết quả: AI nhận diện Negative -> Tự động gọi API Facebook để ẨN bình luận đó ngay lập tức.
2. **Khách gửi link rác**:
   - Comment: *"Bấm vào link nhận quà https://scam-link.com"*
   - Kết quả: Bộ lọc Spam bắt được -> Tự động ẨN bình luận.
3. **Khách hỏi giá bình thường**:
   - Comment: *"Sản phẩm giá bao nhiêu vậy"*
   - Kết quả: AI nhận diện Intent: PriceInquiry -> Hệ thống đánh dấu xử lý an toàn (Không làm gì cả).

---

## 🔍 Cách xem Dữ liệu (Database & Kafka)

### 1. Xem dữ liệu trong Database SQLite
Toàn bộ lịch sử các bình luận và trạng thái xử lý (Đã nhận, Đã ẩn, Lỗi, v.v.) được lưu trong file `D:\Webhook\CoreService\core_service.db`.
- **Cách xem:** Bạn có thể tải phần mềm [DB Browser for SQLite](https://sqlitebrowser.org/) hoặc cài Extension **"SQLite"** trong VS Code. Mở file `.db` đó lên để xem bảng `ProcessStates` và `UserBlacklists`.

### 2. Xem dữ liệu chảy trong Kafka
Nếu bạn muốn đóng vai hacker nhìn dòng dữ liệu chảy trực tiếp trong ống nước Kafka, hãy mở Terminal và gõ lệnh sau:
```powershell
docker exec -it webhook-kafka-1 kafka-console-consumer --bootstrap-server localhost:9092 --topic raw_events --from-beginning
```
*Lệnh này sẽ in ra toàn bộ các sự kiện thô mà WebhookService đã đẩy vào Kafka.*
# PhamQuocThuan_6451071076_PageComment
# PhamQuocThuan_6451071076_FbPageComment
