# PersonalizationPortService

**Dosya:** `Services/PersonalizationPortService.cs`  
**Implements:** `IPersonalizationPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Müşteri profil kişiselleştirmesini API katmanına açar. Profil listeleme, tekil profil getirme, LLM ile konsolidasyon, admin notu ekleme ve profil silme işlemlerini sağlar.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `CustomerProfileService` | Heuristik + LLM profil güncelleme servisi |
| `ICustomerProfileStore` | Profil kalıcılık katmanı |

---

## Metodlar

### `GetProfiles`

```csharp
(int Count, IReadOnlyList<CustomerProfile> Items) GetProfiles(int take = 100)
```

Toplam profil sayısı ve ilk `take` kadar profili döner.

---

### `GetProfile`

```csharp
CustomerProfile? GetProfile(string customerId)
```

Tekil müşteri profilini döner.

---

### `RefreshProfileAsync`

```csharp
Task<CustomerProfile?> RefreshProfileAsync(string customerId, CancellationToken ct = default)
```

`CustomerProfileService.ConsolidateAsync` çağırır — LLM ile profilin `Summary` ve `PreferredTone` alanlarını günceller.

> Bu metod LLM çağrısı yapar. Yoğun kullanımda dikkatli tetiklenmelidir.

---

### `SetAdminNote`

```csharp
CustomerProfile SetAdminNote(string customerId, string? note)
```

Müşteri profiline admin notu ekler veya temizler (null/boş string gelirse siler).

**Akış:**
```
1. _profiles.GetOrCreate(customerId)  → profil yoksa boş oluştur
2. profile.AdminNote = note
3. _profiles.Upsert(profile)
4. güncel profil döndür
```

`SkillsBasedRouter`, eskalasyon yönlendirmesinde `AdminNote` içindeki anahtar kelimeleri skill'e çevirir. Örneğin "VIP müşteri" → `vip` skill tag'i.

---

### `DeleteProfile`

```csharp
bool DeleteProfile(string customerId)
```

Müşteri profilini kalıcı siler. Bulunamazsa `false` döner.

---

## CustomerProfile modeli

```csharp
public class CustomerProfile
{
    string CustomerId;
    int TotalSessions;
    int TotalTurns;
    DateTime? LastInteractionAt;
    DateTime? LastConsolidatedAt;
    string? PreferredLanguage;     // "tr" / "en" — heuristik
    string? PreferredTone;         // "formal" / "casual" / "concise" / "verbose" / "neutral"
    string? Summary;               // LLM özeti (max 2 cümle)
    string? AdminNote;             // Manuel admin notu — SkillsBasedRouter'da kullanılır
    Dictionary<string, int> IntentFrequency;   // intent → sayı (max 20 entry)
    List<string> ProductInterests;             // son ilgilenilen ürünler (max 10)
    List<int> RecentRatings;                   // son puanlar (max 10)
}
```

---

## Profil güncelleme akışı

Profil iki yoldan güncellenir; `PersonalizationPortService` yalnızca okuma ve admin işlemlerine hizmet eder:

```
Otomatik (her workflow turu)
  CustomerProfileService.RecordInteractionAsync() → LLM-siz, heuristik

Manuel (admin tetikler)
  PersonalizationPortService.RefreshProfileAsync() → LLM ile konsolidasyon
```

---

## API endpoint'leri

```http
GET    /personalization/profiles              → GetProfiles
GET    /personalization/profiles/{customerId} → GetProfile
POST   /personalization/profiles/{id}/refresh → RefreshProfileAsync
PATCH  /personalization/profiles/{id}/note    → SetAdminNote
DELETE /personalization/profiles/{id}         → DeleteProfile
```
