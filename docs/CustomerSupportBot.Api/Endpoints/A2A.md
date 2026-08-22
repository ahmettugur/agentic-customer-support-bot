# A2A Uç Noktaları (Agent-to-Agent Protokolü)

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Endpoints/A2AEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/A2AAuthEndpoints.cs`
- **Namespace:** `CustomerSupportBot.Api.Endpoints`

## 1. Ne İşe Yarar

Botun üç ajanını (ürün, sipariş, şikayet) [A2A protokolü](https://a2a-protocol.org) üzerinden
**dış partner sistemlere** yayınlar. A2A, bir ajanın yeteneklerini (`AgentCard`) ilan edip
JSON-RPC veya HTTP+JSON ile çağrılabilmesini sağlayan bir keşif+iletişim standardıdır — burada
kullanılan ajanlar, chat akışındaki ajanlarla **aynı iş mantığını** (`ICustomerSupportToolsService`
üzerinden) çalıştırır, sadece giriş kapısı chat yerine A2A'dır.

## 2. Hangi Amaçla Kullanılır

Bir partner (örn. bir e-ticaret entegrasyon sistemi) kendi müşterisi adına ürün/sipariş/şikayet
bilgisi sorgulamak istediğinde bu uç noktalara JSON-RPC ya da HTTP+JSON isteği gönderir. İki
ayrı token türü vardır:

- **Partner token'ı** — partnerin kendi kimliğiyle aldığı, hiçbir müşteriye kilitlenmemiş token.
  Yalnızca ürün ajanı için yeterlidir (ürün bilgisi müşteriye özel değildir).
- **Özne (subject) token'ı** — `POST /auth/a2a/token-exchange` ile üretilen, **tek bir
  müşteriye kilitli** kısa ömürlü token. Sipariş ve şikayet ajanları bunu zorunlu kılar.

## 3. Sorumlulukları

- A2A JSON-RPC (`MapA2AJsonRpc`) ve HTTP+JSON (`MapA2AHttpJson`) transport'larını **her iki**
  binding için de üç ajana (`product`, `order`, `complaint`) map eder.
- Her ajan grubuna yetkilendirme politikası (`Partner` ya da `A2ASubject`), rate limit (`a2a`
  policy) ve endpoint filtresi (loglama + ambient müşteri bağlamı) ekler.
- `AgentCard` keşif belgelerini (`/a2a/{ajan}/.well-known/agent-card.json` ve kök
  `/.well-known/agent-card.json`) statik olarak inşa edip yayınlar.
- A2A protokol sürüm kontrolünü ve istek gövdesi boyut sınırını (`UseA2AProtocolGuards`
  middleware'i, `Program.cs`'te `UseAuthorization`'dan SONRA kayıtlı) uygular.
- `/auth/a2a/token-exchange` ile partner→özne token değişimini yürütür (asıl iş
  [A2ATokenExchangeService](../../CustomerSupportBot.Application/Services/A2A/A2ATokenExchangeService.md)'te).
- **Üstlenmediği:** ajan iş mantığı (tool çağrıları) — bunlar `Adapters.Agents` katmanındaki A2A
  köprü sınıflarına (`CustomerSupportBot.Adapters.Agents.A2A`) devredilir; bu dosyalar sadece
  HTTP/JSON-RPC yüzeyini ve yetki/kimlik kurulumunu yönetir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [A2ATokenExchangeService](../../CustomerSupportBot.Application/Services/A2A/A2ATokenExchangeService.md) —
  token değişimini gerçekleştiren Application servisi.
- [IApprovalContextAccessor](../../CustomerSupportBot.Application/Ports/Outbound/IApprovalContextAccessor.md) —
  `A2ASubjectScopeFilter`'ın token'dan okuduğu `customerId`'yi ambient bağlama yazdığı yer; sipariş/
  şikayet tool'ları müşteri kimliğini buradan okur (LLM'den değil).
  Bkz. [A2AAgentCatalog](../../CustomerSupportBot.Adapters.Agents/A2A/A2AAgentCatalog.md).
- `A2AOptions` (`MaxRequestBytes`, `PublicBaseUrl`, `DocumentationUrl`) — `Extensions/A2AServicesExtensions.cs`
  ile DI'a bağlanan yapılandırma; bkz. [A2AServicesExtensions](../Extensions/A2AServicesExtensions.md).
- `Program.cs` — `MapA2AAgentEndpoints`/`MapA2AAuthEndpoints`'i çağırır, `UseA2AProtocolGuards`/
  `UseA2ARejectionLogging` middleware zincirine ekler.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **İki transport da yayınlanır** çünkü A2A istemci SDK'sı `AgentCard`'daki ilk binding'i tercih
  eder ve varsayılan tercihi HTTP+JSON'dır; yalnızca JSON-RPC yayınlansaydı bazı istemciler
  bağlanamazdı.
- **Yetki ayrımı kasıtlı iki katmanlı**: ürün ajanı (müşteriye özel olmayan veri) partner
  token'ıyla, sipariş/şikayet ajanı (müşteriye özel veri) yalnızca değişimle üretilmiş özne
  token'ıyla çağrılabilir — partner token'ı bu ikisi için YETERSİZDİR.
- **Rejection logging ayrı bir middleware olarak var** çünkü `RequireAuthorization` başarısız
  olduğunda `UseAuthorization` pipeline'ı kısa devre yapar ve endpoint filtreleri hiç çalışmaz —
  yani en kritik güvenlik olayı (bir partnerin başka müşterinin verisine erişmeye çalışması)
  filtre bazlı loglamada hiç görünmezdi.
- **AgentCard kimlik doğrulaması istemez** — A2A'da keşif public'tir, kullanım değil.
- **Kök keşif belgesi yalnızca ürün ajanını ilan eder** çünkü A2A'nın standart kök keşif yolu
  tanım gereği tek bir ajan tanımlar; sipariş/şikayet ajanları kendi token gereksinimlerini
  kendi kart adreslerinde ilan eder.
- **`create-response` / body boyutu sınırlaması endpoint filtresi değil ayrı bir middleware**
  (`UseA2AProtocolGuards`) çünkü HTTP+JSON handler'ı istek gövdesini endpoint filtresi
  çalışmadan ÖNCE bind eder; sınırlama daha erken, middleware seviyesinde yapılmak zorunda.

## 6. Metotlar / Üyeler

### `A2AEndpoints`

| Üye | Açıklama |
|---|---|
| `MapA2AAgentEndpoints(IEndpointRouteBuilder)` | Üç ajanın JSON-RPC + HTTP+JSON uç noktalarını, `AgentCard`'larını ve kök keşif belgesini map eder. |
| `UseA2AProtocolGuards(IApplicationBuilder)` | A2A sürüm kontrolü + istek gövdesi boyut sınırlamasını uygulayan middleware. |
| `UseA2ARejectionLogging(IApplicationBuilder)` | Yetkilendirme katmanında (401/403/429) reddedilen `/a2a/*` isteklerini loglar — endpoint hiç çalışmadığı için filtre bazlı loglamanın yakalayamadığı olayları yakalar. |
| `A2ASubjectScopeFilter` (private) | Sipariş/şikayet ajanları için: token'daki `linked_customer_id` claim'ini `IApprovalContextAccessor` scope'una yazar, claim yoksa `403 Forbid` döner. |
| `A2ALogFilter` (private) | Ürün ajanı için: sadece istek/yanıt loglar, ambient kimlik kurmaz (müşteri kimliği gerekmediği için). |

### `A2AAuthEndpoints`

| Üye | Açıklama |
|---|---|
| `MapA2AAuthEndpoints(IEndpointRouteBuilder)` | `POST /auth/a2a/token-exchange` uç noktasını map eder. |
| `POST /auth/a2a/token-exchange` | Girdi: `ExchangeRequest { CustomerId }`. Partner kimliği çağıranın token'ından (`ClaimTypes.Name`) okunur, gövdeden DEĞİL. `A2ATokenExchangeService.ExchangeAsync` ile müşteriye kilitli bir özne token'ı üretir. Yetkisiz/müşteri-yok ayrımı kasıtlı olarak yapılmaz (ikisi de `403`) — aksi halde müşteri numarası taraması yapılabilir. Politika: `RequireAuthorization("Partner")` + `RequireRateLimiting("a2a")`. |

## 7. Bağımlılıklar

Bu sınıflar `static` extension sınıflarıdır, constructor injection kullanmaz; servisleri doğrudan
`IEndpointRouteBuilder.ServiceProvider`'dan (`A2AOptions`) veya endpoint lambda parametrelerinden
(`A2ATokenExchangeService`, `IApprovalContextAccessor`) alır.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [A2ATokenExchangeService](../../CustomerSupportBot.Application/Services/A2A/A2ATokenExchangeService.md)
- [A2AServicesExtensions](../Extensions/A2AServicesExtensions.md)
