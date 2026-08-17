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
| `CustomerContextProvider` | Müşterinin sipariş/şikayet geçmişi. Sipariş satırları `OrderInfo.LinesSummary()` ile tek satıra indirilir — `1082: Kahve x2, Çikolata x1, Durum: İşleniyor, Tarih: …` |
| `CustomerIdentityHintBuilder` | AuthenticatedCustomerId → prompt hint |
| `CustomerProfileContextProvider` | Müşteri profili (tercihler, iletişim stili) |
| `SemanticMemoryContextProvider` | Geçmiş konuşmalardan semantic search |
| `NoopContextProvider` | Hiçbir şey yapmaz (test/disable için) |

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
> Bu düzeltme maliyetin yalnızca yarısını çözer: özet hâlâ **sıfırdan** üretiliyor — 12+
> mesajda (`history.Count - SummaryThreshold < RecentMessageCount` önbellek koşulu tutmadığı
> için) her turda tüm eski geçmiş yeniden özetleniyor. Artımlı özetleme
> (`yeni_özet = f(mevcut_özet, pencereden düşen mesajlar)`) hâlâ açık bir iş.

## Kritik mi, iyileştirici mi?

`IContextProvider.IsCritical` (varsayılan `false`) bir provider düştüğünde ne olacağını
belirler — ayrıntı: [ContextPipeline.md](../Chat/ContextPipeline.md#32-kritik--iyileştirici-ayrımı-icontextprovideriscritical).

| Provider | Kritik? | Gerekçe |
|---|:---:|---|
| `CustomerContextProvider` | ✅ | Düşerse model "siparişiniz bulunamadı" der — altyapı hatası **yanlış olguya** dönüşür |
| `ConversationSummaryProvider` | ✗ | Düşerse geçmiş kırpılmaz, tam hâliyle gider (pahalı ama doğru) |
| `SemanticMemoryContextProvider` | ✗ | Bilgi tabanı erişilemese de bot makul cevap verebilir |
| `CustomerProfileContextProvider` | ✗ | Kişiselleştirme kaybı; olgu kaybı değil |

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
