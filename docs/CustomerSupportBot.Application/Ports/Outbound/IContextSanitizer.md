# IContextSanitizer

**Kaynak:** `Ports/Outbound/IContextSanitizer.cs`
**Implementasyon:** [`ContextSanitizer`](../../Services/Memory/ContextSanitizer.md)

## 1. Ne İşe Yarar

Retrieval'dan (vector store, KB, lesson) gelen ham metni prompt'a girmeden önce güvenli hale
getiren port. Prompt-injection yüzeyini daraltmak için iki katman sunar: yazma tarafında
`Sanitize`, okuma tarafında `WrapRetrieved`.

## 2. Hangi Amaçla Kullanılır

Semantik hafıza/knowledge base'den çekilen herhangi bir metin (episodik anı, KB makalesi, ders)
prompt'a eklenmeden önce bu port'tan geçirilir.

## 3. Sorumlulukları

- **Üstlendiği:** Kontrol karakteri temizliği, HTML comment kaldırma, uzunluk kırpma ve
  içeriği işaretli bir sarmalayıcıya (`<retrieved_data>`) alma.
- **Üstlenmediği:** İçeriğin ANLAMLI olup olmadığı — bu yalnızca güvenlik/format temizliğidir,
  içerik kalitesi kontrolü değildir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Memory/ContextSanitizer` implemente eder; semantic memory context
provider'ları ve KB retrieval akışları tarafından çağrılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Retrieval edilen içerik **güvenilmez veri** kabul edilir — bir müşterinin geçmişte yazdığı bir
mesaj ya da bir KB makalesi, teorik olarak modele "önceki talimatları unut" gibi bir prompt
injection denemesi içerebilir. `WrapRetrieved`'ın kapanış etiketini nötralize etmesi
(case-insensitive), içerikte `</retrieved_data>` geçse bile fence'in kırılamamasını garanti
eder — aksi hâlde kötü niyetli bir metin sarmalayıcıdan "kaçıp" kendini sistem talimatı gibi
gösterebilirdi.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `string Sanitize(string text, int maxLength = 2000)` | Newline dışındaki C0/C1 kontrol karakterlerini siler, HTML comment bloklarını kaldırır, `maxLength` üzerini kırpar. |
| `string WrapRetrieved(string text, string source)` | Önce `Sanitize` uygular, sonra `<retrieved_data source="...">…</retrieved_data>` ile sarmalar; içerikteki kapanış etiketini nötralize eder. |

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
