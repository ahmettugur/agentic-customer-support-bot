# IdempotentCall

- **Kaynak:** `CustomerSupportBot.Application/Services/Tools/SideEffectIdempotencyCache.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## Ne işe yarar?

`IdempotentCall`, Application/Services/Tools/SideEffectIdempotencyCache.cs Yan etkili tool çağrıları için kısa pencereli mükerrer-çağrı koruması.  Neden gerekli? MaxDuplicateToolCalls guard'ı CustomerSupportChatManager içinde, yani TEK bir workflow koşusunun mesaj geçmişine bakar. Compound query'de her alt görev AYRI bir workflow koşusu olarak (bazen paralel) çalıştığı için o guard mükerrer order_placement_tool / complaint_registration_tool çağrılarını göremez. LLM'in aynı tool'u yeniden çağırması ve istemci tarafı çift gönderim de aynı sonucu doğurur. Bu cache, süreç genelinde son N saniyedeki aynı-parametreli çağrıyı yakalar.  Davranış: Cache isabetinde tool ÇALIŞTIRILMAZ (DB'ye yazılmaz), ancak ilk sonuç sessizce taklit de edilmez — çağırana "bu kaydı az önce oluşturdum" bilgisini içeren ayırt edilebilir bir sonuç döndürmesi için orijinal kayıt geri verilir. Böylece hem mükerrer kayıt engellenir hem meşru tekrar talebi görünür kalır. <summary> Bir yan etkili tool çağrısının cache'lenmiş sonucu. </summary> <param name="Result">Orijinal çağrının döndürdüğü sonuç.</param> <param name="EntityId">Oluşturulan kaydın kimliği (sipariş/şikayet numarası) — uyarı mesajında kullanılır.</param> <param name="RecordedAt">Kaydın alındığı UTC zamanı.</param>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IdempotentCall`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `TryGetRecent`
```csharp
public bool TryGetRecent(
        string toolName,
        IReadOnlyList<object?> parameters,
        out IdempotentCall recent)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Record`
```csharp
public void Record(
        string toolName,
        IReadOnlyList<object?> parameters,
        ToolResult result,
        string? entityId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Clear`
```csharp
public void Clear()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
