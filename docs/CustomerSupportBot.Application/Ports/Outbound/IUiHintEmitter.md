# IUiHintEmitter

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IUiHintEmitter.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IUiHintEmitter`, <summary> Tool fonksiyonlarından streaming pipeline'a UI ipuçları gönderir. Session ID tabanlı ConcurrentDictionary kullanır; AsyncLocal yerine IApprovalContextAccessor üzerinden session ID okur — SDK uyumlu, güvenilir. </summary> <summary> Bir UI ipucunu mevcut session'ın kuyruğuna ekler. Session ID'yi IApprovalContextAccessor'dan otomatik alır.  <para> <b>Dönüş değeri</b>: ipucu kuyruğa girdiyse <c>true</c>, ambient bağlamda session olmadığı için düştüyse <c>false</c>. Bu ayrım kozmetik değil — ipucu düştüğünde ekranda hiçbir şey belirmez, dolayısıyla çağıran tool LLM'e "kullanıcıya gösterildi" diyemez. Sesli (native realtime) kanalda ambient bağlam hiç kurulmadığı için bu yol gerçekten yürünüyor; bkz. <c>ProductToolsService.ProductListTool</c>. </para> </summary> <summary> Verilen session'a ait bekleyen tüm ipuçlarını okuyup kuyruğu temizler. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IUiHintEmitter`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
