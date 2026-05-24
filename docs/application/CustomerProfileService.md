# CustomerProfileService

**Dosya:** `Services/Personalization/CustomerProfileService.cs`  
**Implements:** `ICustomerProfileService`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Müşteri profilini iki yoldan günceller: her workflow turu sonrasında LLM kullanmadan heuristik güncelleme (sıfır maliyet), ve admin talep ettiğinde LLM ile özet + ton analizi (konsolidasyon). Dağıtık lock sayesinde aynı müşteri için eş zamanlı güncellemeler serialize edilir.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `ICustomerProfileStore` | Profil kalıcılık katmanı |
| `IGeneralChatClient` | LLM çağrısı (sadece ConsolidateAsync'te) |
| `IAppDistributedLock` | Per-customer distributed lock (Redis/InMemory) |
| `IProductCatalogRepository` | Ürün adı tanıma için katalog |

---

## `RecordInteractionAsync`

```csharp
Task<CustomerProfile?> RecordInteractionAsync(
    string? customerId,
    string userQuery,
    string botResponse,
    string? intent,
    int? rating = null,
    bool isNewSession = false,
    CancellationToken ct = default)
```

**LLM çağrısı YOK.** Tamamen deterministik, kural tabanlı.

`_distributedLock.AcquireAsync($"profile:{customerId}")` ile serialized çalışır.

**Güncellenen alanlar:**

| Alan | Güncelleme kuralı |
|------|-----------------|
| `TotalTurns` | +1 |
| `TotalSessions` | `isNewSession == true` ise +1 |
| `LastInteractionAt` | `DateTime.UtcNow` |
| `IntentFrequency[intent]` | +1; 20'den fazla entry'de en az kullanılan silinir |
| `ProductInterests` | Soru + yanıt metninden ürün adı extraksiyon; son kullanılan başa geçer (max 10) |
| `PreferredLanguage` | Türkçe karakter veya kelime varsa `"tr"`, İngilizce sinyal varsa `"en"` |
| `RecentRatings` | 1–5 arası rating eklenir (max 10, eski atılır) |

---

## `ConsolidateAsync`

```csharp
Task<CustomerProfile?> ConsolidateAsync(string customerId, CancellationToken ct = default)
```

**LLM çağrısı YAPAR.** Admin veya API üzerinden tetiklenir.

**LLM prompt'u içeriği:**
- Toplam oturum ve tur sayısı
- Tercih edilen dil
- En sık 5 niyet
- İlgilenilen ilk 5 ürün
- Son puanlar ve ortalama

**LLM çıktısı:**
```json
{
  "summary": "Müşteri ağırlıklı olarak sipariş sorgulama yapıyor...",
  "preferredTone": "formal"
}
```

**Güncellenen alanlar:**
- `profile.Summary` — 1–2 cümle özet (Türkçe)
- `profile.PreferredTone` — `formal | casual | concise | verbose | neutral`
- `profile.LastConsolidatedAt` — `DateTime.UtcNow`

LLM yanıtı parse edilemezse Warning log yazılır, profil değiştirilmeden döner.

---

## Dil tespiti (heuristik)

### LooksTurkish

Türkçe özel karakterler (`çğıöşüÇĞİÖŞÜ`) veya yaygın Türkçe kelimeler:
`merhaba, sipariş, şikayet, teşekkür, nerede, nasıl, nedir, var mı, lütfen, iade`

### LooksEnglish

Türkçe karakter YOK ve İngilizce kelimeler:
`hello, order, where, how, please, thanks, thank you, return, complaint`

---

## Ürün ilgi tespiti

Kullanıcı sorusu + bot yanıtından tüm katalog ürün adları taranır. Case-insensitive tam eşleşme aranır. Eşleşen ürünler `ProductInterests` listesinin başına eklenir; liste 10 ile sınırlandırılır.

---

## Tasarım kararı: LLM maliyet sınıfı

`RecordInteractionAsync` kasıtlı olarak LLM çağrısı yapmaz. Profil her konuşma turunda güncellendiği için LLM kullansaydı N×M maliyet olurdu. LLM yalnızca `ConsolidateAsync`'te, yani admin elle tetiklediğinde çalışır.

---

## Dağıtık lock

```csharp
await using var handle = await _distributedLock
    .AcquireAsync($"profile:{customerId}", ct: ct);
```

Aynı müşteri için aynı anda gelen iki workflow turu çakışmadan sırayla profil güncellemesini garantiler.
