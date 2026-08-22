# InMemoryHumanAgentRegistry

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryHumanAgentRegistry.cs`
- **Port:** `IHumanAgentRegistry` (`CustomerSupportBot.Application.Services.Routing`)
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

İnsan temsilcilerin (admin panelinde görünen agent listesi) kayıtlarını `ConcurrentDictionary<string, HumanAgent>` ile bellekte tutan, uygulama açılışında `RoutingOptions.SeedAgents`'tan tohumlanan registry'dir.

## 2. Hangi Amaçla Kullanıldığı

`PostgresHumanAgentRegistry`'nin tek-process karşılığı. Escalation routing'inin ("hangi temsilciye devredilsin") temel veri kaynağıdır.

## 3. Sorumlulukları

- Açılışta `appsettings.json`'daki `SeedAgents` listesinden **savunmacı kopya** (`new HumanAgent {...}`) oluşturarak tohumlama — orijinal seed nesnelerinin instance'ı paylaşılmaz.
- CRUD: `Create`, `Update` (kısmi güncelleme — sadece `HumanAgentInput`'ta dolu olan alanlar değişir), `Delete`, `Get`, `GetAll`, `GetActive`.
- Yük sayacı yönetimi: `IncrementLoad`/`DecrementLoad` (`lock (a)` ile eşzamanlı erişime karşı korumalı; `CurrentLoad` asla negatife düşmez).
- Etiket normalizasyonu (`NormalizeTags`) — boşlukları temizler, küçük harfe çevirir, tekrarları eler.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresHumanAgentRegistry` (`../Postgres/HitlAndChat.md`) ile aynı arayüzü uygular.
- `RoutingOptions` (Application katmanı) üzerinden `IOptions<RoutingOptions>` ile tohum verisini alır.
- `GetLinkedUsersAsync` — Postgres karşıtının aksine burada her zaman **boş liste** döner (`Task.FromResult(new List<HumanAgent>())`); bu, "kullanıcı hesabına bağlı temsilci" ilişkisinin veritabanı (auth tabloları) gerektirmesinden, bellek-içi implementasyonda anlamsız olmasından kaynaklanır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`StringComparer.OrdinalIgnoreCase` sözlük anahtarlaması — agent ID'lerinin büyük/küçük harf farkından kaynaklanan kayıp aramaları önler. `MaxConcurrentLoad <= 0` ise otomatik `5`'e set edilmesi — appsettings'te unutulan bir alanın sessizce "hiç yük almaz" (`0`) anlamına gelmesini önleyen bir varsayılan-değer güvencesi.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetAll()` | Aktiflik ve ada göre sıralı tüm temsilciler. |
| `GetActive()` | Sadece `IsActive == true` olanlar. |
| `Get(id)` | Tek kayıt, ID boşsa `null`. |
| `Create(agent)` | ID boşsa 8 karakterlik yeni ID üretir, etiketleri normalize eder. |
| `Update(id, input)` | Sadece dolu alanları günceller (kısmi PATCH semantiği). |
| `Delete(id)` | Kaydı siler. |
| `IncrementLoad(id)` / `DecrementLoad(id)` | Yük sayacını thread-safe artırır/azaltır. |
| `GetLinkedUsersAsync(ct)` | Her zaman boş liste (bkz. yukarıdaki not). |

## 7. Bağımlılıklar

- `IOptions<RoutingOptions>`
