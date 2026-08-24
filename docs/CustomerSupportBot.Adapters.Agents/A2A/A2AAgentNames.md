# A2AAgentNames

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/A2A/A2AAgentCatalog.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents.A2A`

## 1. Ne işe yarar?

A2A protokolüyle dışarı yayınlanan üç ajanın (`Product`, `Order`, `Complaint`) adlarını tutan bir sabitler sınıfıdır. Her ajan adı sadece bir `public const string`'tir; kendisi çalışan bir bileşen değildir, sadece isim kaynağıdır.

## 2. Hangi amaçla kullanılır?

`AddA2AServer` kaydı sırasında ve AgentCard/endpoint tarafında **aynı** ajan adının kullanılmasını garanti etmek için vardır. Ajan adı iki farklı yerde serbest metin (`"ProductInfoAgent"` gibi) olarak yazılsaydı, biri değiştirildiğinde diğeri değiştirilmeyi unutulabilirdi ve A2A köprüsü çalışma zamanında sessizce kopardı (ajan kaydı yapılmış ama AgentCard'da bulunamayan bir ada işaret ederdi).

## 3. Sorumlulukları

- **Üstlendiği:** Üç sabit ajan adını tek, paylaşılan bir yerde tutmak.
- **Üstlenmediği:** Ajanların kendisini oluşturmak (bu iş [A2AAgentCatalog](A2AAgentCatalog.md)'un constructor'ında yapılır).

## 4. Diğer katman ve bileşenlerle ilişkileri

- [A2AAgentCatalog](A2AAgentCatalog.md) constructor'ı içinde `A2AAgentNames.Product`, `.Order`, `.Complaint` değerlerini `ChatClientAgent.Name` olarak kullanır.
- A2A endpoint/AgentCard kaydı yapan Api katmanı kodu da aynı sabitleri referans alır.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Projenin genelinde geçerli olan "Tek Doğruluk Kaynağı" ilkesinin (bkz. Domain katmanındaki `WellKnown` sınıfı) küçük ölçekli bir örneğidir — magic string'lerin tek bir yerde toplanması, iki yerde senkronsuz kalma riskini ortadan kaldırır.

## 6. Metotlar / Üyeler

| Üye | Tür | Değer | Açıklama |
|---|---|---|---|
| `Product` | `const string` | `"ProductInfoAgent"` | Dış sistemlere açık, salt-okunur ürün kataloğu ajanının adı. |
| `Order` | `const string` | `"OrderInfoAgent"` | Dış sistemlere açık, salt-okunur sipariş bilgisi ajanının adı. |
| `Complaint` | `const string` | `"ComplaintInfoAgent"` | Dış sistemlere açık, salt-okunur şikayet bilgisi ajanının adı. |

## 7. Bağımlılıklar

Yok — statik sabitlerden oluşan, bağımlılıksız bir yardımcı sınıf.
