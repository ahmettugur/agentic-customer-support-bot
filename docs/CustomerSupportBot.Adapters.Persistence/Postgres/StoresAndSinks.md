# PostgreSQL Gözlemlenebilirlik, İyileştirme ve Bilgi Depoları

- **Kaynaklar:**
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresKnowledgeArticleStore.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresLessonStore.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresReasoningTraceStore.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresLlmCallUsageSink.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresRatingStore.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresSlaEventSink.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresCustomerProfileStore.cs`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`

## 1. PostgresReasoningTraceStore & PostgresLlmCallUsageSink
- **Uyguladığı Portlar:** [IReasoningTraceStore](../../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md), [ILlmCallPersistencePort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ILlmCallPersistencePort.md)
- **Çalışma Mantığı:**
  - `StartTrace`: `observability.reasoning_traces` tablosuna yeni trace kaydı açar.
  - `Update`: Canlı akış sırasında Reasoning, Planning ve ToolCalls JSON alanlarını günceller.
  - `Complete`: Final yanıtı ve bitiş zamanını yazar.
  - `RecordAsync`: Her LLM çağrısının model, provider, token sayıları, gecikme ve maliyetini `observability.llm_call_usages` tablosuna yazar.

---

## 2. PostgresKnowledgeArticleStore
- **Uyguladığı Port:** [IKnowledgeArticleStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IKnowledgeArticleStore.md)
- **Çalışma Mantığı:** `knowledge.knowledge_articles` tablosunda şirket makalelerinin başlık, içerik, etiket ve kategori bilgilerini yönetir; anlamsal vektör aramasından dönen dokümanların meta verilerini eşleştirir.

---

## 3. PostgresLessonStore
- **Uyguladığı Port:** [ILessonStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ILessonStore.md)
- **Çalışma Mantığı:** Başarısız veya düzeltilen turlardan çıkarılan iyileştirme kurallarını (`improvement.lessons`) kaydeder ve gelecekteki benzer sorgularda prompt'a beslemek üzere listeler.

---

## 4. PostgresCustomerProfileStore
- **Uyguladığı Port:** [ICustomerProfileStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerProfileStore.md)
- **Çalışma Mantığı:** Müşterinin etkileşim sayısını, son tercih ettiği iletişim tonunu ve geçmiş konuşma özetlerini `personalization.customer_profiles` tablosunda saklar.

---

## 5. PostgresRatingStore & PostgresSlaEventSink
- **Uyguladığı Portlar:** [IRatingStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IRatingStore.md), [ISlaEventSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISlaEventSink.md)
- **Çalışma Mantığı:** Müşteri memnuniyet anketlerini (`1-5 yıldız`, yorum) ve yanıt gecikmesi, eskalasyon süresi gibi SLA olaylarını analitik tablolarına yazar.
