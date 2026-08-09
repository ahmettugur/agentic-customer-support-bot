# SideEffectIdempotencyCache

## Ne İşe Yarar
Yan etkili tool çağrıları (sipariş oluşturma, şikayet kaydı) için parametre-imzası tabanlı kısa pencereli mükerrer çağrı koruması sağlar.

## Hangi Amaçla Kullanılır
LLM'in aynı tool'u tekrar çağırması, istemci tarafı çift gönderim veya compound query'de paralel alt görevlerin aynı işlemi tetiklemesi durumlarında mükerrer kaydı engeller.

## Sorumlulukları
- Tool adı + parametre imzasının SHA256 hash'ini hesaplamak.
- 60 saniye pencere içinde aynı imzalı çağrının cache'te olup olmadığını kontrol etmek.
- Cache hit durumunda orijinal sonucu döndürmek (tool tekrar çalıştırılmaz ama sonuç taklit de edilmez).
- Yalnızca başarılı sonuçları cache'lemek (hata durumunda tekrar deneme engellenmez).
- Bellek sınırı (200 entry) ve zaman bazlı expire ile cache'i yönetmek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kullanan sınıf**: `CustomerSupportToolsService` — `OrderToolsService.PlaceOrderAsync`, `ComplaintToolsService.RegisterComplaintAsync` gibi yan etkili tool'lardan önce kontrol eder.
- **DI kaydı**: Singleton (süreç geneli cache).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
`CustomerSupportChatManager.MaxDuplicateToolCalls` guard'ı yalnızca tek bir workflow koşusunun mesaj geçmişine bakar. Compound query'de her alt görev ayrı workflow koşusu olarak (bazen paralel) çalışır — o guard mükerrer çağrıları göremez. Bu cache süreç genelinde koruma sağlar.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `TryGetRecent(toolName, parameters, out recent)` | Cache'te aynı imzalı çağrı var mı kontrol eder. |
| `Record(toolName, parameters, result, entityId)` | Başarılı çağrıyı cache'e kaydeder. |
| `Clear()` | Cache'i temizler (test ve oturum sıfırlama için). |

### İlişkili Record

| Record | Açıklama |
|--------|----------|
| `IdempotentCall` | Cache'lenmiş çağrı: `Result`, `EntityId`, `RecordedAt`. |

## Bağımlılıklar
Yok — saf C# (System.Security.Cryptography, System.Text).
