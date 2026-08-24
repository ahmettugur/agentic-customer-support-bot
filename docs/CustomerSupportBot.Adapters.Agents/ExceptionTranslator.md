# ExceptionTranslator

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/ExceptionTranslator.cs`
- **Tür:** `internal static class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`ExceptionTranslator`, Microsoft Agents Framework (MAF) veya HTTP/ağ katmanından fırlatılan teknik istisnaları Domain katmanının anlayacağı [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md) türüne dönüştüren yardımcı sınıftır. `Translate`, kullanıcıya/istemciye güvenle gösterilebilecek sabit bir `context` metni ile sarmalar — sonuçtaki `Message` her zaman bu `context`'tir (verilmişse); orijinal exception `InnerException` olarak korunur (trace/log için).

> 🐞 **Geçmişte farklıydı:** Bu sınıf eskiden exception türüne göre dallanan bir switch
> içeriyordu (`InvalidOperationException`, `TaskCanceledException{InnerException:TimeoutException}`,
> `OperationCanceledException`, `HttpRequestException`). Ölçüldü: her dal AYNI
> `ExternalServiceException` tipini üretiyordu — yalnızca `context` `null` olduğunda
> kullanılan fallback mesaj metni farklıydı. Repo genelindeki tüm çağrı yerleri her zaman bir
> context geçtiği için (`context ?? ...` deseninde context her zaman kazanıyordu) switch'in
> hiçbir dalı pratikte gözlemlenebilir bir fark yaratmıyordu — "tür bazlı çeviri" görünümü
> altında ölü koddu. Sadeleştirildi.
>
> Ayrıca eskiden `WorkflowRunner.RunStreamingAsync`'in üç hata yolu (prompt hazırlığı,
> workflow event döngüsü, finalizasyon) bu sınıfı hiç çağırmıyordu — ham `exception.Message`'ı
> doğrudan istemciye `StreamEvent(Error, ...)` olarak gönderiyorlardı (framework/HTTP iç
> detayları sızma riski). Artık üçü de `Translate(...).Message` kullanıyor; ham detay yalnızca
> trace'e (admin/debug amaçlı) yazılıyor. Bkz. [WorkflowRunner.md](WorkflowRunner.md).

## Hangi amaçla kullanılır`?

- Üst katmanların altyapı bağımlılığı olan MAF framework tiplerine bağımlı olmadan, temiz ve standart domain istisnalarını yakalayabilmesini sağlamak.
- `WorkflowRunner`'ın hem non-streaming (`RunAsync`, `throw`) hem streaming (`RunStreamingAsync`, `yield return StreamEvent.Error`) hata yollarının kullanıcıya **güvenli, sabit** bir metin göstermesini; iç detayın yalnızca trace'e yazılmasını garanti etmek.

## Sorumlulukları

- **Üstlendiği:**
  - Herhangi bir exception'ı, çağıranın verdiği sabit `context` metniyle bir `ExternalServiceException`'a sarmak.
  - Orijinal exception'ı `InnerException` olarak korumak (log/trace zincirinde kaybolmasın diye).
- **Üstlenmediği:**
  - Exception türüne göre farklı bir `DomainException` alt tipi üretmek (tek tip: `ExternalServiceException`).

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `Translate` | Metot | `public static DomainException Translate(Exception ex, string? context = null)` | İstisnayı `ExternalServiceException` nesnesine çevirir; `Message` = `context ?? "Ajan workflow hatası: {ex.Message}"`. |

## Bağımlılıklar

- [DomainException](../CustomerSupportBot.Domain/Exceptions/DomainException.md)
- [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md)
