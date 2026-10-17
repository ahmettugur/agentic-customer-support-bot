# MCP sunucusu (Model Context Protocol)

Uygulamanın salt-okunur iş araçlarını MCP ile dış sistemlere açar: MCP destekleyen bir ajan ya da IDE (Claude Desktop,
Copilot, başka bir ajan çerçevesi) ürün kataloğunu ve — yetkili bir müşteri adına — sipariş/şikayet durumunu sorgulayabilir.
Araya LLM girmez: araç doğrudan çalışır, maliyet ve prompt-injection yüzeyi yoktur.

**Varsayılan KAPALI.** Kapalıyken endpoint'ler hiç map edilmez (404). Açmak için `Mcp:Enabled=true` **ve** `A2A:Enabled=true`
(MCP, A2A'nın Partner/özne token altyapısını kullanır; A2A kapalıyken MCP açılırsa uygulama açılışta hata verir).

## Endpoint'ler (Streamable HTTP, stateless)

| Endpoint | Token | Araçlar |
|---|---|---|
| `/mcp/catalog` | **Partner** | `product_inquiry_tool`, `product_list_tool` |
| `/mcp/customer` | **A2ASubject** (tek müşteriye kilitli) | `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool`, `complaint_status_tool`, `get_all_complaints_tool` |

"Hangi sistem" (Partner) ile "hangi müşteri" (özne token'ı) ayrı token'larda kalır — A2A ile birebir aynı ayrım. Partner token'ı
`/mcp/customer`'a **giremez** (403); özne token'ı `/mcp/catalog`'a giremez.

## Kanal yetkisi (partner başına)

Token "geçerli bir partner" demektir; **hangi kanala girebileceği** partnerin yapılandırmasındadır ve her istekte okunur
(token'a yazılmaz): erişim yapılandırma değişince token süresi beklenmeden kesilir, çalınmış bir token kendi kanalını
genişletemez.

```json
"A2A": {
  "Partners": [
    { "PartnerId": "acme",  "AllowedCustomerIds": ["1027"] },                         // yalnızca A2A (varsayılan)
    { "PartnerId": "ide-x", "AllowedCustomerIds": ["*"], "Channels": ["mcp"] },       // yalnızca MCP
    { "PartnerId": "bot-y", "AllowedCustomerIds": ["1027"], "Channels": ["a2a", "mcp"] }
  ]
}
```

- `Channels` belirtilmezse **yalnızca `a2a`**: var olan partnerler MCP'ye kendiliğinden girmez.
- `A2A:Partners`'ta hiç tanımlı olmayan `Partner` rollü bir hesap eskiden olduğu gibi A2A **ürün ajanını** kullanır (geriye dönük
  uyum; müşteri verisi için özne token'ı gerekir ve onu yalnızca tanımlı partner alabilir). MCP'ye ise **giremez**.
- Kanalı olmayan (tanımsız) partner token değişimi de yapamaz. Bir partneri tamamen kapatmak için listeden çıkarın.
- İhlal, dışarıya sessiz 403 döner; sebep (`[mcp] REDDEDİLDİ · partner=… · sebep=…`) günlüğe yazılır.
- Hız sınırı kanal başına ayrı kovadır (`a2a` ve `mcp`, aynı `A2A:RequestsPerMinute` değeri): biri diğerinin kotasını tüketmez.

> Yapılandırma notu: `Channels` bilerek null başlar. .NET yapılandırma bağlayıcısı dolu bir `List<string>` varsayılanına
> yapılandırmadaki değerleri *ekler*; varsayılan `["a2a"]` olsaydı `["mcp"]` yazan partner sessizce `a2a` da alırdı
> (entegrasyon testi bunu ölçer).

## Bağlanma

1. Partner olarak giriş yapın (`POST /auth/login`, `Partner` rolündeki hesap) → `accessToken`.
2. Katalog için o token'la doğrudan `/mcp/catalog`'a bağlanın.
3. Müşteri verisi için özne token'ı alın: `POST /auth/a2a/token-exchange` `{ "customerId": "1027" }` (Partner token'ıyla).
   Partner yalnızca `A2A:Partners:*:AllowedCustomerIds` içindeki müşteriler için token alabilir. Dönen token'la `/mcp/customer`'a bağlanın.

Örnek istemci ayarı (Claude Desktop tarzı, uzak HTTP sunucusu):

```json
{
  "mcpServers": {
    "destek-katalog": {
      "type": "http",
      "url": "https://api.ornek.com/mcp/catalog",
      "headers": { "Authorization": "Bearer <partner-token>" }
    }
  }
}
```

Özne token'ları kısa ömürlüdür (`A2A:SubjectTokenMinutes`); uzun süreli istemciler yenilemeyi kendileri yapmalıdır.

## Güvenlik modeli

- **Yazma yok, yapısal olarak.** Kümede sipariş verme/iptal/iade/şikayet kaydı **bulunmaz** (bir `if` değil, araç yoktur).
  `A2AAgentCatalog.ReadOnlyOnly`, kümeye bir yazma aracı eklenirse katalog kurulurken hata verir (katalog tembel kurulur: ilk MCP isteğinde;
  `McpToolCatalogTests` aynı kuralı derleme/test aşamasında da yakalar). Tüm araçlar MCP'de
  `readOnlyHint=true` olarak işaretlidir.
- **Müşteri kimliği token'dan gelir**, araç argümanından değil: araçların müşteri parametresi yoktur; kimlik istek kapsamında
  ambient bağlama kurulur. Eşzamanlı iki müşterinin kimliği karışmadığı testle doğrulanır (kapsam kaldırılınca testler düşer).
- **Stateless:** oturum yoktur, dolayısıyla ele geçirilebilecek/paylaşılabilecek bir oturum kimliği de yoktur.
- Hız sınırı partner başına, MCP için ayrı kova (A2A ile aynı limit); gövde üst sınırı `Mcp:MaxRequestBytes` (varsayılan 16 KB).
- Bilinmeyen bir yol **boş** araç kümesi alır (yanlış yapılandırma veri açmaz).

## Kapsam dışı

- **MCP istemcisi** (harici MCP sunucularının araçlarını bizim ajanlara bağlamak) yapılmadı. Harici bir sunucunun araç
  açıklamaları ve sonuçları LLM bağlamına girer: prompt-injection ve veri sızıntısı yüzeyi doğurur ve ayrı bir güvenlik
  tasarımı (izinli sunucu listesi, çıktı sınırları, onay) gerektirir.
- MCP *resources* ve *prompts* yayınlanmaz; yalnızca araçlar.
