// Models/Memory/KnowledgeArticle.cs
// Destek ekibinin panelden yönettiği bilgi tabanı makalesi.
//
// KnowledgeBase/ dizinindeki markdown dosyalarından farkı: bunlar çalışma zamanında
// yazılabilir ve kalıcıdır. Dosyalar build çıktısına kopyalandığı için (bin/) runtime'da
// düzenlenemez — yeniden derlemede kaybolur. Makaleler bu yüzden veritabanında durur;
// vector store bunların türetilmiş indeksidir, kaynak değil.

namespace CustomerSupportBot.Domain.Model.Memory;

/// <summary>
/// Panelden yönetilen bilgi tabanı makalesi. Yayınlandığında chunk'lara bölünüp
/// <see cref="MemoryKind.Knowledge"/> koleksiyonuna indekslenir.
/// </summary>
public sealed class KnowledgeArticle
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>İnsanın okuyacağı başlık; citation'da ve panelde görünür.</summary>
    public string Title { get; set; } = "";

    /// <summary>Markdown gövde. Chunk'lama bunun üzerinden yapılır.</summary>
    public string Content { get; set; } = "";

    /// <summary>Serbest gruplama etiketi (ör. "kargo", "iade"). Filtreleme için.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Yayında değilse indekslenmez — taslak makale ajanların yanıtlarına sızmaz.
    /// Yayından kaldırma, silmeden indeksi temizlemenin yoludur.
    /// </summary>
    public bool IsPublished { get; set; }

    /// <summary>
    /// Bu makalenin vector store'daki chunk sayısı. Güncellemede kısalan makalenin
    /// artık chunk'larını silebilmek için tutulur — yoksa eski metin indekste kalır.
    /// </summary>
    public int IndexedChunkCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Son düzenleyen admin/temsilci kullanıcı adı — denetim izi.</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Vector store doküman id'lerinin deterministik şeması.
    /// Deterministik olması şart: güncellemede aynı chunk'ın üzerine yazılabilmesi,
    /// silmede hangi noktaların kaldırılacağının bilinmesi buna bağlı.
    /// </summary>
    public static string ChunkId(string articleId, int chunkIndex) => $"kb-article:{articleId}:{chunkIndex}";

    /// <summary>Vector store dokümanlarında kaynak alanı — dosya tabanlı chunk'lardan ayırt eder.</summary>
    public string SourceRef => $"article:{Id}";
}
