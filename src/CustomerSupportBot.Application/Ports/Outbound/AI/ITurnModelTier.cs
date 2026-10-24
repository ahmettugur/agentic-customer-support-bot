// Ports/Outbound/AI/ITurnModelTier.cs
// Turun model katmanı: ajan takımının LLM çağrıları hangi modele gitsin? Ortam (ambient) bağlam — takım kurulurken değil,
// tur sırasında karar verilir.

namespace CustomerSupportBot.Application.Ports.Outbound.AI;

public enum ModelTier
{
    /// <summary>Varsayılan, en yetenekli model.</summary>
    Standard,

    /// <summary>Basit, yüksek güvenli turlar için daha ucuz/hızlı model.</summary>
    Fast
}

public interface ITurnModelTier
{
    ModelTier Current { get; }

    /// <summary>Kapsam boyunca (ve o kapsamdan başlayan async çağrılarda) katmanı değiştirir; Dispose eskisini geri koyar.</summary>
    IDisposable Begin(ModelTier tier);
}
