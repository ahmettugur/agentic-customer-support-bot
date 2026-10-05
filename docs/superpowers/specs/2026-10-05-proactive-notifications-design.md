# Proaktif Bildirim (Sipariş Kargoya Verildi / Teslim Edildi) — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/proactive-notifications`

## Amaç

Müşteri siparişinin durumunu ancak sorarak öğreniyor ("kargom nerede?" konuşmalarının büyük kısmı). Sipariş
kargoya verildiğinde ve teslim edildiğinde müşteriye kendiliğinden haber verilmeli; kargo firması ve takip
numarası hem e-postada hem botun sipariş yanıtında görünmeli.

Sistemde bugün sipariş durumunu değiştiren bir akış yok (seed veri dışında). Bu yüzden önce bir **durum
güncelleme girişi** (fulfillment/depo sisteminin çağıracağı uç) gerekiyor.

## Kapsam

- Sipariş durum geçişleri: `İşleniyor → Kargolandı` (kargo firması + takip no isteğe bağlı) ve
  `Kargolandı → Teslim Edildi`. İptal/iade kendi (onaylı) akışlarında kalır.
- Atomik koşullu güncelleme (iptal akışındaki gibi): eşzamanlı iptal ile kargolama yarışında yalnızca biri
  kazanır. Aynı duruma tekrar geçiş hata değildir (değişmedi), e-posta tekrar gitmez.
- Bildirim: e-posta (`Email:Notifications:OrderShipped`, `OrderDelivered`), çok pod'da bir kez
  (`INotificationLedger` anahtarı `order-status:{sipariş}:{durum}`). Gönderilemezse talep geri bırakılır, hata
  loglanır; durum güncellemesi yine başarılıdır.
- Bot: sipariş sorgu araçlarının çıktısında kargo firması ve takip numarası.
- Yönetici paneli: "📦 Siparişler" sekmesi — sipariş no ile bul, durumu gör, "Kargoya verildi" / "Teslim
  edildi".

**Kapsam dışı:** gerçek kargo firması entegrasyonu (takip olayları), SMS/push, uygulama içi bildirim kutusu.

## Mimari

- **Domain:** `OrderInfo` + `ShippedAt`, `Carrier`, `TrackingNumber`. `WellKnown.OrderStatuses` değişmez.
- **Outbound `IOrderRepository`:** `MarkShipped(orderId, carrier, tracking, atUtc)`,
  `MarkDelivered(orderId, atUtc)` → `OrderStatusUpdateResult(Change, Order)`; `Change`: `Updated`,
  `Unchanged` (zaten o durumda), `NotFound`, `InvalidTransition`.
- **Postgres:** `catalog.orders` + `shipped_at`, `carrier`, `tracking_number` (migration `AddOrderShipping`).
- **Inbound `IOrderFulfillmentPort` / `OrderFulfillmentService`:** girdiyi doğrular (kargo firması ≤ 64,
  takip no ≤ 64), depoyu çağırır, `Updated` ise `OrderStatusNotifier` ile e-posta.
- **`OrderStatusNotifier`:** müşteri e-postası (`ICustomerRepository.GetEmailAsync`), ayar kapalıysa ya da
  adres yoksa atlar; içerik Türkçe düz metin + HTML (kullanıcı metni escape), sipariş özeti, kargo bilgisi,
  `PublicBaseUrl` varsa bağlantı.
- **Uçlar (yalnız Admin):** `GET /orders/{id}`, `POST /orders/{id}/shipment` (`{ carrier?, trackingNumber? }`),
  `POST /orders/{id}/delivery`. 404 bulunamadı, 409 geçersiz geçiş (mevcut durumla), 400 geçersiz girdi.
  Gerçek bir depo sistemi bu uçları bir servis hesabıyla çağırır.

## Test

- Depo (Testcontainers): geçişler, takip bilgisi, `Unchanged`, `InvalidTransition` (iptal edilmiş sipariş),
  `NotFound`, teslimde `DeliveredAt` (iade süresi buradan sayılır).
- Servis: yalnız `Updated`'da bildirim, doğrulama.
- Bildirimci: tek gönderim (iki pod), ayar kapalı, e-postasız müşteri, gönderim hatasında geri bırakma, içerik.
- Araç: takip bilgisi çıktıda. API: rol, durum kodları. Tarayıcı: Siparişler sekmesi.
