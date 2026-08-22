# IApprovalQueue

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Persistence`

## Ne işe yarar?

`IApprovalQueue`, <summary> HITL onay kuyruğu için secondary port. </summary> <summary> Bekleyen kayıtların <b>süreç-içi cache görünümü</b>. Hızlıdır ama EKSİK olabilir: başka bir pod'un oluşturduğu kayıt bu pod'a Redis pub/sub ile ulaşır ve o mesaj kaybolabilir (pub/sub en fazla bir kez teslim eder; Redis restart'ı veya ağ kesintisi mesajı düşürür). Cache bir kez hydrate olduktan sonra bir daha DB'ye bakmadığı için böyle bir kayıp KALICI olur.  <para> Bu yüzden yalnızca eksikliğin zararsız olduğu yerde kullanılmalıdır — pratikte tek meşru kullanımı, kaydı bu pod'un kendisinin oluşturduğu mükerrer-çağrı kontrolüdür. "Bekleyen işler" listesi, SLA ve süpürme gibi <b>eksikliğin sessiz bir kayba dönüştüğü</b> her yerde <see cref="GetPendingAsync"/> kullanılmalıdır. </para> </summary> <summary> Bekleyen kayıtların <b>kalıcı</b> listesi — kayıtların gerçek kaynağından okur.

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IApprovalQueue`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
