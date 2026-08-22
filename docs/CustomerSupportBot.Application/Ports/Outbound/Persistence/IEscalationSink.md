# IEscalationSink

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`IEscalationSink`, <summary> Eskalasyon kayıtları için secondary port. </summary> <summary> Bir agent'ın görebileceği son <paramref name="count"/> eskalasyon: atanmamış VEYA ona atanmış olanlar, en yeniden eskiye.  <para> Diğer okumaların aksine bu metot <b>async</b>'tir çünkü cache üzerinden cevaplanamaz. <see cref="GetRecent"/> yalnızca hydrate edilmiş kayıtları görür (Postgres adaptöründe: açık olanlar + son N kapalı); bir agent'ın kendi kapalı kaydı o pencerenin gerisinde kalabilir. Filtreyi cache üzerinde uygulamak sınırı ötelemekten ibarettir — hem daraltma hem limit veri kaynağında yapılmalıdır. </para> </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IEscalationSink`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
