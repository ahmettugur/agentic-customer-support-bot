# A2A (Agent2Agent) — Ajanların Dış Sistemlere Açılması

**Durum:** Ürün, Sipariş ve Şikayet ajanları yayında — **hepsi salt-okunur**.
**Varsayılan:** kanal **KAPALI** (`A2A:Enabled=false`) ve partner listesi boş.

## 1. Neden ayrı ajanlar — workflow ajanları doğrudan açılamaz

Sohbet/sesli kanaldaki ajanlar (`Adapters.Agents/Team/`) kullanıcıya yönelik metin üretmez:

```csharp
ResponseFormat = ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>()
```

Bunlar iç boru hattı bileşenleridir; çıktıları `WorkflowResponseExtractor` ile ayrıştırılır ve
`ResponseAgent` tarafından cümleye çevrilir. Olduğu gibi yayınlansalardı dış çağırana **ham
reasoning JSON'u** döner ve `preToolCheck`, `postToolReflection`, güven skorları, eskalasyon
bayrakları gibi **iç alanlar sızardı**.

Bu yüzden A2A ajanları `A2AAgentCatalog` içinde **ayrı örnekler** olarak kurulur: şema yok, düz
metin çıktı, kendi talimatları (`Prompts/agents/a2a-*.md`). Workflow ajanlarına hiç dokunulmaz.

## 2. Yetki yapısaldır, koşullu değil

Yan etkili tool'lar (sipariş oluşturma/iptal, iade, şikayet kaydı) bu ajanlara **hiç verilmez** —
bir `if` dalıyla engellenmez. Üstüne çalışma zamanı bariyeri vardır:

```csharp
A2AAgentCatalog.ReadOnlyOnly(...)   // kaynağı: WellKnown.SideEffectToolOwners
```

Bariyerin gerekçesi: yan etkili tool listesi ileride büyüyecek ve o an bu dosyayı kimse
açmayacak. Kontrol olmasaydı, yeni bir yazma tool'u A2A ajanına yanlışlıkla eklendiğinde hata
ancak **dış bir sistem onu çağırdığında** fark edilirdi.

> Sipariş tool'ları `ApprovalGateService`'in mevcut builder'larından **yeniden kullanılır**,
> kopyalanmaz — böylece "müşteri kimliği LLM'den değil ambient bağlamdan gelir" garantisi
> olduğu gibi taşınır.

## 3. Kimlik — iki ayrı token

| Token | Cevapladığı soru | Ne yapabilir |
|---|---|---|
| **Partner** (`Partner` rolü) | *Hangi sistem arıyor?* | Yalnızca token değişimi. Ajanları **doğrudan çağıramaz**. |
| **Özne** (`A2ASubject` rolü) | *Hangi müşteri adına?* | Ajanları çağırır. **Tek müşteriye kilitli**, kısa ömürlü. |

Akış (OAuth 2.0 Token Exchange, RFC 8693 deseni):

```
partner token ──> POST /auth/a2a/token-exchange { customerId }
                     │
                     ├─ IA2ASubjectAuthorizer: "bu partner bu müşteri adına hareket edebilir mi?"
                     │     └─ HAYIR → 403 (sebep AÇIKLANMAZ, aşağıya bakın)
                     └─ EVET → kısa ömürlü özne token'ı (linked_customer_id claim'i ile)
                                  │
                                  └─> POST /a2a/order   (A2ASubject policy + scope filtresi)
```

**Partner token'ı neden doğrudan kullanılmıyor:** o "hangi sistem" sorusunu cevaplar, "hangi
müşteri" sorusunu değil. Doğrudan kullanılsaydı müşteri kimliğinin **çağrı gövdesinde parametre**
olarak taşınması gerekirdi — yani tool'ların güvendiği kimlik, istemcinin serbestçe
değiştirebildiği bir alan olurdu. Bu, sohbet kanalında bilerek kapatılan açığın
(`SessionState.CustomerId` yerine `AuthenticatedCustomerId`) A2A'da yeniden açılması demekti.

**Yetkisiz ile "müşteri yok" ayrımı bilerek yapılmaz** (ikisi de 403): ayrım yapılsaydı
dışarıdan müşteri numarası taranarak hangi numaraların var olduğu öğrenilebilirdi.

### Yetki kararı bir İŞ KURALIDIR

`IA2ASubjectAuthorizer` ayrı bir porttur; "hangi partner hangi müşteriye erişebilir" sorusunun
cevabı işletmeye göre değişir (partnerin tanıttığı müşteriler, sözleşme kapsamı, bayi
hiyerarşisi). Varsayılan implementasyon yapılandırmadan okur ve **hiçbir yapılandırma yoksa her
şeyi reddeder** — "ayarı unuttum" durumunun sonucu sızıntı değil, çalışmama olmalıdır.

> ⚠️ Gerçek partner-müşteri ilişkisi bir tabloya/sözleşmeye bağlanacaksa bu port yeniden
> implemente edilmelidir; çağıran taraf hiç değişmez.

## 4. Ambient kimlik — zincirin kilit taşı

`A2ASubjectScopeFilter`, ajanı çalıştırmadan **önce** `IApprovalContextAccessor.SetScope(...)`
ile müşteri kimliğini kurar. Sohbet ve sesli kanallar aynı işi `ChatPortService` /
`RealtimeBridgeService` içinde yapar; A2A'nın karşılığı budur.

**Bu filtre olmadan sipariş ajanı sessizce işe yaramaz:** tool'lar kimliği ambient bağlamdan
alır, kurulmazsa boş string olur, sorgular hiçbir şey bulamaz ve ajan "siparişiniz yok" der —
yani altyapı eksiği **yanlış olguya** dönüşür. Claim yoksa istek reddedilir.

## 5. Endpoint'ler

| Endpoint | Token | Rate limit |
|---|---|---|
| `POST /auth/a2a/token-exchange` | `Partner` | `a2a` |
| `POST /a2a/product` · `/a2a/product/message:send` | `Partner` | `a2a` |
| `POST /a2a/order` · `/a2a/order/message:send` | `A2ASubject` | `a2a` |
| `POST /a2a/complaint` · `/a2a/complaint/message:send` | `A2ASubject` | `a2a` |
| `GET /a2a/{agent}/.well-known/agent-card.json` | — (public) | — |

### İki transport

A2A birden fazla protokol binding'i tanımlar; **ikisi de yayınlanır**:

| Binding | Yol | Kurulum |
|---|---|---|
| JSON-RPC | `POST /a2a/{agent}` | `MapA2AJsonRpc` |
| HTTP+JSON | `POST /a2a/{agent}/message:send` (+ `tasks/*`) | `MapA2AHttpJson` |

> ⚠️ **Neden ikisi de:** SDK istemcisi (`A2AClient.CreateFromCard`) binding'i **AgentCard'dan**
> seçer ve varsayılan tercihi **HTTP+JSON**'dır. Yalnızca JSON-RPC yayınlansaydı, kartı okuyup
> HTTP+JSON deneyen istemciler sessizce bağlanamazdı.
>
> **Yetki/rate-limit/scope zinciri İKİ yolda da aynı uygulanır.** Birinde koruma atlanırsa
> diğerindeki tüm kontroller anlamsız kalır — üstelik kart HTTP+JSON'ı tercih ettirdiği için
> sızıntı *varsayılan* yol olurdu. `HttpJsonTransport_*` testleri bunu iki uçta da kilitler.

### Bilinen sınır: `/a2a/{agent}/card` boş kart döndürüyor

Köprü (MAF Hosting.A2A **1.12-preview**) transport seviyesinde kendi kart endpoint'ini de
kaydeder ve **yapılandırılamaz** — `A2AServerRegistrationOptions` yalnızca `AgentRunMode` ve
`ServerOptions` içerir, kart alanı yoktur. Ölçüldü:

```
GET /a2a/order/card  →  200
{"name":"A2A Agent","description":"","version":"","supportedInterfaces":[],"skills":[]}
```

**Neden engelleyici değil:** A2A'nın kanonik keşif yolu `/.well-known/agent-card.json`'dır ve
SDK istemcisi de oraya bakar — `A2ACardResolver`'ın varsayılan `agentCardPath` değeri
`"/.well-known/agent-card.json"`. Bizim kartımız orada **eksiksiz** yayınlanıyor.

> ⚠️ Partner entegrasyonlarında keşif için **`.well-known` yolu** kullanılmalıdır. `/card`
> yolunu okuyan bir istemci boş kart alır ve `supportedInterfaces` boş olduğu için hangi
> transport'a bağlanacağını çözemez. Köprünün sonraki sürümlerinde kart yapılandırması
> gelirse burası güncellenmeli.

Aynı ölçümden çıkan diğer sonuçlar:

| Endpoint | Sonuç | Yorum |
|---|---|---|
| `GET tasks/{id}/pushNotificationConfigs` | `400 "Push notifications not supported."` | Kart bayrağı **fiilen uygulanıyor** — SSRF yüzeyi yok |
| `GET extendedAgentCard` | `500` | Desteklenmiyor (kartta `extendedAgentCard: false`) |

Yani **desteklenmediğini ilan ettiğimiz yetenekler gerçekten kapalı**; ilan yalnızca belge
değil, çalışan bir kısıt. `AgentCard_DeclaresCapabilities_Explicitly` ve
`PushNotificationConfig_IsRejected_NotAcceptedAsWebhook` bunu kilitler.

> ⚠️ **Düzeltme — bu tabloda daha önce yanlış bir satır vardı.** `POST message:stream` için
> `500 · desteklenmiyor` yazıyordu. O ölçüm **0.2 biçimli gövdeyle** (`role:"user"`,
> `kind:"text"`) yapılmıştı; 500'ün sebebi streaming'in yokluğu değil, gövdenin
> ayrıştırılamamasıydı. Doğru **1.0 proto adlandırmasıyla** yeniden ölçüldü:
>
> | Çağrı | Sonuç |
> |---|---|
> | JSON-RPC `SendStreamingMessage` | `200` · 45 SSE olayı |
> | HTTP+JSON `POST message:stream` | `200` · 53 SSE olayı |
>
> Her iki yol da aynı olguyu (doğru müşterinin gerçek sipariş verisini) döndürüyor. Kart bu
> yüzden artık **`streaming: true`** ilan ediyor — çalışan bir yeteneği `false` ilan etmek,
> keşifle bulunamayan ama çağrılabilen bir yüzey bırakırdı.
> `JsonRpcStreaming_Works_AsCardDeclares` ve `HttpJsonStreaming_Works_AsCardDeclares` ilanı
> davranışa bağlar.
>
> Bu satırın uzun süre yanlış kalması ayrıca gerçek bir hatayı gizledi: streaming yolunda
> ambient müşteri kimliği hiç kurulmuyordu (bkz. bölüm 4).

### `SupportedInterfaces` boş bırakılamaz

Kart, gerçekten yayınlanan transport'ları **sırayla** ilan eder (ilk giren tercih edilir).
Liste boş olsaydı istemci hangi transport'u deneyeceğini bilemez, kendi varsayılanına düşer ve
o yol yayınlanmamışsa bağlantı sessizce başarısız olurdu.

Ürün ajanı yalnızca katalog sorgular, müşteri kimliği gerektirmez. Sipariş ve şikayet
ajanları müşteri verisi döndürdüğü için **özne token'ı** ister — partner token'ıyla
çağrılamazlar.

**Rate limit bölümlemesi IP'ye değil PARTNER'a göredir.** Dış sistemler proxy/bulut çıkışı
arkasında IP paylaşabilir (bir partnerin trafiği diğerinin sınırını tüketirdi) ya da IP
değiştirebilir (sınır fiilen ortadan kalkardı). Partner, özne token'ının kimliğinden çıkarılır
(`A2ASubjectIdentity`) — böylece bir partner çok sayıda müşteri adına çağrı yaparak servisi tek
başına tüketemez.

> Biçim `A2ASubjectIdentity`'de **tek yerde** tanımlıdır. İki yerde ayrı yazılsaydı, biri
> değiştiğinde rate limit sessizce yanlış anahtara bölümler ve koruma **görünür bir arıza
> olmadan** kaybolurdu.

## 6. AgentCard — keşif

### Keşif sözleşmesi (partner entegrasyonu için)

Kart yolu **ajan başınadır**, çünkü tek host'ta üç ajan yayınlanıyor:

```
GET /a2a/product/.well-known/agent-card.json
GET /a2a/order/.well-known/agent-card.json
GET /a2a/complaint/.well-known/agent-card.json
```

> ⚠️ **SDK çözücüsünün varsayılanı bu yolları BULAMAZ.** Ölçüldü: `A2ACardResolver`
> `new Uri(base, path)` standart göreli çözümünü uygular; taban `/a2a/order` iken son segmenti
> değiştirip `/a2a/.well-known/agent-card.json` ister ve **404** alır. Doğru kullanım taban =
> kök, yol = tam:
>
> ```csharp
> new A2ACardResolver(new Uri("https://api.ornek.com"), http,
>                     "/a2a/order/.well-known/agent-card.json")
> ```
>
> Kök (`/.well-known/agent-card.json`) **bilerek yayınlanmıyor**: orada tek bir kart olabilir ve
> hangi ajanın konulacağı keyfî bir seçim olurdu — `order` bekleyen bir istemciye `product`
> kartı vermek, keşfi düzeltmek yerine yanıltmak olurdu. Partner tam yolu bilmelidir.

### Kimlik kartta ilan edilir

Kart `securitySchemes` + `securityRequirements` taşır (HTTP bearer / JWT). Bu **isteğe bağlı
bir süs değil**: kartın var olma sebebi, çağıranın yetenekleri VE nasıl kimlik doğrulayacağını
*denemeden* öğrenmesidir. İlan edilmezse istemci token'sız çağırır, 401 alır ve gereksinimi
ancak deneme-yanılmayla keşfeder.

Şema açıklaması ajana göre farklıdır ve gerçek kısıtı söyler — sipariş/şikayet kartlarında
*"Partner token'ı bu ajan için yeterli DEĞİLDİR"* yazar.

Kartlar **kimlik doğrulaması istemez**: keşif public'tir, yetenek *kullanımı* değil. Çağıran
hangi yeteneklerin olduğunu ve hangi kimliğin gerektiğini deneyerek değil kartı okuyarak öğrenir.

- Sipariş kartı kapsamı açıkça ilan eder: *"SALT-OKUNUR … sipariş oluşturma/iptal/iade bu kanalda YOKTUR"*
- `Streaming` **`true`** — her iki transport'ta da ölçülerek doğrulandı (yukarıdaki düzeltme kutusu)
- `PushNotifications` ve `ExtendedAgentCard` **açıkça `false`** — varsayılana (`null`) bırakmak
  "bilinmiyor" demektir ve çağıranı denemeye teşvik ederdi. Push notification ayrıca çağıranın
  verdiği adrese sunucudan istek gitmesi (SSRF) anlamına geleceği için kapalı **kalmalıdır**
- `AgentRunMode.DisallowBackground`: arka plan görevleri dış çağıranın sunucuda iş
  biriktirmesine izin verirdi; her çağrı istek ömrü içinde başlar ve biter. Streaming bunu
  **ihlal etmez** — SSE yanıtı da aynı istek ömrü içinde üretilip biter

## 7. Ajanlar ve tool'ları

| Ajan | Salt-okunur tool'lar | Kimlik |
|---|---|---|
| Ürün | `product_inquiry_tool`, `product_list_tool` | gerekmez |
| Sipariş | `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool` | **özne token'ı** |
| Şikayet | `complaint_status_tool`, `get_all_complaints_tool` | **özne token'ı** |

> Adların tamamı `_tool` ile biter (`WellKnown.ToolNames`) — tool adları LLM'e bu hâliyle
> gider, kısaltılmış biçim hiçbir yerde geçerli değildir.

### Şikayet tool'ları sonradan yazıldı

Başlangıçta salt-okunur bir şikayet ajanının **çağıracağı hiçbir tool yoktu**:
`IComplaintToolsService` yalnızca `ComplaintRegistrationTool` (yazma) içeriyordu ve şikayet
verisi tool'la değil, `CustomerContextProvider` üzerinden bağlama enjekte edilerek geliyordu.

`complaint_status_tool` ve `get_all_complaints_tool` bu iş kapsamında eklendi. Sahiplik
kontrolü **sipariş tarafındaki desenin birebir aynısıdır**:

```csharp
// Sahiplik ihlali "bulunamadı" ile AYNI metni döner
private static string ComplaintNotAccessibleMessage(string complaintId) =>
    $"'{complaintId}' numaralı şikayet bulunamadı.";
```

> ⚠️ **Enumeration koruması:** "var ama senin değil" ile "hiç yok" durumları dışarıdan
> **ayırt edilemez** — ayrı metinler dönseydi numara taranarak hangi şikayetlerin var olduğu ve
> dolaylı olarak başka müşterilerin şikayet hacmi öğrenilebilirdi. Hata **kodu** farklıdır
> (`CUSTOMER_ID_MISMATCH` vs `COMPLAINT_NOT_FOUND`), böylece trace/admin panelinde gerçek sebep
> görünür ve sızıntı denemeleri "bulunamadı" gürültüsünde kaybolmaz.
>
> `ComplaintReadOnlyToolsTests` her iki ucu da kilitler: metinlerin **aynı**, kodların **farklı**
> olduğunu ayrı ayrı doğrular.

## 8. Yapılandırma

```json
"A2A": {
  "Enabled": false,
  "SubjectTokenMinutes": 5,
  "RequestsPerMinute": 60,
  "PublicBaseUrl": "",
  "Partners": [
    { "PartnerId": "acme", "AllowedCustomerIds": ["1027", "1044"] }
  ]
}
```

> **`PartnerId` = partner kullanıcısının KULLANICI ADI**, veritabanı satırının GUID'i değil.
> Token değişimi, çağıranın token'ındaki kullanıcı adı claim'ini (`name` / `unique_name`)
> buradaki değerle karşılaştırır. GUID okunsaydı her yeniden seed'de değişir ve yapılandırma
> kırılırdı; ayrıca hız-sınırı anahtarında okunamaz bir değer görünürdü.

`AllowedCustomerIds` içinde `"*"` tüm müşteriler demektir — yalnızca gerçekten güvenilen,
sözleşmeli bir sistem için kullanılmalı; **tek bir sızan token tüm müşterileri açar**.

`PublicBaseUrl` doldurulmazsa kart içindeki binding URL'leri **göreli** kalır (`/a2a/order`).
Göreli URL, kartı okuyan dış istemcinin adresi kendi başına çözmesini gerektirir ve
proxy/gateway arkasında çoğu zaman yanlış sonuç verir — **dışa açılan bir kurulumda
doldurulmalıdır** (ör. `https://api.ornek.com`).

Kanal kapalıyken ajanlar hiç kaydedilmez ve endpoint hiç map edilmez: kapalı bir kanalın
yayında olmaması, yetkiyle engellenmesinden güvenlidir — yanlış yapılandırma yüzeyi hiç doğmaz.

### Development ortamı — yerel deneme ayarı

`appsettings.json` içindeki A2A **kapalıdır ve öyle kalmalıdır**; kanalı yalnızca kendi
makinende açarsın. `src/CustomerSupportBot.Api/appsettings.Development.json` dosyası
`.gitignore`'da olduğu için bu blok depoya girmez — makineye özeldir ve her geliştiricinin
kendi elinde oluşturması gerekir:

```json
"A2A": {
  "Enabled": true,
  "SubjectTokenMinutes": 5,
  "RequestsPerMinute": 60,
  "PublicBaseUrl": "http://localhost:5021",
  "Partners": [
    { "PartnerId": "demo-partner", "AllowedCustomerIds": ["1027"] }
  ]
}
```

`PartnerId`, `DemoDataSeeder.SeedA2APartnerAsync`'in ürettiği hesabın **kullanıcı adıyla**
(`demo-partner`) birebir aynı olmalı; seed yalnızca `A2A:Enabled=true` iken çalışır, yani
bu blok olmadan partner hesabı hiç oluşmaz. `AllowedCustomerIds` içindeki `1027`, demo
verisindeki bir müşteridir — başka bir numara verilirse token değişimi doğru şekilde 403 döner.

Hesap bilgileri `A2A:DevPartnerUsername` ve `A2A:DevPartnerPassword` ile değiştirilebilir
(varsayılanlar `demo-partner` / `Partner123!`). Kullanıcı adını değiştirirsen `PartnerId`'yi de
**birlikte** değiştirmek zorundasın: eşleşme kullanıcı adı üzerinden kurulur, ikisi ayrışırsa
giriş başarılı olur ama token değişimi 403 döner.

Uygulamayı bu ayarla ayağa kaldırmak için:

```bash
cd src/CustomerSupportBot.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5021 dotnet run --no-launch-profile
```

`--no-launch-profile` kullanıldığında `ASPNETCORE_ENVIRONMENT` **elle verilmelidir**; aksi
halde ortam Production olur, `appsettings.Development.json` hiç okunmaz ve uygulama boş
bağlantı dizesiyle açılmayı deneyip başarısız olur.

> **Testlere etkisi:** entegrasyon testleri de Development ortamında koştuğu için bu dosyayı
> okur. A2A'nın *kapalı* olmasını doğrulayan test (`A2ADisabledByDefaultTests`) bu yüzden
> ayarı kendi factory'sinde açıkça `false`'a sabitler — kapalılığı ortama bıraksaydı, blok
> ekli bir makinede kırmızı, CI'da yeşil olurdu.

## 9. Örnek istemci (konsol)

`samples/CustomerSupportBot.A2AClient.Sample` — bir partnerin yazacağı entegrasyonun
referansı. Bizim hiçbir projemize referans **vermez**, yalnızca herkese açık NuGet
paketlerini kullanır; böylece "dışarıdan bakan biri gerçekten bağlanabiliyor mu" sorusu
dürüstçe ölçülür. Paylaşılan tiplerle çalışsaydı entegrasyonu değil kendi iç
tutarlılığımızı test ederdik.

### Mimari: A2A-as-a-Tool

İstemcinin **kendi yerel ajanı** vardır. Üç uzak A2A ajanı şu zincirle birer *tool*'a
dönüşür ve o ajana verilir:

```
A2ACardResolver → AgentCard → card.AsAIAgent(httpClient, options) → AsAIFunction()
                                                                      ↓
                                            yerel ajanın Tools listesi
```

Önemi: **hangi soruyu hangi ajana soracağına kod değil LLM karar verir.** Kodda
sabitlenseydi "Çay fiyatı nedir, bir de son siparişim ne durumda?" gibi tek cümlede iki
ajanı birden gerektiren bir istek karşılanamazdı — ölçüldü, tek turda ürün ve sipariş
ajanlarının ikisi de çağrılıyor ve sonuç tek cevapta birleşiyor.

Tool adı ve açıklaması `AsAIFunction()` sayesinde **AgentCard'dan** gelir; sunucudaki
açıklama değişince istemcideki tool açıklaması kendiliğinden güncellenir. İki yerde ayrı
yazılsaydı sessizce ayrışırlardı.

Yerel ajanın **yönlendirme talimatı** da aynı ilkeye tabidir: `SupportAgentFactory`
bu metni kartların `skills` listesinden (her skill'in adı, açıklaması, ilk örneği) üretir,
elle yazmaz. Önceki hâlde üç sabit satırdı ("ProductInfoAgent : ürün kataloğu — fiyat,
stok, kategori.") — kartlar bundan çok daha zengin (her skill kendi örneğini taşıyor) ve
sunucu tarafında bir skill değişirse elle yazılan özet sessizce eskirdi. Ölçüldü: "İçecekler
kategorisinde neler var?" sorusu, kartın `product-list` skill'inin örneğine yakın olduğu
için doğru ajana yönlendi — bu skill eski üç satırlık özette hiç geçmiyordu.

> ⚠️ `AsAIAgent`'a `httpClient` **geçilmek zorundadır**. Geçilmezse kendi iç istemcisini
> kurar, token handler'ımız o boru hattına eklenmez ve **kart keşfi başarılı olduğu hâlde**
> mesaj gönderimi 401 alır — yani hata sebebinden uzakta patlar.

### İki mod

```bash
# Etkileşimli ajan (varsayılan) — siz yazarsınız, ajan yönlendirir
dotnet run --project samples/CustomerSupportBot.A2AClient.Sample

# LLM'siz uçtan uca doğrulama — CI için çıkış kodu döndürür
dotnet run --project samples/CustomerSupportBot.A2AClient.Sample -- --verify
```

`--verify` modu korunmuştur çünkü farklı bir iş yapar: LLM'e hiç bağlı olmadan zincirin
bütün halkalarını (giriş → token değişimi → kart keşfi → gerçek A2A çağrısı → yetki sınırı)
tek komutta ölçer. Partner kimliği hatasını yakalayan da tam olarak bu moddu.

Negatif kontrol yalnızca **401/403**'ü kabul eder. Önceden `catch (Exception)` idi ve
ölçüldü: var olmayan bir ajana istek atıldığında da "beklendiği gibi reddedildi" deyip 0
ile çıkıyordu — yani gerçek bir yetki sınırıyla "sunucu bozuk" durumunu ayırt edemiyordu.
Gerçek red 403, bozukluk 404 döndürdüğü için ayrım yapılabilir, dolayısıyla yapılır.

### Token yenileme

Özne token'ı kısa ömürlüdür (`A2A:SubjectTokenMinutes`, varsayılan 5 dakika). Etkileşimli
bir oturumda kullanıcı bundan uzun konuşur; token bir kez alınıp saklansaydı sohbetin
ortasında çağrılar 401'e düşer ve bu, ajanın *"cevap veremiyorum"* demesi olarak görünürdü —
altyapı eksiği **yanlış olguya** dönüşürdü. `A2ATokenProvider` süre dolmadan yeniler.

İki ayrı `HttpClient` boru hattı vardır (`a2a-partner`, `a2a-subject`): ürün ajanı **partner**,
sipariş/şikayet ajanları **özne** token'ı ister; tek istemci ikisine birden hizmet edemez.

### Yapılandırma

`appsettings.json` sunucu adresi, partner adı ve modeli taşır. Anahtar
`appsettings.Development.json`'a (gitignore'lu) ya da user-secrets'a yazılır:

```bash
dotnet user-secrets set "AI:OpenAI:ApiKey" "sk-..."   # veya: export OPENAI_API_KEY=sk-...
```

> İstemci `ContentRootPath`'i açıkça kendi çıktı dizinine sabitler. Varsayılan, süreci
> başlattığın dizindir; depo kökünden `dotnet run --project …` ile çalıştırıldığında
> yapılandırma **hiç okunmazdı** ve bu "anahtarı yazdım ama görmüyor" şeklinde ortaya çıkardı.

### Ön koşullar

Bölüm 8'deki Development bloğu ve seed edilen `demo-partner` / `Partner123!` hesabı.
Etkileşimli mod ayrıca bir model anahtarı ister; `--verify` istemez.

### İzlenebilirlik: OpenTelemetry + correlation.id

İstemci, sunucudaki `Adapters.Telemetry` desenini (`TelemetryAdapterServiceCollectionExtensions.cs`)
aynı paket sürümüyle (1.17.0) yansıtır — `Telemetry:Enabled` (varsayılan **kapalı**), `Telemetry:Otlp:Endpoint`.
Endpoint boşsa açıldığında konsola span basar; bir collector'ınız varsa oraya gönderir.

```bash
dotnet run --project samples/CustomerSupportBot.A2AClient.Sample -- --verify
# yapılandırmada Telemetry:Enabled=true iken:
```

Her tur/koşu kendi `correlation.id`'sini alır ve konsola basılır (`[correlation.id=...]`).
Bu süslü bir etiket değil — `AddHttpClientInstrumentation` açıkken giden her A2A çağrısına
**W3C `traceparent`** olarak eklenir; sunucu da `AddAspNetCoreInstrumentation` ile bunu okur
(`Telemetry:Enabled`, sunucu tarafında varsayılan **açık**). Ölçülen hiyerarşi:

```
chat-turn (correlation.id, customer.id)
  └─ invoke_agent partner-support-agent   (gen_ai.* alanları, LLM çağrısı dahil)
       └─ POST /a2a/order, GET .well-known/agent-card.json, ...   (aynı TraceId)
```

13 span, tek `TraceId` altında — kök aktivite, LLM çağrısı ve üç uzak ajana giden HTTP
istekleri hepsi aynı izde. Bu istemci yazılana kadar A2A entegrasyonunda gözlemlenebilirlik
hiç yoktu (sunucuda vardı, istemcide yoktu); en büyük risk sessizce hiç çalışmamaktı — ki
tam olarak başına geldi: `AddOpenTelemetry` (Hosting entegrasyonu) `TracerProvider`'ı bir
`IHostedService` üzerinden kurar, `host.StartAsync()` çağrılmadan o servis **hiç başlamaz**.
İlk denemede `Telemetry:Enabled=true` olduğu hâlde tek bir span basılmadı — ölçülerek
bulundu, `Program.cs`'e `await host.StartAsync()` eklenerek düzeltildi.

## 10. Başka dillerden entegrasyon

A2A'nın amacı zaten budur: karşı taraf hangi dilde yazılmış olursa olsun ajanla konuşabilsin.
Bu bölümdeki her istek/yanıt **çalışan sunucudan ölçülerek** alınmıştır, elle yazılmamıştır.

### Önce sürüm: hangi A2A lehçesini konuşuyoruz

Sunucumuz **A2A 1.0** konuşur (kart `supportedInterfaces[].protocolVersion: "1.0"` ilan eder).
1.0 ile birlikte JSON adlandırması protobuf şemasından türetilir ve **eski sürümlerden farklıdır**:

| | A2A 1.0 (bizim konuştuğumuz) | A2A 0.2 / 0.3 (eski) |
|---|---|---|
| Metot adı | `SendMessage` | `message/send` |
| Rol değeri | `ROLE_USER` / `ROLE_AGENT` | `user` / `agent` |
| Metin parçası | `{"text": "..."}` | `{"kind": "text", "text": "..."}` |

> ⚠️ **En sık yapılacak hata bu.** İnternetteki A2A örneklerinin çoğu hâlâ 0.2/0.3 biçimindedir.
> O biçimde gönderirseniz istek **çalışmaz** ve hata mesajı sebebi doğrudan söylemez. Ölçüldü:
>
> | Gönderilen | Sonuç |
> |---|---|
> | `"method": "message/send"` | `-32601` · *'method' field is not a valid A2A method* |
> | `"role": "user"` (küçük harf) | `-32602` · *could not be deserialized as SendMessageRequest* |
> | `{"kind":"text", ...}` parça | `-32602` · aynı mesaj |
> | 0.2 biçimi, HTTP+JSON yolunda | **HTTP 500** (temiz 400 değil — köprü kaynaklı) |
>
> Yani hata mesajı "sürümün yanlış" demez; alan adı yanlışmış gibi görünür. Sürüm uyuşmazlığından
> şüphelenmek ilk refleks olmalıdır.

### Alan kuralları (ölçüldü)

| Alan | Kural |
|---|---|
| `method` | Tam olarak `SendMessage` |
| `message.role` | Tam olarak `ROLE_USER` — küçük harf **veya eksik** olursa `-32602` |
| `message.messageId` | **Zorunlu** — eksikse `-32602` |
| `message.parts[].text` | Metin doğrudan; `kind` alanı **yoktur** |

Doğrulama gevşek değildir: eksik/yanlış alan sessizce yok sayılmaz, istek reddedilir.

### Adım 1-2: iki token (A2A'ya ait değil, bizim uçlarımız)

A2A spesifikasyonu token *dağıtımını* tanımlamaz; kart yalnızca "bearer gerekli" diye ilan eder.
Bu iki çağrı düz HTTP'dir ve her dilde standart bir HTTP istemcisiyle yapılır:

```bash
# 1) Partner girişi
curl -s -X POST http://localhost:5021/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"demo-partner","password":"Partner123!"}'
# -> {"accessToken":"eyJ...", ...}

# 2) Özne token'ı (tek müşteriye kilitli)
curl -s -X POST http://localhost:5021/auth/a2a/token-exchange \
  -H "Authorization: Bearer $PARTNER_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"customerId":"1027"}'
# -> {"accessToken":"eyJ...","expiresAt":"...","customerId":"1027","tokenType":"Bearer"}
```

> Hangi ucun hangi token'ı istediği **asimetriktir** ve şaşırtır: sipariş/şikayet **özne**
> token'ı ister, ürün ise **partner** token'ı. Özne token'ıyla ürün ajanını çağırmak da 403
> döner. Bölüm 5'teki tablo bağlayıcıdır.

### Adım 3: kart keşfi

```bash
curl -s http://localhost:5021/a2a/order/.well-known/agent-card.json
```

Bağlanılacak URL ve binding buradan okunur — elle sabitlenmemelidir:

```json
"supportedInterfaces": [
  { "url": "http://localhost:5021/a2a/order", "protocolBinding": "JSONRPC",   "protocolVersion": "1.0" },
  { "url": "http://localhost:5021/a2a/order", "protocolBinding": "HTTP+JSON", "protocolVersion": "1.0" }
]
```

### Adım 4: ajanı çağır — JSON-RPC

```bash
curl -s -X POST http://localhost:5021/a2a/order \
  -H "Authorization: Bearer $SUBJECT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{
    "jsonrpc": "2.0",
    "id": "1",
    "method": "SendMessage",
    "params": { "message": {
      "role": "ROLE_USER",
      "messageId": "m1",
      "parts": [ { "text": "Son siparişim ne durumda?" } ]
    }}
  }'
```

Gerçek yanıt (kısaltılmadan, ölçüldü):

```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "message": {
      "role": "ROLE_AGENT",
      "parts": [ { "text": "Son siparişiniz: 1057  \nDurum: İşleniyor  \nTarih: 4/22/2006 12:00 AM" } ],
      "messageId": "chatcmpl-...",
      "contextId": "d0c6fbbf8f9a4d40a8913242dab39a97"
    }
  }
}
```

### Adım 4 (alternatif): HTTP+JSON

Aynı işi JSON-RPC zarfı olmadan yapar; yol `:send` son ekiyle biter:

```bash
curl -s -X POST http://localhost:5021/a2a/order/message:send \
  -H "Authorization: Bearer $SUBJECT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"message":{"role":"ROLE_USER","messageId":"h1","parts":[{"text":"Merhaba"}]}}'
# -> {"message":{"role":"ROLE_AGENT","parts":[{"text":"..."}],"messageId":"...","contextId":"..."}}
```

### Adım 4 (akış): parça parça yanıt

Kart `streaming: true` ilan eder ve **her iki transport** da destekler. Yanıt SSE'dir
(`text/event-stream`): her satır `data:` ile başlar, gövdesi akışsız yanıtla aynı şekli taşır.
Tek bir akış yanıtı içindeki `parts[].text` alanları **birleştirildiğinde** o çağrının tam
metnini verir.

> Bunu **ayrı** bir akışsız çağrının metniyle karşılaştırmayın: her çağrı yeni bir LLM
> üretimidir, aynı soruya farklı ifadeyle cevap verir. Ölçüldü — aynı soru için akış
> `"Sipariş No: 1057, durum: İşleniyor. Tarih: …"`, akışsız `"Sipariş No: 1057\nDurum: İşleniyor\n…"`
> döndü: aynı olgu, farklı cümle. Bu bir tutarsızlık değil, LLM'in doğası. Bayt düzeyinde
> eşitlik bekleyen bir test yazarsanız rastgele kırmızıya döner.

```bash
# JSON-RPC — metot adı farklı: SendStreamingMessage
curl -sN -X POST http://localhost:5021/a2a/order \
  -H "Authorization: Bearer $SUBJECT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":"1","method":"SendStreamingMessage",
       "params":{"message":{"role":"ROLE_USER","messageId":"s1",
                            "parts":[{"text":"Son siparişim ne durumda?"}]}}}'

# HTTP+JSON — yol soneki farklı: :stream
curl -sN -X POST http://localhost:5021/a2a/order/message:stream \
  -H "Authorization: Bearer $SUBJECT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"message":{"role":"ROLE_USER","messageId":"hs1",
                  "parts":[{"text":"Son siparişim ne durumda?"}]}}'
```

`curl`'de **`-N`** (buffering kapalı) şarttır; yoksa parçalar birikip tek seferde görünür ve
akış çalışmıyor sanılır.

> ⚠️ Akışta da sürüm tuzağı aynıdır: gövdeyi 0.2 biçiminde (`role:"user"`, `kind:"text"`)
> gönderirseniz **500** alırsınız. Bu belge uzun süre o 500'ü "streaming desteklenmiyor" diye
> kaydetmişti — hata gövdedeydi, sunucuda değil.

### SDK'sız tam örnek (Python, yalnızca standart kütüphane)

Hiçbir A2A SDK'sı olmayan diller için de yeterlidir — protokol düz HTTP + JSON'dur:

```python
import json, urllib.request

BASE = "http://localhost:5021"

def post(path, body, token=None):
    req = urllib.request.Request(
        BASE + path, method="POST",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json",
                 **({"Authorization": f"Bearer {token}"} if token else {})})
    with urllib.request.urlopen(req) as r:
        return json.load(r)

partner = post("/auth/login", {"username": "demo-partner", "password": "Partner123!"})["accessToken"]
subject = post("/auth/a2a/token-exchange", {"customerId": "1027"}, partner)["accessToken"]

resp = post("/a2a/order", {
    "jsonrpc": "2.0", "id": "1", "method": "SendMessage",
    "params": {"message": {"role": "ROLE_USER", "messageId": "m1",
                           "parts": [{"text": "Son siparişim ne durumda?"}]}}
}, subject)

print("".join(p.get("text", "") for p in resp["result"]["message"]["parts"]))
```

### Resmî SDK'lar

A2A, Linux Foundation altında açık kaynak bir protokoldür ve `a2aproject` altında birden çok
resmî SDK yayınlar:

| Dil | Paket | Depo |
|---|---|---|
| .NET | `dotnet add package A2A` | [a2a-dotnet](https://github.com/a2aproject/a2a-dotnet) |
| Python | `pip install a2a-sdk` | [a2a-python](https://github.com/a2aproject/a2a-python) |
| JavaScript / TS | `npm install @a2a-js/sdk` | [a2a-js](https://github.com/a2aproject/a2a-js) |
| Java | Maven | [a2a-java](https://github.com/a2aproject/a2a-java) |
| Go | `go get github.com/a2aproject/a2a-go` | [a2a-go](https://github.com/a2aproject/a2a-go) |
| Rust | Cargo | [a2a-rs](https://github.com/a2aproject/a2a-rs) |

> ⚠️ **Dürüst sınır:** bu kurulumda uçtan uca **yalnızca .NET SDK'sı çalıştırıldı** (bölüm 9).
> Diğer SDK'lar burada denenmedi; her biri kendi sürüm takvimiyle ilerlediği için, bir SDK
> henüz 0.2/0.3 adlandırmasında ise yukarıdaki tablonun ilk satırındaki hatayı verir. Yeni bir
> dille entegrasyona başlarken **önce SDK'sız curl** ile bağlanıp sözleşmeyi doğrulamak, sonra
> SDK'ya geçmek en kısa yoldur — böylece hatanın SDK'dan mı sunucudan mı geldiği karışmaz.

### Hata karşılıkları (ölçüldü)

| Durum | Yanıt |
|---|---|
| Token yok | `401` |
| Partner token'ı ile sipariş/şikayet ajanı | `403` |
| Özne token'ı ile ürün ajanı | `403` |
| Yetkisiz müşteri için token değişimi | `403` |
| Dakikadaki istek sınırı aşıldı | `429` (`A2A:RequestsPerMinute`, partner başına) |
| Yanlış sürüm biçimi | `-32601` / `-32602` (JSON-RPC) · `500` (HTTP+JSON) |

`403`'ün sebebi **bilerek açıklanmaz**: "müşteri yok" ile "yetkin yok" ayrımı dışarıdan
görülseydi müşteri numarası taranabilirdi (bkz. bölüm 3).

## Bağlantılar

- `Adapters.Agents/A2A/A2AAgentCatalog.cs` — ajanlar ve salt-okunur bariyeri
- `Application/Services/A2A/` — token değişimi, yetkilendirici, kimlik biçimi
- `Api/Extensions/A2AServicesExtensions.cs` — **servis kaydı** (`AddA2AAgents`)
- `Api/Endpoints/A2AEndpoints.cs` — **endpoint yayınlama** (`MapA2AAgentEndpoints`), scope filtresi, AgentCard

> Servis kaydı ile endpoint yayınlama **ayrı dosyalardadır** — repo konvansiyonu budur
> (`AuthServicesExtensions`, `ApplicationServicesExtensions` → `Extensions/`; mapping → `Endpoints/`).
- `Api/Endpoints/A2AAuthEndpoints.cs` — token değişimi ucu
- `samples/CustomerSupportBot.A2AClient.Sample/` — referans istemci (bkz. bölüm 9)
- Testler:
  - `A2AAgentCatalogTests` — salt-okunur bariyeri
  - `A2ATokenExchangeTests` — token değişimi iş kuralı
  - `ComplaintReadOnlyToolsTests` — sahiplik + enumeration koruması
  - `A2AEndpointsTests` — yetki/transport zinciri
  - `A2ADisabledByDefaultTests` — kanal kapalıyken hiç yayınlanmaması
  - `A2AProtocolConformanceTests` — protokolün kendi istemcisiyle uçtan uca uyum
