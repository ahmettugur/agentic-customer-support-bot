// Application/Ports/Driven/AI/SemanticMemoryOptions.cs
// appsettings.json > "SemanticMemory" bölümüne bind edilen opsiyonlar.
// VectorStore, Embedding, Retrieval ve Collections gibi domain-agnostik konfigürasyonları içerir.

namespace CustomerSupportBot.Application.Ports.Outbound.AI;

public sealed class SemanticMemoryOptions
{
    public const string SectionName = "SemanticMemory";

    public bool Enabled { get; set; } = true;
    public VectorStoreOptions VectorStore { get; set; } = new();
    public EmbeddingOptions Embedding { get; set; } = new();
    public CollectionOptions Collections { get; set; } = new();
    public RetrievalOptions Retrieval { get; set; } = new();
    public KnowledgeBaseOptions KnowledgeBase { get; set; } = new();

    public sealed class VectorStoreOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 6334;
        public bool UseHttps { get; set; }
        public string? ApiKey { get; set; }

        /// <summary>
        /// Embedding boyutu mevcut koleksiyonunkiyle uyuşmadığında koleksiyonun SİLİNİP yeniden
        /// oluşturulmasına izin verir.
        /// </summary>
        ///
        /// <remarks>
        /// Varsayılan <c>false</c> — yani fail-closed. Eskiden bu davranış koşulsuzdu ve
        /// "dev-friendly" diye işaretlenmişti; ortam ayrımı yoktu. Üretimde embedding modelini
        /// değiştirmek (ör. -small → -large) tüm knowledge base'i, dersleri ve episodic belleği
        /// sessizce siliyordu. Daha kötüsü: knowledge base silindikten sonra
        /// <c>KnowledgeBaseIngestionService</c> kaynak hash'i değişmediği için re-ingest'i
        /// atlıyor, sonuç BOŞ bir bilgi tabanı oluyordu. Episodic bellek ve dersler ise
        /// yeniden üretilemez — onlar için geri dönüş yok.
        ///
        /// <para>
        /// Bu bayrak açılmadan boyut uyuşmazlığı bir başlatma hatasıdır: veri kaybı, sessiz bir
        /// yan etki değil bilinçli bir karar olmalıdır.
        /// </para>
        /// </remarks>
        public bool AllowDestructiveDimensionMigration { get; set; }
    }

    public sealed class EmbeddingOptions
    {
        public string Model { get; set; } = "text-embedding-3-small";
        /// <summary>Vektör boyutu (text-embedding-3-small = 1536, -large = 3072).</summary>
        public int Dimension { get; set; } = 1536;
    }

    public sealed class CollectionOptions
    {
        public string Episodic { get; set; } = "cs_episodic";
        public string Lessons { get; set; } = "cs_lessons";
        public string Knowledge { get; set; } = "cs_knowledge";
    }

    public sealed class RetrievalOptions
    {
        public int TopK { get; set; } = 4;
        public float MinScore { get; set; } = 0.35f;
        public int MaxContextChars { get; set; } = 1800;
    }

    public sealed class KnowledgeBaseOptions
    {
        public bool AutoIngestOnStartup { get; set; } = true;
        public int ChunkSize { get; set; } = 800;
        public int ChunkOverlap { get; set; } = 100;
    }
}

/// <summary>
/// Self-Improving Loop ayarları.
///
/// <para>
/// <b>Tarama yalnızca MANUELDİR</b> — admin panelindeki "Yeni Tarama Çalıştır" düğmesi
/// (<c>POST /improvements/mine</c>) dışında tetikleyen bir şey yoktur. Zamanlanmış bir
/// background service bulunmuyor; bu bilinçli bir tercih, çünkü otomatik tarama hem LLM
/// maliyeti üretir hem de kimsenin bakmadığı bir onay kuyruğu biriktirir.
/// </para>
///
/// <para>
/// NOT: Burada eskiden bir <c>MiningIntervalHours = 24</c> ayarı vardı ama onu okuyan
/// hiçbir kod yoktu — "günde bir otomatik taranır" izlenimi veren ölü bir ayardı.
/// Zamanlanmış tarama istenirse önce bir hosted service yazılmalı, ayar ondan sonra
/// geri eklenmelidir.
/// </para>
/// </summary>
public sealed class SelfImprovementOptions
{
    public const string SectionName = "SelfImprovement";

    public bool Enabled { get; set; } = true;
    /// <summary>Bu yıldız sayısının altındaki konuşmalar lesson adayı olur.</summary>
    public int MinRatingForLesson { get; set; } = 3;
    public int RecentTracesToScan { get; set; } = 50;
    public bool RequireApprovalBeforeActivation { get; set; } = true;
}

