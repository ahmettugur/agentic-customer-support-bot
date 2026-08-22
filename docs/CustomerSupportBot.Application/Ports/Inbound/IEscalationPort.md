# IEscalationPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IEscalationPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`IEscalationPort`, <summary> Eskalasyon yönetimi için primary port. </summary> <summary>Yeni eskalasyon kaydı oluşturur.</summary> <summary>Açık eskalasyonlar.</summary> <summary>Son N eskalasyon.</summary> <summary> Bir agent'ın görebileceği son <paramref name="count"/> eskalasyon: atanmamış VEYA ona atanmış olanlar.  <para> Daraltma ve limit birlikte, <b>veri kaynağında</b> uygulanır. Önce son N kaydı alıp sonra elemek yanlış sonuç verir: o N kaydın tamamı başka agent'lara aitse liste boş döner, oysa daha gerisinde çağıranın kendi kaydı vardır. Aynı sebeple cache üzerinden de cevaplanamaz — cache'in kendisi bir "son N" penceresidir. </para> </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IEscalationPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
