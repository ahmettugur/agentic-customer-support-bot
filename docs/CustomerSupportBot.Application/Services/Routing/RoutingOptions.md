# RoutingOptions

- **Kaynak:** `Services/Routing/RoutingOptions.cs`
- **Tür:** `public class`
- **Namespace:** `CustomerSupportBot.Application.Services.Routing`

## 1. Ne İşe Yarar

[`SkillsBasedRouter`](SkillsBasedRouter.md)'ın davranışını `appsettings.json`'daki `"Routing"`
bölümünden okunan bir ayar nesnesi. Intent→skill eşlemesi, dil ağırlığı, minimum eşleşme skoru
ve başlangıç (seed) temsilci listesini taşır.

## 2. Hangi Amaçla Kullanılır

Eskalasyon anında hangi skill etiketlerinin gerekli sayılacağını ve temsilci seçim skorunun
nasıl ağırlıklandırılacağını kod değiştirmeden (yalnızca appsettings ile) ayarlanabilir kılmak.

## 3. Sorumlulukları

Yalnızca veri taşır — hiçbir hesaplama/karar mantığı içermez (o [`SkillsBasedRouter`](SkillsBasedRouter.md)'da).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IOptions<RoutingOptions>` olarak [`SkillsBasedRouter`](SkillsBasedRouter.md)'a enjekte edilir.
- `SeedAgents` — uygulama ilk açıldığında `InMemoryHumanAgentRegistry`'ye eklenen başlangıç temsilcileri.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`IntentSkillMap`/`ProfileKeywordSkillMap`'in `StringComparer.OrdinalIgnoreCase` ile
oluşturulması bilinçli: appsettings'te yazılan intent/anahtar kelime metinleriyle kod
tarafındaki değerlerin büyük/küçük harf farkından dolayı eşleşmemesi riskini ortadan kaldırır.

## 6. Metotlar / Üyeler

| Üye | Tip | Varsayılan | Açıklama |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Smart routing açık mı? Kapalıysa `SkillsBasedRouter.Decide` no-op döner (`Note` ile birlikte boş `RoutingDecision`). |
| `IntentSkillMap` | `Dictionary<string, List<string>>` | boş | Reasoning trace'inde tespit edilen `Intent`'e göre eklenecek skill etiketleri eşlemesi. |
| `ProfileKeywordSkillMap` | `Dictionary<string, string>` | boş | Müşteri profilinin admin notunda geçen anahtar kelime → skill tag eşlemesi (ör. `"VIP"` → `"vip"`). |
| `LoadBalancingEnabled` | `bool` | `true` | Açıksa, aynı skor aralığındaki temsilciler arasında `CurrentLoad`'u düşük olan hafif bir avantaj kazanır. |
| `LanguageWeight` | `double` | `0.2` | Skor formülünde dil eşleşmesinin ağırlığı (0..1) — bkz. [SkillsBasedRouter.md](SkillsBasedRouter.md). |
| `MinMatchScore` | `double` | `0.1` | Bu değerin altındaki en iyi skor "match yok" sayılır; eskalasyon yine kaydedilir ama `SuggestedAgentId` boş kalır. |
| `SeedAgents` | `List<HumanAgent>` | boş | Uygulama ilk başlatıldığında `InMemoryHumanAgentRegistry`'ye eklenen başlangıç temsilci listesi. |

## 7. Bağımlılıklar

Yalnızca `HumanAgent` (Domain modeli) tipine bağımlıdır.

## Bağlantılar

- [SkillsBasedRouter.md](SkillsBasedRouter.md) — bu ayarları kullanan servis
