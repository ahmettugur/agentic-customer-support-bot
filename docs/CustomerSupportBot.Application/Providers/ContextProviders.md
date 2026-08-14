# IContextProvider & Provider Zinciri

**Dosya:** `Services/Providers/IContextProvider.cs` ve implementasyonları

## 1. Ne İşe Yarar

Reasoning/agent prompt'una enjekte edilecek bağlam parçalarını üreten provider zinciridir. Her provider farklı bir kaynaktan (geçmiş özeti, müşteri profili, semantic memory, customer identity) bağlam çeker.

## 2. Neden Gerekli

> 💡 **Analiz notu:** Bir doktor vizitesinde hemşirenin hazırladığı "hasta özet dosyası" gibi — alerji bilgisi, geçmiş tedaviler, son tahliller hepsi farklı yerlerden gelir ama tek bir dosyaya birleştirilir.

## 3. Provider'lar

| Provider | Açıklama |
| ---------- | ---------- |
| `ConversationSummaryProvider` | Son N turdan özet metin oluşturur |
| `CustomerContextProvider` | Müşterinin sipariş/şikayet geçmişi. Sipariş satırları `OrderInfo.LinesSummary()` ile tek satıra indirilir — `1082: Kahve x2, Çikolata x1, Durum: İşleniyor, Tarih: …` |
| `CustomerIdentityHintBuilder` | AuthenticatedCustomerId → prompt hint |
| `CustomerProfileContextProvider` | Müşteri profili (tercihler, iletişim stili) |
| `SemanticMemoryContextProvider` | Geçmiş konuşmalardan semantic search |
| `NoopContextProvider` | Hiçbir şey yapmaz (test/disable için) |

## Bağlantılar

- [../Reasoning/ReasoningMessageBuilder.md](../Reasoning/ReasoningMessageBuilder.md) — Provider çıktılarını kullanan builder
