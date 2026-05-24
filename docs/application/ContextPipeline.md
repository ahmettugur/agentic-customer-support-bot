# ContextPipeline ve Context Providers

**Ana dosya:** `CustomerSupportBot.Application/Services/ContextPipeline.cs`  
**Implements:** `IContextPipeline`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

`ContextPipeline`, `CustomerSupportTeam` workflow'u başlamadan önce mevcut oturum hakkındaki tüm bağlam bilgisini toplar. Birden fazla `IContextProvider`'ı **paralel** çalıştırır ve çıktılarını birleştirerek tek bir string döner. Bu string, workflow'daki tüm ajanlara `System` mesajı olarak verilir.

## Çalışma şekli

```csharp
// Constructor'da provider'lar Order'a göre sıralanır
_providers = providers.OrderBy(p => p.Order);

// BuildContextAsync: hepsi paralel çalışır
var tasks = providerList.Select(async p => {
    var ctx = await p.GetContextAsync(session);
    return (Order: p.Order, Context: ctx);
});
var results = await Task.WhenAll(tasks);

// Sonuçlar Order'a göre sıralanarak birleştirilir
return string.Join("\n\n", results.OrderBy(r => r.Order)
    .Select(r => r.Context)
    .Where(c => !string.IsNullOrWhiteSpace(c)));
```

**Hata yönetimi:** Her provider ayrı bir try/catch içinde çalışır. Bir provider hata verirse log'a yazılır ve atlanır — diğer provider'lar etkilenmez.

---

## IContextProvider arayüzü

```csharp
public interface IContextProvider
{
    string Name  { get; }   // Loglama için tanımlayıcı ad
    int    Order { get; }   // Küçük = daha önce (sistemin birleştirme sırasında)
    Task<string?> GetContextAsync(AgentSession session);
}
```

`GetContextAsync` null veya boş string döndürebilir — bu durumda provider'ın katkısı atlanır.

---

## Kayıtlı Provider'lar

| Provider | Order | Açıklama | Koşul |
|----------|-------|---------|-------|
| `ConversationSummaryProvider` | 5 | Uzun geçmişi LLM ile özetler | Geçmişte ≥8 mesaj varsa |
| `CustomerProfileContextProvider` | 6 | Müşteri profili (tercihler, sık niyetler) | CustomerId set, profil mevcut |
| `CustomerContextProvider` | 10 | Sipariş ve şikayet geçmişi | CustomerId set |
| `SemanticMemoryContextProvider` | 7 | Knowledge base + dersler (vector arama) | `SemanticMemory.Enabled=true` |
| `NoopContextProvider` | ∞ | Hiçbir şey dönmez | Semantic memory kapalıysa |

Birleşik çıktı örneği:

```
[Konuşma Özeti]
Müşteri ORD-4821'in durumunu sormuş, asistan "kargoda" cevabı vermiş.

## 👤 Müşteri Profili
- ID: CUST-12
- Tercih edilen dil: tr, ton: formal
...

[Müşteri Bağlamı — CUST-12]
Toplam sipariş: 3
  - ORD-4821: Laptop x1, Durum: shipped, Tarih: ...
  ...
```

---

## ConversationSummaryProvider

**Dosya:** `Services/Providers/ConversationSummaryProvider.cs`

Geçmişte `SummaryThreshold` (8) veya daha fazla mesaj varsa, eski mesajları LLM ile özetler. Son `RecentMessageCount` (4) mesaj özetlenmez — güncel bağlam korunur.

**Cache mekanizması:** Özet session state'de (`session.State.ConversationSummary`) saklanır. Geçmiş sayısı son özetten bu yana `RecentMessageCount`'tan az artmışsa yeni LLM çağrısı yapmaz, mevcut özeti döner.

**LLM çağrısı:** `IGeneralChatClient.CompleteAsync` — max 150 kelimelik Türkçe özet üretir.

---

## CustomerContextProvider

**Dosya:** `Services/Providers/CustomerContextProvider.cs`

`session.State.CustomerId` ile müşterinin en fazla 5 siparişini ve 3 şikayetini listeler. CustomerId set değilse null döner.

---

## CustomerProfileContextProvider

**Dosya:** `Services/Providers/CustomerProfileContextProvider.cs`

`ICustomerProfileStore.Get(customerId)` ile müşterinin uzun vadeli profilini getirir. Profil `TotalTurns > 0` ise bağlama eklenir. İçeriği:
- Tercih edilen dil ve ton
- Toplam oturum/tur sayısı
- En sık 3 niyet
- İlgi duyulan ürünler
- Son ortalama puan

Ajana kişiselleştirilmiş yanıt üretmesi için ipucu sağlar.

---

## SemanticMemoryContextProvider

**Dosya:** `Services/Providers/SemanticMemoryContextProvider.cs`

Kullanıcının son mesajını `SemanticMemoryService`'e gönderir. `Knowledge` ve `Lessons` koleksiyonlarında paralel vector arama yapar (`Task.WhenAll`). Bulunan chunk'ları karakter bütçesi (`MaxContextChars`) dahilinde bağlama ekler.

**Önem sırası:** Knowledge > Lessons

**Bütçe kontrolü:** Her chunk eklendiğinde `budget` azaltılır. `budget < 100` olunca ek chunk eklenmez.

---

## Yeni provider eklemek

1. `IContextProvider`'ı implemente eden sınıf yazın.
2. `Order` değerini mevcut sıralamaya göre atayın (tabloyu yukarıda inceleyin).
3. DI kaydını `ApplicationServiceCollectionExtensions.AddContextProviders()` içine ekleyin:

```csharp
services.AddSingleton<IContextProvider, YeniProvider>();
```

DI container tüm `IContextProvider` kayıtlarını toplar — `ContextPipeline` constructor'ında `IEnumerable<IContextProvider>` olarak gelir. Ayrıca `ContextPipeline`'a dokunmanız gerekmez.

## Sık sorular

**Provider'ların sırası neden önemli?**  
Birleşik context string'de önce Order=5 çıktısı, sonra Order=6, sonra 7, sonra 10 gelir. Ajanlar ilk gördükleri bilgilere daha fazla dikkat eder; kritik bilgileri düşük Order ile öne koyun.

**Paralel çalışma performans farkı yaratır mı?**  
Evet. `ConversationSummaryProvider` LLM çağrısı yapar (~200-500ms); `CustomerContextProvider` DB sorgusu yapar (~5-20ms). Sıralı çalışsaydı toplam süre tüm gecikmeler toplamı olurdu. Paralelde en yavaş provider'ın süresi kadar beklenilir.
