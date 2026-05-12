// Models/Memory/SemanticMemoryOptions.cs
// appsettings.json > "SemanticMemory" bölümüne bind edilen opsiyonlar.

namespace CustomerSupportBot.Models.Memory;

public sealed class SemanticMemoryOptions
{
    public const string SectionName = "SemanticMemory";

    public bool Enabled { get; set; } = true;
    public QdrantOptions Qdrant { get; set; } = new();
    public EmbeddingOptions Embedding { get; set; } = new();
    public CollectionOptions Collections { get; set; } = new();
    public RetrievalOptions Retrieval { get; set; } = new();
    public KnowledgeBaseOptions KnowledgeBase { get; set; } = new();

    public sealed class QdrantOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 6334;
        public bool UseHttps { get; set; }
        public string? ApiKey { get; set; }
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

public sealed class SelfImprovementOptions
{
    public const string SectionName = "SelfImprovement";

    public bool Enabled { get; set; } = true;
    /// <summary>Bu yıldız sayısının altındaki konuşmalar lesson adayı olur.</summary>
    public int MinRatingForLesson { get; set; } = 3;
    public int RecentTracesToScan { get; set; } = 50;
    public int MiningIntervalHours { get; set; } = 24;
    public bool RequireApprovalBeforeActivation { get; set; } = true;
}
