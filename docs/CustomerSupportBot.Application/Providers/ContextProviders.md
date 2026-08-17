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
> Ayrıntı ve kalan işler (özellikle **artımlı özetleme** — bugün 12+ mesajda özet her turda
> sıfırdan üretiliyor): [ADR-0002](../../adr/0002-conversation-context-window.md).

## Bağlantılar

- [../Reasoning/ReasoningMessageBuilder.md](../Reasoning/ReasoningMessageBuilder.md) — Provider çıktılarını kullanan builder
