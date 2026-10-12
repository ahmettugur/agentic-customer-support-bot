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

## OrderStatusEmailService / OrderStatusEmailComposer

Proaktif bildirim: sipariş **kargoya verildiğinde** ve **teslim edildiğinde** müşteriye e-posta
(`Email:Notifications:OrderShipped`, `OrderDelivered`). `OrderFulfillmentService` yalnızca gerçek geçişte
(`OrderStatusChange.Updated`) çağırır — tekrarlanan depo çağrısı ikinci e-posta göndermez.

- **Bir kez gönderim:** `order-status:{sipariş}:{shipped|delivered}` anahtarı `INotificationLedger`'da talep edilir.
- **Alıcı/hata:** onay e-postasıyla aynı — adres yoksa atlanır (talep tutulur), SMTP hatasında talep geri bırakılır.
  Bildirim hatası durum güncellemesini geri almaz.
- **İçerik:** konu "Siparişiniz kargoya verildi — #1044" / "… teslim edildi — #1044"; gövdede ürünler, kargo firması
  ve takip numarası, (ayarlıysa) bağlantı. HTML'e giren her metin escape edilir.

## ComplaintStatusEmailService

Şikayet **incelemeye alındığında** (üstlenme, Beklemede'den atama, yeniden açma) ve **kapandığında** (çözüldü / reddedildi,
müşteriye görünen açıklamayla) müşteriye e-posta (`Email:Notifications:ComplaintInReview`, `ComplaintClosed`).

- **Bir kez gönderim:** `complaint:{no}:{in_review|closed}:{sürüm}` anahtarı `INotificationLedger`'da talep edilir. Sürüm
  her geçişte arttığı için yeniden açılıp tekrar kapanan şikayet yeni e-posta alır; aynı geçişin iki pod'da işlenmesi
  tek e-posta gönderir.
- **Alıcı/hata:** sipariş e-postasıyla aynı; bildirim hatası geçişi geri almaz.
- **İçerik** (`ComplaintStatusEmailComposer`): konu "Şikayetiniz inceleniyor — #1006" / "… çözüldü …" / "… sonuçlandı …"
  (reddedildi). İç not asla girmez; HTML'e giren her metin escape edilir.

## ApprovalEmailComposer

Türkçe konu ve gövde (düz metin + HTML). Konu: "Talebiniz onaylandı — İade", "… reddedildi", "… zaman aşımına
uğradı", "… onaylandı ancak tamamlanamadı". Gövdede yürütme sonucu ya da red gerekçesi ve (ayarlıysa) bağlantı.
HTML'e giren her metin escape edilir.
