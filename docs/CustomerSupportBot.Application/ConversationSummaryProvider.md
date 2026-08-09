# ConversationSummaryProvider

## Ne İşe Yarar
Uzun konuşma geçmişini LLM ile özetleyerek token tasarrufu sağlayan context provider'dır.

## Hangi Amaçla Kullanılır
`ContextPipeline` içinde çalışır (Order=5). Konuşma 8+ mesaja ulaştığında eski mesajları özetler ve sonraki turlarda özet + son mesajlar bağlam olarak agent'lara verilir.

## Sorumlulukları
- Konuşma geçmişi eşiği aşıyorsa (8 mesaj) LLM ile özetleme yapmak.
- Özetin session state'ine yazılması (tekrar hesaplama önlenir).
- Türkçe, 150 kelime sınırlı, önemli bilgileri koruyan özet üretmek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Implements**: `IContextProvider`.
- **DI ile inject edilen**: `IGeneralChatClient`, `ISessionManager`.
- **Kullanan sınıf**: `ContextPipeline` — paralel provider listesinde.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `GetContextAsync(session, currentQuery)` | Konuşma geçmişi eşik üstündeyse özet üretir; altındaysa `null`. |

## Bağımlılıklar
- `IGeneralChatClient` — Özetleme LLM çağrısı.
- `ISessionManager` — Geçmiş okuma ve session güncelleme.
