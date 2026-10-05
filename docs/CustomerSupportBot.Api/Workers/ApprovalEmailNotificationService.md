# ApprovalEmailNotificationService

- **Kaynak:** `CustomerSupportBot.Api/Workers/ApprovalEmailNotificationService.cs`
- **Tür:** `IHostedService`

Başlangıçta `IApprovalQueue.RequestDecided` olayına abone olur, durunca bırakır. Her olayı arka planda
`ApprovalResultEmailService.HandleDecidedAsync`'e iletir; hata loglanır, karar akışı beklemez. Tek gönderim
bildirim defteriyle sağlanır (olay her pod'da tetiklenir).
