# IContextProvider & Provider Zinciri

**Dosya:** `Services/Providers/IContextProvider.cs` ve implementasyonları

## 1. Ne İşe Yarar

Reasoning/agent prompt'una enjekte edilecek bağlam parçalarını üreten provider zinciridir. Her provider farklı bir kaynaktan (geçmiş özeti, müşteri profili, semantic memory, customer identity) bağlam çeker.

## 2. Neden Gerekli

> 💡 **Analiz notu:** Bir doktor vizitesinde hemşirenin hazırladığı "hasta özet dosyası" gibi — alerji bilgisi, geçmiş tedaviler, son tahliller hepsi farklı yerlerden gelir ama tek bir dosyaya birleştirilir.

## 3. Provider'lar

| Provider | Açıklama |
| ---------- | ---------- |
| `ConversationSummaryProvider` | Eski turları özetler — özet, o turların **yerine** geçer (aşağıya bakın) |
| `CustomerIdentityHintBuilder` | AuthenticatedCustomerId → prompt hint |
| `CustomerProfileContextProvider` | Müşteri profili (tercihler, ton, çıkarımlar) — `CustomerUnderstandingService`'in sentezini render eder |
| `SemanticMemoryContextProvider` | Knowledge + Lesson + (kimlik doğrulandıysa) müşterinin geçmiş episode'ları |
| `ProductRecommendationContextProvider` | Kural tabanlı ürün önerisi (isteğe bağlı, susma kuralları için bkz. [RecommendationService.md](../Personalization/RecommendationService.md)) |
| `NoopContextProvider` | Hiçbir şey yapmaz (test/disable için) |

## `CustomerContextProvider` kaldırıldı — tool'lara taşındı

Bu provider müşterinin son 5 siparişini + son 3 şikayetini **her turda koşulsuz** çekip
bağlama enjekte ediyordu — sorgunun sipariş/şikayetle ilgisi olsun olmasın. Kaldırıldı çünkü
işlevi zaten `ApprovalGateService`'teki sorgu tool'larıyla (`get_last_order`, `get_all_orders`,
`order_status`, `complaint_status`, `get_all_complaints`) tam olarak çakışıyordu — üstelik bu
tool'lar daha eksiksizdi (`get_all_orders` TÜM siparişleri döner, provider yalnızca ilk 5'i).
`order-agent.md` prompt'u zaten tamamen tool-odaklı yazılmıştı ve enjekte edilen bloğa hiç
referans vermiyordu; model bu veriyi zaten tool ile almak üzere talimatlandırılmıştı.

Provider'ın `IsCritical = true` olması ayrı bir maliyetti: zaman aşımına uğrarsa
sipariş/şikayetle **hiç ilgisi olmayan** bir soruda bile modele "[UYARI] veriye erişemiyorum"
enjeksiyonu yapılıyordu — konu dışı bir arızanın konu dışı bir soruyu bozması. Kaldırıldıktan
sonra hiçbir provider `IsCritical = true` değil (bkz. `ContextPipeline.md`'deki kritik-önce
sıralama notu).

## `ProductRecommendationContextProvider` — talimat değil, öneri

Bu, Customer Memory işlem hattının **Recommendation** aşamasının bağlama giriş noktası.
`IRecommendationService.Recommend(session)`'ı çağırır ve boş dönmezse metni açıkça *"isteğe
bağlı, YALNIZCA uygun bağlamda bahset — zorlama"* diliyle ekler.

Susma kararının (ne zaman öneri **üretilmeyeceği** — duygu, veri yeterliliği) tamamı
`RecommendationService`'te verilir, burada tekrar edilmez; bu provider yalnızca üretileni
render eder. Ayrıntı: [RecommendationService.md](../Personalization/RecommendationService.md).

## `CustomerProfileContextProvider` — artık `CustomerUnderstanding` üzerinden çalışıyor

Bu provider `ICustomerProfileStore`'u artık **doğrudan okumuyor**. Aradaki sentez katmanı
[`CustomerUnderstandingService`](../Personalization/CustomerUnderstandingService.md)
`CustomerProfile`'ı tek bir `CustomerUnderstanding` nesnesine çeviriyor (null-kontrolleri,
top-N sıralama/kırpma dahil); provider yalnızca bu nesneyi metne döküyor.

Sebep: bu sentez mantığı (customerId yoksa/profil yoksa/tur sıfırsa null dönme, en sık 3
niyet, en yeni 5 ürün ilgisi) ileride başka bir tüketicinin (ör. bir öneri motoru) de
ihtiyaç duyacağı bir şey — tek yerde yaşaması, iki tüketicinin aynı kuralları iki kez
yazmasını (ve sessizce birbirinden sapmasını) önler.

## `SemanticMemoryContextProvider` — Episode retrieval canlandırıldı

Bu provider üç koleksiyonu birleştirir: Knowledge (statik SSS/politika), Lesson
(self-improvement) ve — kimlik doğrulandıysa — Episodic (geçmiş konuşma turları).

> 🐞 **Bulundu ve düzeltildi — episodic bellek write-only ölü veriydi.** `TurnFinalizer` her
> turun sonunda soru+yanıtı Episodic koleksiyonuna yazıyordu, ama bu provider yalnızca
> Knowledge+Lesson arıyordu — yazılan hiçbir episode asla geri okunmuyordu. "Müşteriyle
> geçmişte ne konuşuldu?" sorusunun cevabı yalnızca yapısal veriden (sipariş/şikayet
> tool'larından döndürülen kayıtlardan) geliyordu; konuşmasal geçmiş (ürün tercihleri, daha
> önce sorulan sorular, verilen yanıtlar) hiç kullanılmıyordu.
>
> Artık `session.State.AuthenticatedCustomerId` doluysa Episodic koleksiyonu `customerId`
> tag'iyle filtrelenip aranıyor ve "🗂️ Bu Müşteriyle Geçmiş Görüşmeler" başlığı altında
> bağlama ekleniyor. `SessionId` değil `customerId` ile filtrelenmesi kasıtlı — amaç aynı
> müşterinin **farklı oturumlardaki** geçmişini bulmak; ayrıntı için
> [SemanticMemoryService.md](../Memory/SemanticMemoryService.md#3-writeepisodeasync--customerid-tagi).
>
> Kimlik doğrulanmamışsa (anonim tur) Episodic koleksiyonu hiç aranmaz — filtresiz arama
> başka bir müşterinin geçmişini sızdırma riski taşırdı.

## `ConversationSummaryProvider` — özet neyin yerine geçer

8. mesajtan (`SummaryThreshold`) itibaren, son 4 tur (`RecentMessageCount`) dışındaki tüm
geçmiş LLM ile özetlenir. Özet `SessionState.ConversationSummary`'ye, **kapsadığı mesaj sayısı**
ise `SessionState.SummarizedMessageCount`'a yazılır.

O sayı kritik: `WorkflowMessageBuilder.SelectHistoryToSend` geçmişin ilk o kadar mesajını
**atlar**. Özet onların yerine geçer, kalanlar birebir gönderilir.

> 🐞 **Bulundu ve düzeltildi — özetleme maliyeti azaltmıyor, artırıyordu.** Sayı yoktu ve
> `WorkflowMessageBuilder` geçmişi koşulsuz baştan sona ekliyordu. Yani 8. mesajtan sonra her
> tur: **tam geçmiş + aynı turların özeti + özeti üretmek için fazladan bir LLM çağrısı.**
> Aynı turlar iki kez ödeniyordu. Çıktı doğru olduğu için hiçbir test bunu yakalamamıştı.
>
> **İkinci tur düzeltme — artımlı özetleme (fold).** İlk düzeltme maliyetin yalnızca
> yarısını çözüyordu: özet hâlâ **sıfırdan** üretiliyordu — 12+ mesajda (önbellek koşulu
> tutmadığı için) her turda tüm eski geçmiş yeniden özetleniyordu. Artık `yeni_özet =
> f(mevcut_özet, pencereden yeni düşen mesajlar)` — `SummarizedMessageCount` hem "geçmişin
> ne kadarı atlanacak" hem "hangi mesajlardan sonrası hâlâ özetlenmemiş" sınırı olarak
> kullanılıyor, LLM'e her turda yalnızca DELTA gönderiliyor (bkz. `FoldAsync`).
>
> Eşzamanlı çift-submit/çoklu-sekme yarışına karşı `IAppDistributedLock` ile
> `session:{sessionId}` anahtarı kilitleniyor — aynı desen `PostgresSessionManager.
> MutateStateAsync`'te de kullanılıyor. Kilit yalnızca gerçekten katlama gerektiğinde
> alınıyor; önbellek isabetinde (özet zaten güncel sınırı kapsıyorsa) kilitsiz, hızlı yoldan
> dönülüyor.
>
> Bilerek yapılmayan: Facts/Narrative ayrımı (yapısal veriyi — müşteri kimliği, sipariş
> no — kayıpsız bir alanda, geri kalanı serbestçe özetlenen bir alanda tutmak). Mevcut tek
> özet metni LLM'e "önemli bilgileri koru" talimatıyla güveniyor; bu ayrım deterministik bir
> garanti verirdi ama ek şema/karmaşıklık gerektirir ve şu an gözlenen bir veri kaybı
> vakası yok — ölçülmeden eklenmedi.

## Kritik mi, iyileştirici mi?

`IContextProvider.IsCritical` (varsayılan `false`) bir provider düştüğünde ne olacağını
belirler — ayrıntı: [ContextPipeline.md](../Chat/ContextPipeline.md#32-kritik--iyileştirici-ayrımı-icontextprovideriscritical).

| Provider | Kritik? | Gerekçe |
|---|:---:|---|
| `ConversationSummaryProvider` | ✗ | Düşerse geçmiş kırpılmaz, tam hâliyle gider (pahalı ama doğru) |
| `SemanticMemoryContextProvider` | ✗ | Bilgi tabanı erişilemese de bot makul cevap verebilir |
| `CustomerProfileContextProvider` | ✗ | Kişiselleştirme kaybı; olgu kaybı değil |
| `ProductRecommendationContextProvider` | ✗ | Zaten *isteğe bağlı* bir blok — susma kuralları servis seviyesinde, kritiklik burada tekrar edilmiyor |

Yeni provider eklerken sorulacak soru: *bu bağlam olmadan model **yanlış bir şey söyler mi**,
yoksa sadece **daha az iyi** mi söyler?* Birincisi kritik, ikincisi iyileştirici.

## Sezgisel koşullu çalıştırma — bilinçli olarak yapılmadı

Her provider'ın **olgu tabanlı** erken çıkışı zaten var (bellek kapalı, kimlik yok, profil
boş, geçmiş kısa). Bunların ötesinde "kısa/önemsiz turlarda pahalı provider'ları atla" gibi
**sezgisel** bir optimizasyon uygulanmadı. Sebep:

- Diğer korumaların aksine bu bir **güvenlik değil, kalite/maliyet takası**. Yanlış sezgi,
  gereken bağlamı sessizce düşürür ve hatanın izi görünmez.
- Karar için gereken veri (`ReasoningTrace.EstimatedTokens` + `ContextParts`) **yeni eklendi**
  ve henüz canlıda birikmedi. Hangi provider'ın gerçekten pahalı olduğunu ölçmeden sezgiyle
  budamak, ölçmeden optimize etmektir.

Karar için bakılacak veri: tur başına `EstimatedTokens` eğrisi ve `ContextParts` içindeki
provider boyutları. Semantik arama gerçekten baskınsa ve düşük değerli turlarda boş dönüyorsa,
o zaman `TurnSignals`/intent ile koşullandırmak veriye dayalı bir karar olur.

## Bağlantılar

- [../Reasoning/ReasoningMessageBuilder.md](../Reasoning/ReasoningMessageBuilder.md) — Provider çıktılarını kullanan builder
