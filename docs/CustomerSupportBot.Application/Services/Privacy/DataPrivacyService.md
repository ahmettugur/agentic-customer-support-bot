# DataPrivacyService

- **Kaynak:** `CustomerSupportBot.Application/Services/Privacy/DataPrivacyService.cs`
- **Tür:** `public sealed class : IDataPrivacyPort`

## Ne işe yarar?

| Üye | Açıklama |
|---|---|
| `RunRetentionAsync(nowUtc)` | Süresi dolan fotoğrafları siler (`IAttachmentStore.DeleteCreatedBeforeAsync`), hareketsiz oturumları (`ISessionManager.GetInactiveSessionIdsAsync`, en eskiden, en fazla `MaxSessionsPerSweep`) siler. Kapalıysa no-op. |
| `ExportCustomerDataAsync(customerId)` | Profil; oturumlar + mesajlar + fotoğraf bilgileri (görüntü verisi yok); puanlar; onay geçmişi; siparişler; şikayetler. |
| `EraseCustomerDataAsync(customerId)` | Müşterinin bağlı oturumları + `ICustomerDataEraser`'lar (müşteri etiketli bellek) + kişiselleştirme profili. Kısmi hatada `DataErasureException(FailedStores)`. |

## Tasarım kararları

- **Silme sırası:** önce tüm `ISessionDataEraser`'lar, **en son** oturumun kendisi
  (`ClearSessionAsync` — önbellek + pod'lar arası yayın + DB). Bir depo hata verirse o partideki
  oturumlar yerinde kalır: saklama taraması sonraki turda onları yeniden bulur, silme talebi güvenle
  tekrarlanır. Tersi sırada bağlı kayıtlar sahipsiz kalır ve bulunamazdı.
- **Silinmeyenler:** sipariş, şikayet ve onay kayıtları yasal/işlemsel kayıttır — dışa aktarılır,
  silinmez. Giriş hesabı ve müşteri ana kaydı da kalır; hesap kapatma ayrı süreçtir.
- **Loglama:** silme talebinde müşteri kimliği ve talep eden loglanır (denetim izi), içerik loglanmaz.

## Bağımlılıklar

`ISessionManager`, `IAttachmentStore`, `IEnumerable<ISessionDataEraser>`, `IEnumerable<ICustomerDataEraser>`,
`ICustomerProfileStore`, `IRatingStore`, `IApprovalQueue`, `IOrderRepository`, `IComplaintRepository`,
`IOptions<DataRetentionOptions>`, `ILogger`.
