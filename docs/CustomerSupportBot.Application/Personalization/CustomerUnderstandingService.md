# CustomerUnderstandingService

**Dosya:** `Services/Personalization/CustomerUnderstandingService.cs`
**Port:** `ICustomerUnderstandingService`

## 1. Ne İşe Yarar

`CustomerProfile`'ı (depolama şekli — Postgres satırı, artımlı sayaçlar, null olabilen
alanlar) tek, her zaman tutarlı bir `CustomerUnderstanding` görünümüne sentezler. **LLM
çağırmaz** — saf, senkron bir dönüşümdür; maliyet sınıfı `RecordInteractionAsync`'le aynıdır.

## 2. Neden ayrı bir katman — Memory ile Understanding farkı

```
Memory (CustomerProfile)          Understanding (CustomerUnderstanding)
─────────────────────────         ──────────────────────────────────────
Nasıl saklandığı                  Nasıl OKUNDUĞU
Null olabilen alanlar             "Profil yoksa/boşsa null" — TEK yerde karar
Ham sayaçlar (IntentFrequency)    Sıralanmış, top-N kırpılmış (TopIntents)
Depolama detayı                   Tüketiciye sunulan sözleşme
```

Bu ayrım olmadan, her yeni tüketici (bugün `CustomerProfileContextProvider`, yarın bir öneri
motoru) aynı üç soruyu kendi başına yeniden cevaplamak zorunda kalırdı: *"customerId yoksa ne
olacak? Profil yoksa ne olacak? TotalTurns sıfırsa göstermeli miyim?"* Cevap artık tek yerde —
yanlış cevaplanırsa (ör. `TotalTurns` kontrolü unutulursa) tek yerde düzelir, her tüketicide
ayrı ayrı değil.

## 3. `Build(session)` — null semantiği

| Durum | Dönüş |
|---|---|
| `session.State.AuthenticatedCustomerId` boş | `null` |
| Profil bulunamadı | `null` |
| Profil var ama `TotalTurns == 0` | `null` |
| Aksi | dolu `CustomerUnderstanding` |

> ⚠️ **`AuthenticatedCustomerId` — `State.CustomerId` DEĞİL.** İkincisi LLM'in kullanıcı
> metninden çıkardığı, kullanıcının serbestçe değiştirebildiği bir alandır. Onunla
> sorgulamak başka bir müşterinin profilini (admin notu, geçmiş özeti dahil) sızdırırdı —
> bu, eski `CustomerProfileContextProvider`'ın (bu servis onun yerine geçmeden önceki hâli)
> en kritik testiydi ve sentez servise taşınırken korundu
> (`Build_UsesOnlyAuthenticatedCustomerId_NotLlmExtractedOne`).

## 4. Bilerek taşımadığı: anlık niyet/duygu/faz

`CustomerUnderstanding` yalnızca **uzun vadeli** sentezi taşır. Bu turun niyeti/duygusu zaten
`WorkflowMessageBuilder.BuildReasoningSummaryHint` ile (o turun `ReasoningResult`'ından) ayrı
olarak bağlama giriyor. İkisini birleştirmek, "session'ın şu an durumu" ile "profilin genelde
durumu" çelişirse hangisinin doğru olduğu belirsizliğini yaratırdı.

## 5. `LastConsolidatedAt` — tazelik sinyali

`Persona`/`Traits` yalnızca `CustomerProfileService.ConsolidateAsync` çağrıldığında üretilir
(admin tetikler). `ProductInterests`/`TopIntents` ise heuristik — her turda güncellenir.
`LastConsolidatedAt` `null` ise `Persona`/`Traits` boştur ama diğer alanlar yine günceldir —
tüketici bu ikisini birbirinden ayırt edebilsin diye taşınır.

## Bağlantılar

- [CustomerProfileService.md](CustomerProfileService.md) — `Traits`'in nasıl üretildiği
- [../Providers/ContextProviders.md](../Providers/ContextProviders.md) — Doğrudan tüketici: `CustomerProfileContextProvider`
- [RecommendationService.md](RecommendationService.md) — Dolaylı tüketici: `ProductInterests`'i öneri üretmek için okur
- [../Ports.md](../Ports.md) — Port kaydı
