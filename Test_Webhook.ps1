$webhookUrl = "http://localhost:3001/webhook"
$secret = "dd7ee9459f895f9284d4e7b689e0f70f"

function Send-TestEvent {
    param(
        [string]$TestName,
        [string]$Payload
    )
    
    Write-Host "========== $TestName ==========" -ForegroundColor Cyan
    Write-Host "Payload gửi đi:" $Payload -ForegroundColor Gray
    
    # Tính toán chữ ký X-Hub-Signature-256 giống như Facebook
    $hmac = new-object System.Security.Cryptography.HMACSHA256
    $hmac.Key = [Text.Encoding]::UTF8.GetBytes($secret)
    $hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Payload))
    $signature = "sha256=" + ([BitConverter]::ToString($hash) -replace '-', '').ToLower()
    
    try {
        $response = Invoke-RestMethod -Uri $webhookUrl -Method Post -Body $Payload -Headers @{
            "X-Hub-Signature-256" = $signature; 
            "Content-Type" = "application/json"
        }
        Write-Host "Kết quả (HTTP 200 OK):" $response -ForegroundColor Green
    } catch {
        Write-Host "Lỗi:" $_.Exception.Message -ForegroundColor Red
    }
    Write-Host ""
}

# 1. Test Webhook Verification (GET)
Write-Host "========== TEST 1: Xác thực Webhook (Verify Token) ==========" -ForegroundColor Cyan
$verifyUrl = "$webhookUrl`?hub.mode=subscribe&hub.verify_token=thuan_pham_quoc&hub.challenge=1122334455"
try {
    $verifyResponse = Invoke-RestMethod -Uri $verifyUrl -Method Get
    Write-Host "Kết quả xác thực:" $verifyResponse -ForegroundColor Green
} catch {
    Write-Host "Lỗi xác thực:" $_.Exception.Message -ForegroundColor Red
}
Write-Host ""
Start-Sleep -Seconds 1

# 2. Test Comment Spam (Chứa Link)
$payloadSpam = '{"entry":[{"changes":[{"value":{"item":"comment","from":{"id":"user_1","name":"SpamBot"},"message":"Bấm vào link nhận quà: https://scam-link.com"}}]}]}'
Send-TestEvent -TestName "TEST 2: Bình luận Spam (Có Link)" -Payload $payloadSpam

Start-Sleep -Seconds 1

# 3. Test Comment Hỏi Giá
$payloadPrice = '{"entry":[{"changes":[{"value":{"item":"comment","from":{"id":"user_2","name":"KhachHang"},"message":"sản phẩm này giá bao nhiêu vậy shop?"}}]}]}'
Send-TestEvent -TestName "TEST 3: Bình luận Hỏi Giá (Intent: PriceInquiry)" -Payload $payloadPrice

Start-Sleep -Seconds 1

# 4. Test Comment Tích Cực
$payloadPositive = '{"entry":[{"changes":[{"value":{"item":"comment","from":{"id":"user_3","name":"FanCung"},"message":"bài viết hay quá tuyệt vời"}}]}]}'
Send-TestEvent -TestName "TEST 4: Bình luận Tích Cực (Sentiment: Positive)" -Payload $payloadPositive

Write-Host "Hoàn tất! Hãy kiểm tra cửa sổ Terminal của CoreService để xem AI phân loại và hệ thống xử lý thế nào nhé." -ForegroundColor Yellow
