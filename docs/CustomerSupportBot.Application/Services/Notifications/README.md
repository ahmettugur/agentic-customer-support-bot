# CustomerSupportBot.Application.Services.Notifications

## ApprovalResultEmailService

Onay sonucunu müşteriye e-postayla bildirir (uygulama içi bildirime ek). `IApprovalQueue.RequestDecided`
olayı Api'deki `ApprovalEmailNotificationService` (`IHostedService`) tarafından bu servise bağlanır.

- **Bir kez gönderim:** olay karar veren pod'da ve pub/sub ile diğer pod'larda da tetiklenir. Gönderimden önce
  `approval-result:{id}` anahtarı `INotificationLedger`'da talep edilir (Postgres: `notifications.sent_log`,
  `INSERT … ON CONFLICT DO NOTHING`); yalnızca kazanan pod gönderir.
- **Yalnızca sonuçlanmış kayıt:** reddedildi, zaman aşımı, ya da onaylandı **ve** yürütme bitti (başarılı/başarısız).
  Bekleyen ve yürütmesi süren kayıt atlanır.
- **Alıcı:** katalog müşteri kaydındaki e-posta (`ICustomerRepository.GetEmailAsync`); adres yoksa gönderilmez.
  Loglarda adres maskelenir.
- **Hata:** SMTP hatası loglanır ve defter talebi geri bırakılır (otomatik yeniden deneme yok; uygulama içi
  bildirim yine vardır). İşleyici karar akışını hiçbir zaman beklemez/bozmaz.

## ApprovalEmailComposer

Türkçe konu ve gövde (düz metin + HTML). Konu: "Talebiniz onaylandı — İade", "… reddedildi", "… zaman aşımına
uğradı", "… onaylandı ancak tamamlanamadı". Gövdede yürütme sonucu ya da red gerekçesi ve (ayarlıysa) bağlantı.
HTML'e giren her metin escape edilir.
