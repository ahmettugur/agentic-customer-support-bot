# KnowledgeBase Dizini

## Ne İşe Yarar
Startup sırasında vektör veritabanına embed edilecek bilgi tabanı (Knowledge Base) içerik dosyalarını içerir.

## Hangi Amaçla Kullanılır
`KnowledgeBaseIngestor` hosted service'i bu dizindeki dosyaları okuyarak içerikleri chunk'lara böler, embedding vektörleri oluşturur ve Qdrant'a yazar.

## Sorumlulukları
- Ürün bilgileri, SSS, şirket politikaları gibi statik bilgi kaynaklarını barındırmak.
- Startup'ta otomatik indeksleme için kaynak sağlamak.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Okuyan sınıf**: [KnowledgeBaseIngestor](Workers.md) — `IHostedService` olarak startup'ta çalışır.
- **Hedef**: Qdrant vektör veritabanı → `Knowledge` collection.
- **Runtime kullanım**: `SemanticMemoryContextProvider` bu vektörleri sorgulayarak konuşma bağlamına bilgi ekler.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
RAG (Retrieval-Augmented Generation) pattern'i: bilgi tabanı vektörleştirilir, konuşma sırasında semantik arama ile ilgili bilgiler çekilerek LLM'e bağlam olarak verilir.

## Bağımlılıklar
Yok — salt metin/markdown dosyaları.
