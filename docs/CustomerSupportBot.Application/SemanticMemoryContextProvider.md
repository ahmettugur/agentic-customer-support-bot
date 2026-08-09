# SemanticMemoryContextProvider

## Ne İşe Yarar
Kullanıcının sorusunu Knowledge ve Lessons vektör koleksiyonlarında arayarak en ilgili chunk'ları agent bağlamına enjekte eden context provider'dır.

## Hangi Amaçla Kullanılır
`ContextPipeline` içinde çalışır (Order=7). RAG pattern'inin retrieval adımını gerçekleştirir.

## Sorumlulukları
- Kullanıcının mevcut sorusunu tek seferde embed etmek (iki koleksiyon için aynı vektör).
- Knowledge ve Lessons koleksiyonlarını paralel aramak.
- Sonuçları `ContextSanitizer` ile temizleyip `<retrieved_data>` fence'i ile sararak prompt injection'a karşı korumak.
- Bütçe sınırı (MaxContextChars) ile toplam context boyutunu kontrol etmek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Implements**: `IContextProvider`.
- **DI ile inject edilen**: `SemanticMemoryService`, `IContextSanitizer`.
- **Kullanan sınıf**: `ContextPipeline`.
- **Önemli optimizasyon**: Eskiden iki ayrı `SearchAsync` çağrısı aynı metni yeniden embed ediyordu — şimdi sorgu bir kez embed edilir ve `SearchByVectorAsync` ile aranır.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `GetContextAsync(session, currentQuery)` | Semantic arama yapıp ilgili KB/Lesson chunk'larını bağlam olarak döner. |

## Bağımlılıklar
- `SemanticMemoryService` — Embedding ve vector search.
- `IContextSanitizer` — Prompt injection koruması.
