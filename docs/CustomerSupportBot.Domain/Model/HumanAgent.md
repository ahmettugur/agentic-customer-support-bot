# HumanAgent

**Dosya:** `Model/HumanAgent.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `HumanAgentInput`, `EscalationPriority` enum, `RoutingDecision` class (aynı dosyada)

## 1. Ne İşe Yarar

Bir **insan müşteri temsilcisi profilini** temsil eder — yetkinlik etiketleri (skills), dil desteği, mevcut yük (capacity) ve öncelik bilgileriyle. Skills-based routing bu profili kullanarak eskalasyon taleplerini en uygun temsilciye yönlendirir.

## 2. Hangi Amaçla Kullanılır

Admin panelinden temsilciler oluşturulur ve yönetilir. Eskalasyon olduğunda `SkillsBasedRouter` tüm aktif temsilcilerin profillerini okur, `RequiredSkills` ile eşleştirir ve en uygun olanını önerir.

> 💡 **Analiz notu:** Bir şirketin İK sistemindeki çalışan profili gibi — hangi dilleri biliyor, hangi konularda uzman, şu an kaç dosyayla ilgileniyor.

## 3. Metotlar / Üyeler

### HumanAgent

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Id` | `string` | Benzersiz kimlik (8 char) |
| `DisplayName` | `string` | Görünür isim |
| `Email` | `string?` | İletişim e-postası |
| `Skills` | `List<string>` | Yetkinlik etiketleri (ör. ["complaint", "refund", "tr"]) |
| `Languages` | `List<string>` | Dil desteği (ISO kodları) |
| `IsActive` | `bool` | Aktif mi? Pasif temsilciler routing'e dahil edilmez |
| `MaxConcurrentLoad` | `int` | Aynı anda bakabileceği max eskalasyon sayısı |
| `CurrentLoad` | `int` | Şu an üzerindeki Open+Acknowledged eskalasyon sayısı |
| `Priority` | `int` | Tie-break önceliği (yüksek tercih edilir) |

### EscalationPriority Enum

| Değer | Açıklama |
| ------- | ---------- |
| `Low` | Düşük öncelik |
| `Normal` | Normal (varsayılan) |
| `High` | Yüksek öncelik |
| `Critical` | Kritik — hemen ilgilenilmeli |

### RoutingDecision

Skills-based router'ın çıktısı — hangi temsilci, neden, eşleşme skoru.

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `SuggestedAgentId` | `string?` | Önerilen temsilci ID |
| `SuggestedAgentName` | `string?` | Önerilen temsilci adı |
| `MatchScore` | `double` | Eşleşme skoru (0-1) |
| `MatchedSkills` | `List<string>` | Eşleşen yetkinlikler |
| `MissingSkills` | `List<string>` | Eksik yetkinlikler |
| `Note` | `string?` | Açıklama |

## Bağlantılar

- [EscalationRequest.md](EscalationRequest.md) — Bu temsilcilere atanan eskalasyonlar
- [../../CustomerSupportBot.Application/SkillsBasedRouter.md](../../CustomerSupportBot.Application/Services/Routing/SkillsBasedRouter.md) — Routing algoritması
