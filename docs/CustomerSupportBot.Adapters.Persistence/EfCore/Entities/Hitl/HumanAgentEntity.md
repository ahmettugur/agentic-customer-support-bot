# HumanAgentEntity

**Dosya:** `EfCore/Entities/Hitl/HumanAgentEntity.cs`
**Şema/Tablo:** `hitl.human_agents`
**Configuration:** [HumanAgentConfiguration](../../Configurations/Hitl/HumanAgentConfiguration.md)

## 1. Ne İşe Yarar

Sisteme kayıtlı bir insan destek temsilcisini, becerilerini/dillerini ve o an ne kadar
"yüklü" (aktif konuşma sayısı) olduğunu temsil eder.

## 2. Hangi Amaçla Kullanılır

Skills-based routing (eskalasyon/live-takeover'da "hangi temsilciye ata" kararı) bu tabloyu
sorgular — beceri/dil eşleşmesi + `CurrentLoad < MaxConcurrentLoad` + `Priority` sırasına göre
en uygun temsilci seçilir.

## 3. Sorumlulukları

- **Üstlendiği:** Temsilcinin becerilerini (`SkillsJson`), dillerini (`LanguagesJson`), yük
  durumunu ve aktiflik bilgisini taşımak.
- **Üstlenmediği:** Atama algoritmasının kendisi — bu, temsilciyi seçen servisin (routing) işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

[EscalationEntity.AssignedTo](EscalationEntity.md) ve `UserEntity.LinkedAgentId` (Auth şeması)
bu entity'ye mantıksal referans verir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`SkillsJson`/`LanguagesJson` string dizileri `jsonb` olarak saklanır — beceri/dil listesi
sabit bir enum kümesi değil, işletme büyüdükçe yeni beceriler eklenebileceğinden esnek bir
liste tercih edilmiştir.

> 🐞 **`ix_human_agents_active` neden filtreli:** Routing sorgusu her zaman "aktif olan
> temsilciler arasından" seçim yapar (`IsActive = true`); pasif temsilcileri de içeren tam bir
> index gereksiz yer kaplardı — partial index sadece ilgili alt kümeyi kapsar.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Birincil anahtar. |
| `DisplayName` | `string` | Görünen ad. |
| `Email` | `string?` | Opsiyonel e-posta. |
| `SkillsJson` | `string` | Beceri listesi, `jsonb` (ör. `["complaint","refund"]`). |
| `LanguagesJson` | `string` | Dil listesi, `jsonb` (ör. `["tr","en"]`). |
| `IsActive` | `bool` | Aktif mi, varsayılan `true`. |
| `MaxConcurrentLoad` | `int` | Aynı anda alabileceği maksimum konuşma, varsayılan `5`. |
| `CurrentLoad` | `int` | Şu an üstlendiği konuşma sayısı. |
| `Priority` | `int` | Atama sırasında öncelik sırası. |
| `CreatedAt` | `DateTime` | Kayıt zamanı. |
| `LastAssignedAt` | `DateTime?` | Son atama zamanı. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [HumanAgentConfiguration](../../Configurations/Hitl/HumanAgentConfiguration.md)
- [EscalationEntity](EscalationEntity.md)
- [README](../README.md)
