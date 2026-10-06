// Temsilci–müşteri sesli görüşmesi — appsettings "VoiceCall" bölümü.

namespace CustomerSupportBot.Application.Ports.Outbound;

public sealed class VoiceCallOptions
{
    public const string SectionName = "VoiceCall";

    public bool Enabled { get; set; } = true;

    /// <summary>Müşteri bu sürede yanıt vermezse görüşme cevapsız (Missed) kapanır.</summary>
    public int RingTimeoutSeconds { get; set; } = 45;

    /// <summary>Aktif görüşmede bu süre parça gelmezse (temsilci sekmesi kapandı) görüşme başarısız kapanır.</summary>
    public int ChunkStaleSeconds { get; set; } = 60;

    public int MaxChunkBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Görüşme bittikten sonra son parçaların kabul edildiği süre.</summary>
    public int LateChunkGraceSeconds { get; set; } = 120;

    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string TranscriptionLanguage { get; set; } = "tr";
    public int TranscriptionMaxAttempts { get; set; } = 3;

    public List<string> StunUrls { get; set; } = ["stun:stun.l.google.com:19302"];
    public TurnOptions Turn { get; set; } = new();

    public sealed class TurnOptions
    {
        /// <summary>ör. <c>turn:localhost:3478?transport=udp</c>, <c>turn:localhost:3478?transport=tcp</c>. Boşsa yalnızca STUN.</summary>
        public List<string> Urls { get; set; } = [];

        /// <summary>coturn <c>static-auth-secret</c> ile aynı. Tarayıcıya gönderilmez.</summary>
        public string? SharedSecret { get; set; }

        public int CredentialTtlMinutes { get; set; } = 10;
    }
}
