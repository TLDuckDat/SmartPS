# SmartPS - VietQR / PayOS Setup

## 1. Database

Mở Package Manager Console tại project `SmartPS` và chạy:

```powershell
Update-Database
```

Các bảng Payment cần có:

- `Payments`
- `PaymentTransactions`
- `PaymentAttempts`
- `PaymentWebhooks`

## 2. Cấu hình PayOS

Không ghi secret trực tiếp vào source khi đưa project lên Git. Có 2 cách local:

### Cách A - appsettings.local.json

Copy `appsettings.local.json.example` thành `appsettings.local.json`, sau đó điền:

```json
{
  "PayOS": {
    "ClientId": "CLIENT_ID_CUA_BAN",
    "ApiKey": "API_KEY_CUA_BAN",
    "ChecksumKey": "CHECKSUM_KEY_CUA_BAN"
  }
}
```

File `appsettings.local.json` sẽ được copy vào output khi build và đã được thêm vào `.gitignore`. File `.example` không chứa secret thật.

### Cách B - biến môi trường

```powershell
$env:SMARTPS_PAYOS__CLIENTID="..."
$env:SMARTPS_PAYOS__APIKEY="..."
$env:SMARTPS_PAYOS__CHECKSUMKEY="..."
```

Biến môi trường được ưu tiên sau `appsettings.local.json`.

## 3. Webhook local

Mặc định SmartPS lắng nghe:

`http://localhost:5005/api/payment/webhook`

Endpoint kiểm tra health:

`http://localhost:5005/health`

PayOS ở Internet không thể gọi trực tiếp `localhost`. Muốn test webhook PayOS thật cần một HTTPS public URL/tunnel trỏ về máy chạy SmartPS.

## 4. Luồng nghiệp vụ

Camera/OCR -> Parking Session -> Tính phí -> VietQR -> Pending -> Webhook -> kiểm tra chữ ký/reference/orderCode/amount -> đối soát PayOS -> Paid -> hoàn tất phiên gửi xe -> cập nhật barrier/UI.

Xe có phí và chọn VietQR sẽ không bị Auto Checkout bỏ qua bước thanh toán. Vé tháng/cước 0đ vẫn có thể tự động xuất bãi.

## 5. Không để lộ secret

Không commit `appsettings.local.json` có key thật. Không gửi `ClientId`, `ApiKey`, `ChecksumKey` vào chat hoặc source public.
