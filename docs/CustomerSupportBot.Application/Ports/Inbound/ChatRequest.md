# ChatRequest

**Dosya:** `Ports/Inbound/ChatRequest.cs`
**Tür:** `record`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

`IChatPort`'un giriş DTO'su — kullanıcının bir chat turunda gönderdiği sorguyu, hangi oturuma ait olduğunu ve kimliği doğrulanmış müşteri kimliğini taşır.

## 2. Hangi amaçla kullanılır?

Hem `IChatPort.HandleAsync` (non-streaming) hem `IChatPort.HandleStreamAsync` (SSE streaming) bu tipi parametre olarak alır. **Bu tip artık HTTP body'sinden doğrudan bind edilmez** — Api katmanındaki `ChatEndpoints`, `Api.Models.ChatRequestBody` adlı ayrı bir wire DTO'yu (`Query`, `SessionId` — `CustomerId` YOK) bind eder, sonra JWT claim'inden okunan `CustomerId` ile birlikte bu `ChatRequest`'i elle oluşturur.

## 3. Sorumlulukları

- **Üstlendiği:** Bir chat turunun üç girdisini (`Query`, `SessionId`, `CustomerId`) taşımak.
- **Üstlenmediği:** Kimlik doğrulama — `CustomerId`'nin gerçekten doğru olduğunu garanti etmek bu tipin işi değil, onu dolduran Api katmanının sorumluluğudur.

## 4. Diğer katman/bileşenlerle ilişkileri

- `IChatPort`'un implementasyonu olan `ChatPortService` (Application/Services/Chat) bu tipi tüketir.
- Api katmanındaki chat endpoint'i bu tipi oluşturur.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`CustomerId` alanının varlığı ve XML yorumundaki uyarı önemlidir: **güvenlik açısından kritik bir tasarım kararını belgeler.**

> 🔒 **Güvenlik notu:** `CustomerId`, LLM'in tool çağrısı parametresi olarak serbest metinden çıkardığı bir değer DEĞİLDİR — Api katmanı bu alanı, kimlik doğrulanmış `HttpContext.User`'ın JWT claim'inden doldurur, asla client'ın gönderdiği body'den güvenilir olarak almaz. Bu, "bir müşteri başka birinin müşteri numarasını söyleyip onun adına işlem yaptırabilir mi?" sorusuna karşı alınan önlemdir.
>
> 🔒 **Neden ayrı bir wire DTO var:** Başlarda `ChatRequest`'in kendisi doğrudan minimal API body parametresi olarak bind ediliyordu — `CustomerId` her zaman JWT'den ezilse de, alan public olduğu için Swagger/OpenAPI şemasında görünüyordu ve istemciye "bunu ben doldurabilirim" yanlış izlenimini veriyordu. Çözüm: `CustomerId` içermeyen küçük bir `ChatRequestBody(Query, SessionId)` (bkz. `Api/Models/EndpointModels.cs`) endpoint'in bind ettiği tip oldu; bu `ChatRequest` ise yalnızca endpoint'in içinde, JWT'den okunan değerle birlikte elle inşa ediliyor. `ChatRequest`'in kendisi (Application katmanındaki bu tip) değişmedi — hâlâ `CustomerId` taşıyor, sadece dış API yüzeyinde görünmüyor.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Query` | `string` | Kullanıcının yazdığı/söylediği metin. |
| `SessionId` | `string?` | Mevcut oturum kimliği; `null` ise yeni oturum açılır. |
| `CustomerId` | `string?` | Login'li müşterinin JWT'den doğrulanmış kimliği. |

## 7. Bağımlılıklar

Yok — saf bir DTO.

## Bağlantılar

- [IChatPort](IChatPort.md) — bu DTO'yu kullanan port.
- [ChatResponse](ChatResponse.md) — aynı use case'in çıktı DTO'su.
