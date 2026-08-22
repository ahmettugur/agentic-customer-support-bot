# ParallelExecutionOptions

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/ParallelExecutionOptions.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`ParallelExecutionOptions`, Application/Services/ParallelExecutionOptions.cs Compound query alt görevleri için paralel çalıştırma ayarları.  Paralel çalıştırma yalnızca bütün tool'ları salt-okunur olan ajanlara açılır. LLM'in ürettiği intent, karma bir ajanın hangi tool'u gerçekten çağıracağını garanti etmez; OrderAgent gibi hem okuma hem yazma tool'u taşıyan ajanlar bu nedenle her zaman sıralı çalışır. <summary> Bileşik (compound) sorgudaki alt görevlerin paralel yürütme politikası. </summary> <summary>Paralel sub-task çalıştırma aktif mi?</summary> <summary> Aynı anda en fazla kaç yan-etkisiz alt görev çalıştırılabilir. </summary> <summary>Tek bir compound sorguda kabul edilen en yüksek alt görev sayısı.</summary> <summary>Tüm alt görevlerin paylaştığı uçtan uca zaman bütçesi.</summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ParallelExecutionOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `IsReadOnly`
```csharp
public bool IsReadOnly(SubTask sub)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Özellikler/Properties

- `Enabled` (`bool`): İlgili veriyi temsil eden özellik.
- `MaxDegreeOfParallelism` (`int`): İlgili veriyi temsil eden özellik.
- `MaxSubTasks` (`int`): İlgili veriyi temsil eden özellik.
- `TimeoutSeconds` (`int`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
