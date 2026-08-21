# ContextPipeline

**Dosya:** `Services/Chat/ContextPipeline.cs`
**Port:** `IContextPipeline`
**Ayarlar:** `ContextPipelineOptions` (`appsettings.json` → `ContextPipeline`)

## 1. Ne İşe Yarar

Kayıtlı tüm [`IContextProvider`](../Providers/ContextProviders.md)'ları çalıştırır ve
sonuçlarını workflow prompt'una girecek tek bir bağlam metnine birleştirir.

## 2. Temel ilke

> **Bağlam bir iyileştirmedir, zorunluluk değil.**

Üretimi turu süresiz bekletmemeli, prompt'u sınırsız şişirmemeli ve bir parçası düştüğünde
tur durmamalı. Aşağıdaki dört mekanizmanın tamamı bu ilkenin sonucudur.

## 3. Çalışma biçimi

```
provider'lar PARALEL koşar          →  biri diğerini beklemez
sonuç Order'a göre BİRLEŞİR         →  prompt turdan tura kaymaz (cache dostu)
her provider AYRI try/catch + timeout →  biri patlayınca bağlamın tamamı kaybolmaz
bütçe tavanı                        →  dolduğunda düşük öncelikli olan dışarıda kalır
```

### 3.1 Zaman aşımı (`ProviderTimeoutSeconds`, varsayılan 5sn)

Her provider kendi bütçesiyle koşar; süresi dolan atlanır ve tur devam eder.

> 🐞 **Neden eklendi:** `IContextProvider.GetContextAsync` imzasında `CancellationToken` bile
> yoktu ve pipeline `Task.WhenAll`'ı çıplak çağırıyordu — yani hiçbir provider kesilebilir
> değildi. Bu, pipeline içinde **ağ ve LLM çağrıları olduğu için** ciddiydi:
> `SemanticMemoryContextProvider` embedding + vektör araması yapıyor,
> `ConversationSummaryProvider` doğrudan bir `IChatClient.CompleteAsync` çağırıyor. Bağlam
> kurulumu workflow'dan **önce** çalıştığından `WorkflowGuardOptions.TimeoutSeconds` koruması
> da henüz devrede değildi; yavaş bir provider turu belirsiz süre bloklayabiliyordu.

Token artık uçtan uca geçiriliyor — süre dolduğunda iş gerçekten iptal ediliyor, arka planda
sürmüyor.

### 3.2 Kritik / iyileştirici ayrımı (`IContextProvider.IsCritical`)

| Provider tipi | Düştüğünde |
|---|---|
| İyileştirici (varsayılan) | Sessizce atlanır — bot yine makul cevap verebilir |
| **Kritik** | Bağlama açık bir **[UYARI]** konur: "bu bilgi okunamıyor, tahminde bulunma" |

Şu an `IsCritical = true` yapan bir provider yok — mekanizma dorman ama yerinde duruyor.

> 🐞 **Geçmişte gerekliydi, artık kaldırıldı:** Tek kritik provider `CustomerContextProvider`
> idi — müşterinin sipariş/şikayet geçmişini her turda koşulsuz enjekte ediyordu ve düştüğünde
> model eksikliği fark etmeyip *"kayıtlı siparişiniz bulunamadı"* diyebiliyordu (altyapı hatası
> → yanlış olgu). Bu sınıf tamamen kaldırıldı: işlevi zaten sorgu tool'larıyla
> (`get_last_order`, `get_all_orders` vb.) tam olarak çakışıyordu ve prompt'lar zaten
> tool-odaklı yazılmıştı — enjekte edilen bloğa hiç referans vermiyorlardı. Bkz.
> [ContextProviders.md](../Providers/ContextProviders.md#customercontextprovider-kaldırıldı--toollara-taşındı).
> Yeni bir kritik provider eklenirse buradaki uyarı mekanizması devreye girer.

### 3.3 Bütçe (`MaxProviderChars`, `MaxTotalChars`)

Provider başına çıktı kırpılır; toplam tavana ulaşıldığında **en düşük öncelikli** (en yüksek
`Order`) provider'lar dışarıda bırakılır — kritik bağlam önce yerleşir. Dışarıda kalanlar
`Dropped` olarak raporlanır, sessizce kaybolmaz.

### 3.4 Raporlama (`ContextResult`)

Pipeline düz `string` değil `ContextResult` döner: birleşik metin **+ hangi provider'ın ne
yaptığı** (`Included` / `Empty` / `Failed` / `TimedOut` / `Dropped` + boyut).

İki tüketicisi var:

1. **`WorkflowMessageBuilder`** — geçmişi kırpma kararı. Özetlenen turlar yalnızca özet
   **bu turda gerçekten prompt'a girdiyse** atlanabilir.
   > 🐞 Karar oturum durumuna bakılarak veriliyordu ve bu bir regresyon üretmişti:
   > özetleyici hata verdiğinde/zaman aşımına uğradığında provider `null` döner ama
   > `SessionState.ConversationSummary` eski değerini korur — böylece özet prompt'ta olmaz,
   > geçmiş yine de atlanır ve **o turlar modelin görüş alanından tamamen kaybolurdu.**
2. **`ReasoningTrace.ContextParts`** — "model bu turda neyi biliyordu?" sorusunun kaydı.
   Yanlış yanıtların sebebi çoğu zaman eksik bağlamdır; eskiden bu yalnızca `Debug`
   seviyesinde loglandığı için trace'ten görünmüyordu.

## 4. Koşullu çalıştırma

Her provider **olgu tabanlı** erken çıkışa sahip ve bunlar zaten yerinde:

| Provider | Erken çıkış |
|---|---|
| `SemanticMemoryContextProvider` | Bellek kapalıysa / sorgu boşsa |
| `CustomerProfileContextProvider` | Kimlik yoksa / profil boşsa |
| `ConversationSummaryProvider` | Geçmiş 8 mesajdan kısaysa |

**Sezgisel** koşullu çalıştırma (ör. "teşekkürler" gibi turlarda semantik aramayı atla)
bilinçli olarak **yapılmadı** — bkz. [ContextProviders.md](../Providers/ContextProviders.md).

## 5. Ayarlar

```json
"ContextPipeline": {
  "ProviderTimeoutSeconds": 5,
  "MaxProviderChars": 4000,
  "MaxTotalChars": 12000
}
```

Üçü de başlangıçta doğrulanır (`ValidateOnStart`); `MaxTotalChars < MaxProviderChars` ise
uygulama açılmaz.

## Bağlantılar

- [../Providers/ContextProviders.md](../Providers/ContextProviders.md) — Provider'lar
- [ChatPortService.md](ChatPortService.md) — Turu orkestre eden servis
- [../../CustomerSupportBot.Adapters.Agents/WorkflowRunner.md](../../CustomerSupportBot.Adapters.Agents/WorkflowRunner.md) — `ContextParts`'ı trace'e yazan taraf
