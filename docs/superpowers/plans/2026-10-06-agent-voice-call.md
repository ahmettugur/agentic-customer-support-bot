# Temsilci ile Sesli Görüşme (Kayıt + Döküm) — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Temsilci, devraldığı canlı sohbette müşteriyle WebRTC üzerinden gerçek zamanlı sesli görüşür; görüşme temsilci tarayıcısında iki izli kaydedilir, 10 sn'lik parçalar halinde yüklenir, görüşme sırasında yazıya dökülüp konuşma geçmişine girer.

**Architecture:** Ses tarayıcılar arasında P2P (WebRTC, TURN = coturn). Sunucu yalnızca sinyal mesajlarını mevcut canlı sohbet köprüsünden (`IChatBridge`, SSE + Redis pub/sub) yönlü ve kalıcılaştırmadan iletir. Görüşme durumu `voice_calls` tablosunda; temsilci başına tek açık görüşme Postgres unique partial index ile garanti. Kayıt parçaları `voice_recording_chunks` tablosunda; bir arka plan işi parçaları sahiplenip OpenAI ses dökümüyle yazıya döker ve köprüye "yalnız temsilci" mesajı olarak yazar.

**Tech Stack:** .NET 10, ASP.NET Core minimal API, EF Core (Npgsql), Blazor WebAssembly, xUnit v3 (MTP) + FluentAssertions + NSubstitute + Testcontainers, OpenAI .NET SDK (`AudioClient`), WebRTC / MediaRecorder (tarayıcı), coturn.

**Spec:** `docs/superpowers/specs/2026-10-06-agent-voice-call-design.md`

## Global Constraints

- Dal: `feat/agent-voice-call`. Push yok; her görev kendi commit'iyle biter. Commit mesajı sonu: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Ses taşıma WebRTC P2P; sunucu sesi taşımaz.
- Kaydı **temsilci tarayıcısı** yapar: yerel mikrofon (`agent`) ve uzak müşteri sesi (`customer`), ayrı iki iz.
- Parça süresi **10 sn**; her parça başlıklı tam bir webm (`audio/webm;codecs=opus`) — kaydedici her 10 sn'de yeniden başlatılır. Parça başına en fazla **2 MB**.
- Rıza **zorunlu**: `ConsentAt` olmadan `Active` yok. Ret → yazılı devam.
- Çalma zaman aşımı **45 sn** → `Missed`. Aktif görüşmede **60 sn** parça gelmezse → `Failed` (`connection_lost`).
- Temsilci aynı anda **en fazla bir** `Ringing`/`Active` görüşme; oturum başına da tek açık görüşme.
- Ses saklama **90 gün** (`DataRetention:VoiceRecordingRetentionDays`); döküm metni konuşmayla kalır.
- Döküm: `VoiceCall:TranscriptionModel` (varsayılan `gpt-4o-transcribe`), dil `tr`, en fazla **3** deneme.
- TURN kimliği: coturn `use-auth-secret`; kullanıcı `"{sonGeçerlilikUnix}:{callId}"`, parola `Base64(HMAC-SHA1(sır, kullanıcı))`, ömür **10 dk**. Kalıcı sır tarayıcıya gitmez.
- Docker: `coturn/coturn:latest`, `container_name: aibot_coturn`; yığın `docker compose -p aibot -f deploy/docker-compose.yml up -d`. Dev parolası hassas değil.
- UI metinleri Türkçe; emoji yok, ikonlar `Icon` bileşeninden; renkler tema değişkenlerinden.
- xUnit async çağrılarına `TestContext.Current.CancellationToken` geçirilir.

## Review Focus

1. **Aynı anda iki başlatma (çift tıklama / iki sekme / iki pod)** — yalnızca biri `Ringing` olur, diğeri 409 `voice_call_busy`. → Task 3 (Postgres eşzamanlı test) + Task 5 (servis).
2. **Görüşmenin tarafı olmayan biri sinyal/parça/kabul gönderir** (başka temsilci, başka müşteri) — 403, durum değişmez. → Task 5 + Task 6.
3. **Aynı parça iki kez yüklenir (ağ tekrarı)** — ikinci yükleme yok sayılır, döküm satırı bir kez yazılır. → Task 3 + Task 7.
4. **Rıza verilmeden `Active`'e geçiş denemesi / zaten bitmiş görüşmeye kabul** — reddedilir (409 `invalid_state`). → Task 1 + Task 6.
5. **Temsilci sekmesini kapatır (hangup gelmez)** — 60 sn sonra görüşme `Failed`, temsilci tekrar arama başlatabilir. → Task 7 (süpürme testi).

---

## Dosya Haritası

**Domain** (`src/CustomerSupportBot.Domain/Model/Voice/`)
- `VoiceCall.cs` — görüşme varlığı + durum geçişleri.
- `VoiceRecordingChunk.cs` — kayıt parçası + döküm durumu.

**Application**
- `Ports/Outbound/Persistence/IVoiceCallStore.cs`, `IVoiceRecordingStore.cs`
- `Ports/Outbound/AI/IAudioTranscriber.cs`
- `Ports/Outbound/VoiceCallOptions.cs`
- `Ports/Inbound/IVoiceCallPort.cs` — servis sözleşmesi + sonuç/görünüm kayıtları.
- `Services/Voice/VoiceCallService.cs` — başlat/kabul/ret/kapat/sinyal/parça/görüntüle/süpür.
- `Services/Voice/TurnCredentialFactory.cs` — TURN kısa ömürlü kimlik.
- `Services/Voice/VoiceTranscriptionProcessor.cs` — tek parça döküm adımı (worker çağırır).
- `Ports/Outbound/Persistence/IChatBridge.cs` — `PublishVoiceSignal`, `PublishVoiceTranscriptAsync` eklenir.

**Persistence** (`src/CustomerSupportBot.Adapters.Persistence/`)
- `EfCore/Entities/Voice/VoiceCallEntity.cs`, `VoiceRecordingChunkEntity.cs`
- `EfCore/Configurations/Voice/VoiceCallConfiguration.cs`, `VoiceRecordingChunkConfiguration.cs`
- `EfCore/Schemas.cs` — `Voice = "voice"`.
- `Postgres/PostgresVoiceCallStore.cs`, `PostgresVoiceRecordingStore.cs`
- `InMemory/InMemoryVoiceCallStore.cs`, `InMemoryVoiceRecordingStore.cs` (API testleri için)
- `Postgres/PostgresChatBridge.cs`, `InMemory/InMemoryChatBridge.cs` — sinyal + döküm mesajı.
- `EfCore/Entities/Chat/ChatBridgeMessageEntity.cs` + config — meta sütunları.
- Migration: `AddVoiceCalls`.

**AI** — `src/CustomerSupportBot.Adapters.AI/Audio/OpenAiAudioTranscriber.cs`

**Api**
- `Endpoints/VoiceCallEndpoints.cs` — personel + müşteri uçları.
- `Workers/VoiceCallWorker.cs` — döküm kuyruğu + zaman aşımı süpürmesi.
- `Services/ChatEventOrchestrator.cs`, `Endpoints/AgentPanelEndpoints.cs`, `Endpoints/AdminEndpoints.cs` — SSE `voice_signal` olayı + meta alanları.

**Web**
- `wwwroot/js/agent-voice-call.js` — WebRTC, sinyal, kayıt/yükleme, oynatıcı.
- `Components/StaffVoiceCallBar.razor` — temsilci tarafı arama çubuğu.
- `Components/CustomerVoiceCallCard.razor` — müşteri tarafı gelen arama + görüşme kartı.
- `Components/VoiceCallPlayer.razor` — iki izli dinleme.
- `Services/AdminApiService.cs`, `Services/ChatApiService.cs`, `Models/AdminModels.cs` — istemci metotları/modeller.
- `Pages/Admin.razor(.cs)`, `Pages/Chat.razor`, `wwwroot/js/admin-chat-bridge.js`, `wwwroot/js/chat-bridge.js`.

**Deploy/Docs** — `deploy/docker-compose.yml`, `deploy/turnserver.conf`, `docs/deployment.md`, `docs/CustomerSupportBot.*` belgeleri.

---

### Task 1: Domain — VoiceCall ve VoiceRecordingChunk

**Files:**
- Create: `src/CustomerSupportBot.Domain/Model/Voice/VoiceCall.cs`
- Create: `src/CustomerSupportBot.Domain/Model/Voice/VoiceRecordingChunk.cs`
- Test: `tests/CustomerSupportBot.Domain.Tests/VoiceCallTests.cs`

**Interfaces:**
- Produces: `VoiceCall`, `VoiceCallStatus`, `VoiceCallEndReasons`, `VoiceCallStateException`, `VoiceTrack`, `VoiceRecordingChunk`, `VoiceTranscriptStatus` (namespace `CustomerSupportBot.Domain.Model.Voice`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CustomerSupportBot.Domain.Tests/VoiceCallTests.cs
// Sesli görüşme durum makinesi: rıza olmadan görüşme olmaz, bitmiş görüşme yeniden açılmaz.

using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Domain.Tests;

public class VoiceCallTests
{
    private static readonly DateTime T0 = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static VoiceCall Ringing() => VoiceCall.Start("s1", "agent-1", "Elif", T0);

    [Fact]
    public void Start_IsRinging_AndOpen()
    {
        var call = Ringing();
        call.Status.Should().Be(VoiceCallStatus.Ringing);
        call.IsOpen.Should().BeTrue();
        call.ConsentAt.Should().BeNull();
    }

    [Fact]
    public void Accept_RecordsConsent_AndActivates()
    {
        var call = Ringing();
        call.Accept(T0.AddSeconds(5));
        call.Status.Should().Be(VoiceCallStatus.Active);
        call.ConsentAt.Should().Be(T0.AddSeconds(5));
        call.AnsweredAt.Should().Be(T0.AddSeconds(5));
    }

    [Fact]
    public void Accept_AfterEnd_Throws()
    {
        var call = Ringing();
        call.Decline(T0.AddSeconds(2), VoiceCallEndReasons.Declined);
        var act = () => call.Accept(T0.AddSeconds(3));
        act.Should().Throw<VoiceCallStateException>();
    }

    [Fact]
    public void Hangup_WhileRinging_ByAgent_IsCancelled()
    {
        var call = Ringing();
        call.Hangup(T0.AddSeconds(3), VoiceCallEndReasons.AgentHangup);
        call.Status.Should().Be(VoiceCallStatus.Cancelled);
        call.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Hangup_WhileActive_IsEnded_WithDuration()
    {
        var call = Ringing();
        call.Accept(T0.AddSeconds(5));
        call.Hangup(T0.AddSeconds(65), VoiceCallEndReasons.CustomerHangup);
        call.Status.Should().Be(VoiceCallStatus.Ended);
        call.Duration.Should().Be(TimeSpan.FromSeconds(60));
        call.EndReason.Should().Be(VoiceCallEndReasons.CustomerHangup);
    }

    [Theory]
    [InlineData(VoiceCallEndReasons.ConnectionLost)]
    [InlineData(VoiceCallEndReasons.ConnectFailed)]
    public void Hangup_WithConnectionProblem_IsFailed(string reason)
    {
        var call = Ringing();
        call.Accept(T0.AddSeconds(5));
        call.Hangup(T0.AddSeconds(30), reason);
        call.Status.Should().Be(VoiceCallStatus.Failed);
    }

    [Fact]
    public void Miss_OnlyFromRinging()
    {
        var call = Ringing();
        call.Miss(T0.AddSeconds(45));
        call.Status.Should().Be(VoiceCallStatus.Missed);
        call.EndReason.Should().Be(VoiceCallEndReasons.Missed);

        var active = Ringing();
        active.Accept(T0.AddSeconds(1));
        var act = () => active.Miss(T0.AddSeconds(45));
        act.Should().Throw<VoiceCallStateException>();
    }

    [Fact]
    public void Hangup_Twice_IsNoOp()
    {
        var call = Ringing();
        call.Accept(T0.AddSeconds(1));
        call.Hangup(T0.AddSeconds(10), VoiceCallEndReasons.AgentHangup);
        call.Hangup(T0.AddSeconds(20), VoiceCallEndReasons.CustomerHangup);
        call.EndedAt.Should().Be(T0.AddSeconds(10));
        call.EndReason.Should().Be(VoiceCallEndReasons.AgentHangup);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Domain.Tests -- --filter-class "*VoiceCallTests"`
Expected: FAIL (derleme hatası — `VoiceCall` tanımlı değil).

- [ ] **Step 3: Write the implementation**

```csharp
// src/CustomerSupportBot.Domain/Model/Voice/VoiceCall.cs
// Temsilci ile müşteri arasındaki sesli görüşme. Ses tarayıcılar arasında akar (WebRTC); bu kayıt
// görüşmenin durumunu, rızasını ve zamanlarını tutar.

namespace CustomerSupportBot.Domain.Model.Voice;

public enum VoiceCallStatus { Ringing, Active, Ended, Declined, Missed, Cancelled, Failed }

public static class VoiceCallEndReasons
{
    public const string AgentHangup    = "agent_hangup";
    public const string CustomerHangup = "customer_hangup";
    public const string ConnectionLost = "connection_lost";
    public const string ConnectFailed  = "connect_failed";
    public const string Declined       = "declined";
    public const string NoMicrophone   = "no_microphone";
    public const string Missed         = "missed";
}

public sealed class VoiceCallStateException(string message) : InvalidOperationException(message);

/// <summary>
/// Durumlar: <c>Ringing → Active → Ended|Failed</c>; <c>Ringing → Declined|Missed|Cancelled</c>.
/// <see cref="ConsentAt"/> (müşterinin kayıt rızası) olmadan <c>Active</c> olunamaz — kabul rızadır.
/// Bitmiş görüşmede yeniden kapatma çağrısı yok sayılır (iki taraf aynı anda kapatabilir).
/// </summary>
public sealed class VoiceCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string AgentDisplayName { get; set; } = "";
    public VoiceCallStatus Status { get; set; } = VoiceCallStatus.Ringing;
    public DateTime CreatedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? ConsentAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }

    /// <summary>Son kayıt parçasının yüklendiği an — sekmesi kapanan temsilciyi yakalamak için.</summary>
    public DateTime? LastChunkAt { get; set; }

    public bool IsOpen => Status is VoiceCallStatus.Ringing or VoiceCallStatus.Active;

    public TimeSpan? Duration => AnsweredAt is { } a && EndedAt is { } e ? e - a : null;

    public static VoiceCall Start(string sessionId, string agentId, string agentDisplayName, DateTime now) => new()
    {
        SessionId = sessionId,
        AgentId = agentId,
        AgentDisplayName = agentDisplayName,
        Status = VoiceCallStatus.Ringing,
        CreatedAt = now
    };

    public void Accept(DateTime now)
    {
        Require(VoiceCallStatus.Ringing, "kabul");
        ConsentAt = now;
        AnsweredAt = now;
        Status = VoiceCallStatus.Active;
    }

    public void Decline(DateTime now, string reason)
    {
        Require(VoiceCallStatus.Ringing, "ret");
        Close(now, VoiceCallStatus.Declined, reason);
    }

    public void Miss(DateTime now)
    {
        Require(VoiceCallStatus.Ringing, "cevapsız");
        Close(now, VoiceCallStatus.Missed, VoiceCallEndReasons.Missed);
    }

    /// <summary>Her iki taraf da kapatabilir. Çalarken kapatmak iptaldir; bağlantı sorunu başarısızlıktır.</summary>
    public void Hangup(DateTime now, string reason)
    {
        if (!IsOpen) return;
        var status = Status == VoiceCallStatus.Ringing
            ? VoiceCallStatus.Cancelled
            : reason is VoiceCallEndReasons.ConnectionLost or VoiceCallEndReasons.ConnectFailed
                ? VoiceCallStatus.Failed
                : VoiceCallStatus.Ended;
        Close(now, status, reason);
    }

    private void Close(DateTime now, VoiceCallStatus status, string reason)
    {
        Status = status;
        EndedAt = now;
        EndReason = reason;
    }

    private void Require(VoiceCallStatus expected, string action)
    {
        if (Status != expected)
            throw new VoiceCallStateException($"Görüşme {Status} durumunda; {action} yapılamaz.");
    }
}
```

```csharp
// src/CustomerSupportBot.Domain/Model/Voice/VoiceRecordingChunk.cs
// Temsilci tarayıcısının yüklediği 10 sn'lik kayıt parçası (tek başına çözülebilir webm/opus).

namespace CustomerSupportBot.Domain.Model.Voice;

public enum VoiceTrack { Agent, Customer }

public enum VoiceTranscriptStatus { Pending, Processing, Done, Failed }

public sealed class VoiceRecordingChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CallId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public VoiceTrack Track { get; set; }

    /// <summary>İz içindeki sıra (0'dan). (CallId, Track, Sequence) benzersizdir.</summary>
    public int Sequence { get; set; }

    /// <summary>Parçanın görüşme başına göre başlangıcı (ms) — oynatıcıda "o ana atla" için.</summary>
    public int OffsetMs { get; set; }

    public int DurationMs { get; set; }
    public string ContentType { get; set; } = "audio/webm";

    /// <summary>Ses verisi. Listelemelerde ve saklama süresi dolduktan sonra boştur.</summary>
    public byte[] Data { get; set; } = [];

    public VoiceTranscriptStatus TranscriptStatus { get; set; } = VoiceTranscriptStatus.Pending;
    public string? TranscriptText { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? AudioPurgedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Domain.Tests -- --filter-class "*VoiceCallTests"`
Expected: PASS (8 test).

- [ ] **Step 5: Commit**

```bash
git add src/CustomerSupportBot.Domain/Model/Voice tests/CustomerSupportBot.Domain.Tests/VoiceCallTests.cs
git commit -m "feat(voice-call): domain model for agent voice calls"
```

---

### Task 2: Port'lar, seçenekler ve bellek içi depolar

**Files:**
- Create: `src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IVoiceCallStore.cs`
- Create: `src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IVoiceRecordingStore.cs`
- Create: `src/CustomerSupportBot.Application/Ports/Outbound/AI/IAudioTranscriber.cs`
- Create: `src/CustomerSupportBot.Application/Ports/Outbound/VoiceCallOptions.cs`
- Modify: `src/CustomerSupportBot.Application/Ports/Outbound/DataRetentionOptions.cs`
- Create: `src/CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryVoiceCallStore.cs`
- Create: `src/CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryVoiceRecordingStore.cs`
- Test: `tests/CustomerSupportBot.Adapters.Persistence.Tests/InMemoryVoiceStoresTests.cs`

**Interfaces:**
- Consumes: Task 1 tipleri.
- Produces:
  - `IVoiceCallStore.TryCreateAsync(VoiceCall, CancellationToken) : Task<bool>` — temsilcinin ya da oturumun açık görüşmesi varsa `false`.
  - `IVoiceCallStore.GetAsync(string id, CancellationToken) : Task<VoiceCall?>`
  - `IVoiceCallStore.GetOpenForAgentAsync(string agentId, CancellationToken) : Task<VoiceCall?>`
  - `IVoiceCallStore.ListOpenAsync(CancellationToken) : Task<IReadOnlyList<VoiceCall>>`
  - `IVoiceCallStore.ListForSessionAsync(string sessionId, CancellationToken) : Task<IReadOnlyList<VoiceCall>>`
  - `IVoiceCallStore.TryUpdateAsync(VoiceCall call, VoiceCallStatus expectedStatus, CancellationToken) : Task<bool>` — kalıcı durum `expectedStatus` değilse `false` (yarış).
  - `IVoiceCallStore.TouchChunkAsync(string callId, DateTime at, CancellationToken) : Task`
  - `IVoiceRecordingStore.TryAddAsync(VoiceRecordingChunk, CancellationToken) : Task<bool>` — aynı (CallId, Track, Sequence) varsa `false`.
  - `IVoiceRecordingStore.TryClaimNextPendingAsync(DateTime now, CancellationToken) : Task<VoiceRecordingChunk?>` — `Pending` ve `NextAttemptAt <= now` olan en eski parçayı `Processing` yapıp **verisiyle** döner; 5 dk'dan eski `Processing` da yeniden alınabilir.
  - `IVoiceRecordingStore.CompleteAsync(string chunkId, string? text, CancellationToken) : Task`
  - `IVoiceRecordingStore.FailAttemptAsync(string chunkId, DateTime nextAttemptAt, bool final, CancellationToken) : Task`
  - `IVoiceRecordingStore.PostponeAsync(string chunkId, DateTime nextAttemptAt, CancellationToken) : Task` — deneme sayısını artırmadan `Pending`'e döndürür (bütçe).
  - `IVoiceRecordingStore.ListMetaAsync(string callId, CancellationToken) : Task<IReadOnlyList<VoiceRecordingChunk>>` — veri olmadan, `OffsetMs`, `Track` sıralı.
  - `IVoiceRecordingStore.GetAsync(string chunkId, CancellationToken) : Task<VoiceRecordingChunk?>` — verisiyle.
  - `IVoiceRecordingStore.PurgeAudioCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken) : Task<int>` — veriyi boşaltır, `AudioPurgedAt` yazar; döküm metni kalır.
  - `IAudioTranscriber.TranscribeAsync(byte[] audio, string contentType, CancellationToken) : Task<string>`
  - `VoiceCallOptions` (bölüm `VoiceCall`), `DataRetentionOptions.VoiceRecordingRetentionDays`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CustomerSupportBot.Adapters.Persistence.Tests/InMemoryVoiceStoresTests.cs
// Bellek içi ikizler Postgres sözleşmesiyle aynı kuralları uygulamalı (API testleri bunları kullanır).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryVoiceStoresTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime T0 = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CallStore_AllowsOneOpenCallPerAgent_AndPerSession()
    {
        var store = new InMemoryVoiceCallStore();
        (await store.TryCreateAsync(VoiceCall.Start("s1", "a1", "Elif", T0), Ct)).Should().BeTrue();
        (await store.TryCreateAsync(VoiceCall.Start("s2", "a1", "Elif", T0), Ct)).Should().BeFalse("temsilci zaten görüşmede");
        (await store.TryCreateAsync(VoiceCall.Start("s1", "a2", "Can", T0), Ct)).Should().BeFalse("oturumda açık görüşme var");
    }

    [Fact]
    public async Task CallStore_AfterEnd_AgentCanCallAgain()
    {
        var store = new InMemoryVoiceCallStore();
        var call = VoiceCall.Start("s1", "a1", "Elif", T0);
        await store.TryCreateAsync(call, Ct);
        call.Hangup(T0.AddSeconds(1), VoiceCallEndReasons.AgentHangup);
        (await store.TryUpdateAsync(call, VoiceCallStatus.Ringing, Ct)).Should().BeTrue();
        (await store.TryCreateAsync(VoiceCall.Start("s2", "a1", "Elif", T0.AddSeconds(2)), Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task CallStore_TryUpdate_RejectsStaleExpectedStatus()
    {
        var store = new InMemoryVoiceCallStore();
        var call = VoiceCall.Start("s1", "a1", "Elif", T0);
        await store.TryCreateAsync(call, Ct);
        var copy = (await store.GetAsync(call.Id, Ct))!;
        call.Accept(T0.AddSeconds(1));
        (await store.TryUpdateAsync(call, VoiceCallStatus.Ringing, Ct)).Should().BeTrue();
        copy.Hangup(T0.AddSeconds(2), VoiceCallEndReasons.AgentHangup);
        (await store.TryUpdateAsync(copy, VoiceCallStatus.Ringing, Ct)).Should().BeFalse("kalıcı durum artık Active");
    }

    [Fact]
    public async Task RecordingStore_IgnoresDuplicateChunk_AndClaimsOnce()
    {
        var store = new InMemoryVoiceRecordingStore();
        var chunk = new VoiceRecordingChunk { CallId = "c1", SessionId = "s1", Track = VoiceTrack.Customer, Sequence = 0, Data = [1, 2], CreatedAt = T0, NextAttemptAt = T0 };
        (await store.TryAddAsync(chunk, Ct)).Should().BeTrue();
        (await store.TryAddAsync(new VoiceRecordingChunk { CallId = "c1", Track = VoiceTrack.Customer, Sequence = 0, Data = [9], CreatedAt = T0, NextAttemptAt = T0 }, Ct)).Should().BeFalse();

        var claimed = await store.TryClaimNextPendingAsync(T0, Ct);
        claimed!.Data.Should().Equal(1, 2);
        (await store.TryClaimNextPendingAsync(T0, Ct)).Should().BeNull("zaten Processing");
    }

    [Fact]
    public async Task RecordingStore_Purge_KeepsTranscript()
    {
        var store = new InMemoryVoiceRecordingStore();
        var chunk = new VoiceRecordingChunk { CallId = "c1", SessionId = "s1", Track = VoiceTrack.Agent, Sequence = 0, Data = [1], CreatedAt = T0, NextAttemptAt = T0 };
        await store.TryAddAsync(chunk, Ct);
        await store.CompleteAsync(chunk.Id, "Merhaba", Ct);

        (await store.PurgeAudioCreatedBeforeAsync(T0.AddDays(1), Ct)).Should().Be(1);
        var after = await store.GetAsync(chunk.Id, Ct);
        after!.Data.Should().BeEmpty();
        after.TranscriptText.Should().Be("Merhaba");
        after.AudioPurgedAt.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests -- --filter-class "*InMemoryVoiceStoresTests"`
Expected: FAIL (derleme hatası).

- [ ] **Step 3: Write ports and options**

```csharp
// src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IVoiceCallStore.cs
using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>Sesli görüşme kayıtları. Temsilci ve oturum başına tek açık görüşme kuralını depo garanti eder.</summary>
public interface IVoiceCallStore
{
    /// <summary>Ekler; temsilcinin ya da oturumun açık (Ringing/Active) görüşmesi varsa eklemez, <c>false</c> döner.</summary>
    Task<bool> TryCreateAsync(VoiceCall call, CancellationToken ct = default);
    Task<VoiceCall?> GetAsync(string id, CancellationToken ct = default);
    Task<VoiceCall?> GetOpenForAgentAsync(string agentId, CancellationToken ct = default);
    Task<IReadOnlyList<VoiceCall>> ListOpenAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Kalıcı durum <paramref name="expectedStatus"/> ise yazar (koşullu güncelleme); değilse <c>false</c>.</summary>
    Task<bool> TryUpdateAsync(VoiceCall call, VoiceCallStatus expectedStatus, CancellationToken ct = default);

    Task TouchChunkAsync(string callId, DateTime at, CancellationToken ct = default);
}
```

```csharp
// src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IVoiceRecordingStore.cs
using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>Sesli görüşme kayıt parçaları ve dökümleri.</summary>
public interface IVoiceRecordingStore
{
    /// <summary>Aynı (CallId, Track, Sequence) zaten varsa eklemez — ağ tekrarı idempotent.</summary>
    Task<bool> TryAddAsync(VoiceRecordingChunk chunk, CancellationToken ct = default);

    /// <summary>
    /// Sıradaki dökülecek parçayı sahiplenir (Pending → Processing) ve verisiyle döner. 5 dk'dan uzun
    /// Processing kalan (çöken pod) parça yeniden alınabilir. Yoksa <c>null</c>.
    /// </summary>
    Task<VoiceRecordingChunk?> TryClaimNextPendingAsync(DateTime now, CancellationToken ct = default);

    Task CompleteAsync(string chunkId, string? text, CancellationToken ct = default);
    Task FailAttemptAsync(string chunkId, DateTime nextAttemptAt, bool final, CancellationToken ct = default);

    /// <summary>Deneme sayısını artırmadan Pending'e döndürür (ör. LLM bütçesi dolu).</summary>
    Task PostponeAsync(string chunkId, DateTime nextAttemptAt, CancellationToken ct = default);

    /// <summary>Görüşmenin parçaları — ses verisi olmadan, (OffsetMs, Track) sıralı.</summary>
    Task<IReadOnlyList<VoiceRecordingChunk>> ListMetaAsync(string callId, CancellationToken ct = default);

    Task<VoiceRecordingChunk?> GetAsync(string chunkId, CancellationToken ct = default);

    /// <summary>Bu tarihten önce yüklenmiş parçaların sesini siler; döküm metni kalır. Etkilenen sayı.</summary>
    Task<int> PurgeAudioCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
```

```csharp
// src/CustomerSupportBot.Application/Ports/Outbound/AI/IAudioTranscriber.cs
namespace CustomerSupportBot.Application.Ports.Outbound.AI;

/// <summary>Tek başına çözülebilir bir ses dosyasını yazıya döker (dil: VoiceCall:TranscriptionLanguage).</summary>
public interface IAudioTranscriber
{
    Task<string> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default);
}
```

```csharp
// src/CustomerSupportBot.Application/Ports/Outbound/VoiceCallOptions.cs
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
```

`DataRetentionOptions.cs` içine `AttachmentRetentionDays` satırının altına ekle:

```csharp
    /// <summary>Bundan eski sesli görüşme kayıtlarının sesi silinir; döküm metni konuşmayla kalır.</summary>
    public int VoiceRecordingRetentionDays { get; set; } = 90;
```

- [ ] **Step 4: Write in-memory stores**

```csharp
// src/CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryVoiceCallStore.cs
// IVoiceCallStore'un bellek içi ikizi (API testleri). Postgres'teki partial unique index kuralını kilitle uygular.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryVoiceCallStore : IVoiceCallStore, ISessionDataEraser
{
    private readonly Dictionary<string, VoiceCall> _calls = new();
    private readonly Lock _gate = new();

    private static VoiceCall Copy(VoiceCall c) => new()
    {
        Id = c.Id, SessionId = c.SessionId, AgentId = c.AgentId, AgentDisplayName = c.AgentDisplayName,
        Status = c.Status, CreatedAt = c.CreatedAt, AnsweredAt = c.AnsweredAt, ConsentAt = c.ConsentAt,
        EndedAt = c.EndedAt, EndReason = c.EndReason, LastChunkAt = c.LastChunkAt
    };

    public Task<bool> TryCreateAsync(VoiceCall call, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_calls.Values.Any(c => c.IsOpen && (c.AgentId == call.AgentId || c.SessionId == call.SessionId)))
                return Task.FromResult(false);
            _calls[call.Id] = Copy(call);
            return Task.FromResult(true);
        }
    }

    public Task<VoiceCall?> GetAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_calls.TryGetValue(id, out var c) ? Copy(c) : null);
    }

    public Task<VoiceCall?> GetOpenForAgentAsync(string agentId, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_calls.Values.Where(c => c.IsOpen && c.AgentId == agentId).Select(Copy).FirstOrDefault());
    }

    public Task<IReadOnlyList<VoiceCall>> ListOpenAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<VoiceCall>>(_calls.Values.Where(c => c.IsOpen).Select(Copy).ToList());
    }

    public Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<VoiceCall>>(
                _calls.Values.Where(c => c.SessionId == sessionId).OrderBy(c => c.CreatedAt).Select(Copy).ToList());
    }

    public Task<bool> TryUpdateAsync(VoiceCall call, VoiceCallStatus expectedStatus, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_calls.TryGetValue(call.Id, out var current) || current.Status != expectedStatus)
                return Task.FromResult(false);
            _calls[call.Id] = Copy(call);
            return Task.FromResult(true);
        }
    }

    public Task TouchChunkAsync(string callId, DateTime at, CancellationToken ct = default)
    {
        lock (_gate) if (_calls.TryGetValue(callId, out var c)) c.LastChunkAt = at;
        return Task.CompletedTask;
    }

    public string Name => "voice_calls";

    public Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var ids = _calls.Values.Where(c => sessionIds.Contains(c.SessionId)).Select(c => c.Id).ToList();
            ids.ForEach(id => _calls.Remove(id));
            return Task.FromResult(ids.Count);
        }
    }
}
```

```csharp
// src/CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryVoiceRecordingStore.cs
// IVoiceRecordingStore'un bellek içi ikizi (API testleri).

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryVoiceRecordingStore : IVoiceRecordingStore, ISessionDataEraser
{
    private static readonly TimeSpan StaleProcessing = TimeSpan.FromMinutes(5);
    private readonly List<VoiceRecordingChunk> _chunks = [];
    private readonly Dictionary<string, DateTime> _claimedAt = new();
    private readonly Lock _gate = new();

    private static VoiceRecordingChunk Copy(VoiceRecordingChunk c, bool withData) => new()
    {
        Id = c.Id, CallId = c.CallId, SessionId = c.SessionId, Track = c.Track, Sequence = c.Sequence,
        OffsetMs = c.OffsetMs, DurationMs = c.DurationMs, ContentType = c.ContentType,
        Data = withData ? c.Data : [], TranscriptStatus = c.TranscriptStatus, TranscriptText = c.TranscriptText,
        Attempts = c.Attempts, NextAttemptAt = c.NextAttemptAt, AudioPurgedAt = c.AudioPurgedAt, CreatedAt = c.CreatedAt
    };

    public Task<bool> TryAddAsync(VoiceRecordingChunk chunk, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_chunks.Any(c => c.CallId == chunk.CallId && c.Track == chunk.Track && c.Sequence == chunk.Sequence))
                return Task.FromResult(false);
            _chunks.Add(Copy(chunk, withData: true));
            return Task.FromResult(true);
        }
    }

    public Task<VoiceRecordingChunk?> TryClaimNextPendingAsync(DateTime now, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var next = _chunks
                .Where(c => (c.TranscriptStatus == VoiceTranscriptStatus.Pending && c.NextAttemptAt <= now)
                         || (c.TranscriptStatus == VoiceTranscriptStatus.Processing
                             && _claimedAt.TryGetValue(c.Id, out var at) && now - at > StaleProcessing))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Sequence)
                .FirstOrDefault();
            if (next is null) return Task.FromResult<VoiceRecordingChunk?>(null);
            next.TranscriptStatus = VoiceTranscriptStatus.Processing;
            _claimedAt[next.Id] = now;
            return Task.FromResult<VoiceRecordingChunk?>(Copy(next, withData: true));
        }
    }

    public Task CompleteAsync(string chunkId, string? text, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var c = _chunks.Single(x => x.Id == chunkId);
            c.TranscriptStatus = VoiceTranscriptStatus.Done;
            c.TranscriptText = text;
        }
        return Task.CompletedTask;
    }

    public Task FailAttemptAsync(string chunkId, DateTime nextAttemptAt, bool final, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var c = _chunks.Single(x => x.Id == chunkId);
            c.Attempts++;
            c.NextAttemptAt = nextAttemptAt;
            c.TranscriptStatus = final ? VoiceTranscriptStatus.Failed : VoiceTranscriptStatus.Pending;
        }
        return Task.CompletedTask;
    }

    public Task PostponeAsync(string chunkId, DateTime nextAttemptAt, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var c = _chunks.Single(x => x.Id == chunkId);
            c.NextAttemptAt = nextAttemptAt;
            c.TranscriptStatus = VoiceTranscriptStatus.Pending;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VoiceRecordingChunk>> ListMetaAsync(string callId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<VoiceRecordingChunk>>(_chunks
                .Where(c => c.CallId == callId).OrderBy(c => c.OffsetMs).ThenBy(c => c.Track)
                .Select(c => Copy(c, withData: false)).ToList());
    }

    public Task<VoiceRecordingChunk?> GetAsync(string chunkId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_chunks.FirstOrDefault(c => c.Id == chunkId) is { } c ? Copy(c, withData: true) : null);
    }

    public Task<int> PurgeAudioCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var old = _chunks.Where(c => c.CreatedAt < cutoffUtc && c.AudioPurgedAt is null).ToList();
            foreach (var c in old) { c.Data = []; c.AudioPurgedAt = DateTime.UtcNow; }
            return Task.FromResult(old.Count);
        }
    }

    public string Name => "voice_recordings";

    public Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_chunks.RemoveAll(c => sessionIds.Contains(c.SessionId)));
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests -- --filter-class "*InMemoryVoiceStoresTests"`
Expected: PASS (5 test).

- [ ] **Step 6: Commit**

```bash
git add src/CustomerSupportBot.Application/Ports src/CustomerSupportBot.Adapters.Persistence/InMemory tests/CustomerSupportBot.Adapters.Persistence.Tests/InMemoryVoiceStoresTests.cs
git commit -m "feat(voice-call): ports, options and in-memory stores"
```

---

### Task 3: Postgres kalıcılığı ve tek görüşme kuralı

**Files:**
- Modify: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Schemas.cs` (`public const string Voice = "voice";`)
- Create: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Entities/Voice/VoiceCallEntity.cs`
- Create: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Entities/Voice/VoiceRecordingChunkEntity.cs`
- Create: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Configurations/Voice/VoiceCallConfiguration.cs`
- Create: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Configurations/Voice/VoiceRecordingChunkConfiguration.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/EfCore/CustomerSupportDbContext.cs` (DbSet'ler)
- Create: `src/CustomerSupportBot.Adapters.Persistence/Postgres/PostgresVoiceCallStore.cs`
- Create: `src/CustomerSupportBot.Adapters.Persistence/Postgres/PostgresVoiceRecordingStore.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/DependencyInjection/PersistenceAdapterServiceCollectionExtensions.cs`
- Create: migration `AddVoiceCalls` (EF CLI üretir)
- Test: `tests/CustomerSupportBot.Adapters.Persistence.Tests/PostgresVoiceStoresTests.cs`

**Interfaces:**
- Consumes: Task 2 port'ları.
- Produces: `PostgresVoiceCallStore`, `PostgresVoiceRecordingStore` (ikisi de `ISessionDataEraser`), DI kayıtları.

- [ ] **Step 1: Write the failing tests** (Docker gerekir — Testcontainers)

```csharp
// tests/CustomerSupportBot.Adapters.Persistence.Tests/PostgresVoiceStoresTests.cs
// Gerçek Postgres: tek açık görüşme kuralı partial unique index ile ve eşzamanlı isteklerde de geçerli;
// parça idempotentliği; sahiplenme tek pod'a düşer; saklama sesi siler, dökümü korur; oturum silmesi.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model.Voice;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresVoiceStoresTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private PostgresVoiceCallStore Calls => new(fixture.DbFactory);
    private PostgresVoiceRecordingStore Recordings => new(fixture.DbFactory);

    private async Task<string> InsertSessionAsync()
    {
        var sessionId = $"vc-{Guid.NewGuid():N}";
        await using var ctx = fixture.DbFactory.CreateDbContext();
        ctx.Sessions.Add(new SessionEntity { SessionId = sessionId, CreatedAt = DateTime.UtcNow, LastActivity = DateTime.UtcNow, StateJson = "{}" });
        await ctx.SaveChangesAsync(Ct);
        return sessionId;
    }

    [Fact]
    public async Task ConcurrentStarts_ForSameAgent_OnlyOneSucceeds()
    {
        var agent = $"agent-{Guid.NewGuid():N}";
        var sessions = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => InsertSessionAsync()));
        var results = await Task.WhenAll(sessions.Select(s =>
            new PostgresVoiceCallStore(fixture.DbFactory).TryCreateAsync(VoiceCall.Start(s, agent, "Elif", DateTime.UtcNow), Ct)));
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task SecondOpenCall_OnSameSession_IsRejected_UntilFirstEnds()
    {
        var sid = await InsertSessionAsync();
        var first = VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Elif", DateTime.UtcNow);
        (await Calls.TryCreateAsync(first, Ct)).Should().BeTrue();
        (await Calls.TryCreateAsync(VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Can", DateTime.UtcNow), Ct)).Should().BeFalse();

        first.Hangup(DateTime.UtcNow, VoiceCallEndReasons.AgentHangup);
        (await Calls.TryUpdateAsync(first, VoiceCallStatus.Ringing, Ct)).Should().BeTrue();
        (await Calls.TryCreateAsync(VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Can", DateTime.UtcNow), Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task TryUpdate_WithStaleStatus_ReturnsFalse()
    {
        var sid = await InsertSessionAsync();
        var call = VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Elif", DateTime.UtcNow);
        await Calls.TryCreateAsync(call, Ct);
        call.Accept(DateTime.UtcNow);
        (await Calls.TryUpdateAsync(call, VoiceCallStatus.Ringing, Ct)).Should().BeTrue();
        (await Calls.TryUpdateAsync(call, VoiceCallStatus.Ringing, Ct)).Should().BeFalse();
        (await Calls.GetAsync(call.Id, Ct))!.ConsentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Chunks_AreIdempotent_ClaimedOnce_AndPurgeKeepsTranscript()
    {
        var sid = await InsertSessionAsync();
        var call = VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Elif", DateTime.UtcNow);
        await Calls.TryCreateAsync(call, Ct);
        var created = DateTime.UtcNow.AddDays(-100);
        var chunk = new VoiceRecordingChunk { CallId = call.Id, SessionId = sid, Track = VoiceTrack.Customer, Sequence = 0, OffsetMs = 0, DurationMs = 10000, Data = [1, 2, 3], CreatedAt = created, NextAttemptAt = created };
        (await Recordings.TryAddAsync(chunk, Ct)).Should().BeTrue();
        (await Recordings.TryAddAsync(new VoiceRecordingChunk { CallId = call.Id, SessionId = sid, Track = VoiceTrack.Customer, Sequence = 0, Data = [9], CreatedAt = created, NextAttemptAt = created }, Ct)).Should().BeFalse();

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            new PostgresVoiceRecordingStore(fixture.DbFactory).TryClaimNextPendingAsync(DateTime.UtcNow, Ct)));
        claims.Count(c => c?.Id == chunk.Id).Should().Be(1);

        await Recordings.CompleteAsync(chunk.Id, "Kargom gelmedi.", Ct);
        (await Recordings.PurgeAudioCreatedBeforeAsync(DateTime.UtcNow.AddDays(-90), Ct)).Should().BeGreaterThanOrEqualTo(1);
        var after = await Recordings.GetAsync(chunk.Id, Ct);
        after!.Data.Should().BeEmpty();
        after.TranscriptText.Should().Be("Kargom gelmedi.");
    }

    [Fact]
    public async Task EraseSessions_RemovesCallsAndChunks()
    {
        var sid = await InsertSessionAsync();
        var call = VoiceCall.Start(sid, $"a-{Guid.NewGuid():N}", "Elif", DateTime.UtcNow);
        await Calls.TryCreateAsync(call, Ct);
        await Recordings.TryAddAsync(new VoiceRecordingChunk { CallId = call.Id, SessionId = sid, Track = VoiceTrack.Agent, Sequence = 0, Data = [1], CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow }, Ct);

        (await Recordings.EraseSessionsAsync([sid], Ct)).Should().Be(1);
        (await Calls.EraseSessionsAsync([sid], Ct)).Should().Be(1);
        (await Calls.GetAsync(call.Id, Ct)).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests -- --filter-class "*PostgresVoiceStoresTests"`
Expected: FAIL (derleme hatası).

- [ ] **Step 3: Entities and configurations**

```csharp
// src/CustomerSupportBot.Adapters.Persistence/EfCore/Entities/Voice/VoiceCallEntity.cs
// `voice.calls` — temsilci–müşteri sesli görüşmeleri.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;

public sealed class VoiceCallEntity
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string AgentDisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? ConsentAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public DateTime? LastChunkAt { get; set; }
}
```

```csharp
// src/CustomerSupportBot.Adapters.Persistence/EfCore/Entities/Voice/VoiceRecordingChunkEntity.cs
// `voice.recording_chunks` — 10 sn'lik kayıt parçaları ve dökümleri.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;

public sealed class VoiceRecordingChunkEntity
{
    public string Id { get; set; } = "";
    public string CallId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Track { get; set; } = "";
    public int Sequence { get; set; }
    public int OffsetMs { get; set; }
    public int DurationMs { get; set; }
    public string ContentType { get; set; } = "";
    public int SizeBytes { get; set; }
    public byte[] Data { get; set; } = [];
    public string TranscriptStatus { get; set; } = "";
    public string? TranscriptText { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? AudioPurgedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

```csharp
// src/CustomerSupportBot.Adapters.Persistence/EfCore/Configurations/Voice/VoiceCallConfiguration.cs
// voice.calls — temsilci ve oturum başına tek açık görüşme: partial unique index (çok pod'da da geçerli).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Voice;

internal sealed class VoiceCallConfiguration : IEntityTypeConfiguration<VoiceCallEntity>
{
    internal const string OpenFilter = "status IN ('Ringing','Active')";

    public void Configure(EntityTypeBuilder<VoiceCallEntity> builder)
    {
        builder.ToTable("calls", Schemas.Voice);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasMaxLength(32);
        builder.Property(c => c.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired();
        builder.Property(c => c.AgentId).HasColumnName("agent_id").HasMaxLength(128).IsRequired();
        builder.Property(c => c.AgentDisplayName).HasColumnName("agent_display_name").HasMaxLength(128).IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(c => c.AnsweredAt).HasColumnName("answered_at").HasColumnType("timestamptz");
        builder.Property(c => c.ConsentAt).HasColumnName("consent_at").HasColumnType("timestamptz");
        builder.Property(c => c.EndedAt).HasColumnName("ended_at").HasColumnType("timestamptz");
        builder.Property(c => c.EndReason).HasColumnName("end_reason").HasMaxLength(32);
        builder.Property(c => c.LastChunkAt).HasColumnName("last_chunk_at").HasColumnType("timestamptz");

        builder.HasIndex(c => c.AgentId).IsUnique().HasFilter(OpenFilter).HasDatabaseName("ux_calls_agent_open");
        builder.HasIndex(c => c.SessionId).IsUnique().HasFilter(OpenFilter).HasDatabaseName("ux_calls_session_open");
        builder.HasIndex(c => new { c.SessionId, c.CreatedAt }).HasDatabaseName("ix_calls_session");

        builder.HasOne<SessionEntity>().WithMany().HasForeignKey(c => c.SessionId)
            .HasConstraintName("fk_calls_session").OnDelete(DeleteBehavior.Cascade);
    }
}
```

```csharp
// src/CustomerSupportBot.Adapters.Persistence/EfCore/Configurations/Voice/VoiceRecordingChunkConfiguration.cs
// voice.recording_chunks — (call, track, sequence) benzersiz: aynı parça iki kez yüklenmez.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Configurations.Voice;

internal sealed class VoiceRecordingChunkConfiguration : IEntityTypeConfiguration<VoiceRecordingChunkEntity>
{
    public void Configure(EntityTypeBuilder<VoiceRecordingChunkEntity> builder)
    {
        builder.ToTable("recording_chunks", Schemas.Voice);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasMaxLength(32);
        builder.Property(c => c.CallId).HasColumnName("call_id").HasMaxLength(32).IsRequired();
        builder.Property(c => c.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired();
        builder.Property(c => c.Track).HasColumnName("track").HasMaxLength(16).IsRequired();
        builder.Property(c => c.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(c => c.OffsetMs).HasColumnName("offset_ms").IsRequired();
        builder.Property(c => c.DurationMs).HasColumnName("duration_ms").IsRequired();
        builder.Property(c => c.ContentType).HasColumnName("content_type").HasMaxLength(64).IsRequired();
        builder.Property(c => c.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(c => c.Data).HasColumnName("data").HasColumnType("bytea").IsRequired();
        builder.Property(c => c.TranscriptStatus).HasColumnName("transcript_status").HasMaxLength(16).IsRequired();
        builder.Property(c => c.TranscriptText).HasColumnName("transcript_text");
        builder.Property(c => c.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(c => c.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(c => c.ClaimedAt).HasColumnName("claimed_at").HasColumnType("timestamptz");
        builder.Property(c => c.AudioPurgedAt).HasColumnName("audio_purged_at").HasColumnType("timestamptz");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(c => new { c.CallId, c.Track, c.Sequence }).IsUnique().HasDatabaseName("ux_chunks_call_track_seq");
        builder.HasIndex(c => new { c.TranscriptStatus, c.NextAttemptAt }).HasDatabaseName("ix_chunks_transcript_queue");
        builder.HasIndex(c => c.CreatedAt).HasDatabaseName("ix_chunks_created");

        builder.HasOne<VoiceCallEntity>().WithMany().HasForeignKey(c => c.CallId)
            .HasConstraintName("fk_chunks_call").OnDelete(DeleteBehavior.Cascade);
    }
}
```

`CustomerSupportDbContext.cs` içine `Attachments` DbSet'inin altına:

```csharp
    public DbSet<VoiceCallEntity> VoiceCalls => Set<VoiceCallEntity>();
    public DbSet<VoiceRecordingChunkEntity> VoiceRecordingChunks => Set<VoiceRecordingChunkEntity>();
```

(dosyanın `using` bloğuna `using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;`). Konfigürasyonlar `ApplyConfigurationsFromAssembly` ile zaten toplanıyorsa ek kayıt gerekmez; dosyada konfigürasyonlar tek tek ekleniyorsa `modelBuilder.ApplyConfiguration(new VoiceCallConfiguration()); modelBuilder.ApplyConfiguration(new VoiceRecordingChunkConfiguration());` eklenir — dosyayı açıp mevcut deseni izle.

- [ ] **Step 4: Postgres stores**

```csharp
// src/CustomerSupportBot.Adapters.Persistence/Postgres/PostgresVoiceCallStore.cs
// Sesli görüşmeler — doğrudan PostgreSQL. Tek açık görüşme kuralı partial unique index'te; ekleme
// çakışması (23505) "meşgul" demektir. Durum değişiklikleri koşullu UPDATE ile (yarışan kapatmalar).

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresVoiceCallStore(IDbContextFactory<CustomerSupportDbContext> dbFactory)
    : IVoiceCallStore, ISessionDataEraser
{
    private static readonly string[] OpenStatuses = [nameof(VoiceCallStatus.Ringing), nameof(VoiceCallStatus.Active)];

    private static VoiceCall ToDomain(VoiceCallEntity e) => new()
    {
        Id = e.Id, SessionId = e.SessionId, AgentId = e.AgentId, AgentDisplayName = e.AgentDisplayName,
        Status = Enum.Parse<VoiceCallStatus>(e.Status), CreatedAt = e.CreatedAt, AnsweredAt = e.AnsweredAt,
        ConsentAt = e.ConsentAt, EndedAt = e.EndedAt, EndReason = e.EndReason, LastChunkAt = e.LastChunkAt
    };

    public async Task<bool> TryCreateAsync(VoiceCall call, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.VoiceCalls.Add(new VoiceCallEntity
        {
            Id = call.Id, SessionId = call.SessionId, AgentId = call.AgentId, AgentDisplayName = call.AgentDisplayName,
            Status = call.Status.ToString(), CreatedAt = call.CreatedAt, AnsweredAt = call.AnsweredAt,
            ConsentAt = call.ConsentAt, EndedAt = call.EndedAt, EndReason = call.EndReason, LastChunkAt = call.LastChunkAt
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    public async Task<VoiceCall?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.VoiceCalls.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return e is null ? null : ToDomain(e);
    }

    public async Task<VoiceCall?> GetOpenForAgentAsync(string agentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.VoiceCalls.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AgentId == agentId && OpenStatuses.Contains(c.Status), ct);
        return e is null ? null : ToDomain(e);
    }

    public async Task<IReadOnlyList<VoiceCall>> ListOpenAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.VoiceCalls.AsNoTracking().Where(c => OpenStatuses.Contains(c.Status)).ToListAsync(ct);
        return list.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.VoiceCalls.AsNoTracking().Where(c => c.SessionId == sessionId)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);
        return list.Select(ToDomain).ToList();
    }

    public async Task<bool> TryUpdateAsync(VoiceCall call, VoiceCallStatus expectedStatus, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var expected = expectedStatus.ToString();
        var status = call.Status.ToString();
        return await db.VoiceCalls
            .Where(c => c.Id == call.Id && c.Status == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.AnsweredAt, call.AnsweredAt)
                .SetProperty(c => c.ConsentAt, call.ConsentAt)
                .SetProperty(c => c.EndedAt, call.EndedAt)
                .SetProperty(c => c.EndReason, call.EndReason), ct) > 0;
    }

    public async Task TouchChunkAsync(string callId, DateTime at, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.VoiceCalls.Where(c => c.Id == callId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastChunkAt, at), ct);
    }

    public string Name => "voice_calls";

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.VoiceCalls.Where(c => sessionIds.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
    }
}
```

```csharp
// src/CustomerSupportBot.Adapters.Persistence/Postgres/PostgresVoiceRecordingStore.cs
// Kayıt parçaları — doğrudan PostgreSQL. Listeleme sesi OKUMAZ. Döküm kuyruğu sahiplenmesi koşullu
// UPDATE ile: aynı parçayı iki pod birlikte işleyemez.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresVoiceRecordingStore(IDbContextFactory<CustomerSupportDbContext> dbFactory)
    : IVoiceRecordingStore, ISessionDataEraser
{
    private static readonly TimeSpan StaleProcessing = TimeSpan.FromMinutes(5);
    private static readonly string Pending = nameof(VoiceTranscriptStatus.Pending);
    private static readonly string Processing = nameof(VoiceTranscriptStatus.Processing);

    private static VoiceRecordingChunk ToDomain(VoiceRecordingChunkEntity e, bool withData) => new()
    {
        Id = e.Id, CallId = e.CallId, SessionId = e.SessionId, Track = Enum.Parse<VoiceTrack>(e.Track),
        Sequence = e.Sequence, OffsetMs = e.OffsetMs, DurationMs = e.DurationMs, ContentType = e.ContentType,
        Data = withData ? e.Data : [], TranscriptStatus = Enum.Parse<VoiceTranscriptStatus>(e.TranscriptStatus),
        TranscriptText = e.TranscriptText, Attempts = e.Attempts, NextAttemptAt = e.NextAttemptAt,
        AudioPurgedAt = e.AudioPurgedAt, CreatedAt = e.CreatedAt
    };

    public async Task<bool> TryAddAsync(VoiceRecordingChunk chunk, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.VoiceRecordingChunks.Add(new VoiceRecordingChunkEntity
        {
            Id = chunk.Id, CallId = chunk.CallId, SessionId = chunk.SessionId, Track = chunk.Track.ToString(),
            Sequence = chunk.Sequence, OffsetMs = chunk.OffsetMs, DurationMs = chunk.DurationMs,
            ContentType = chunk.ContentType, SizeBytes = chunk.Data.Length, Data = chunk.Data,
            TranscriptStatus = chunk.TranscriptStatus.ToString(), TranscriptText = chunk.TranscriptText,
            Attempts = chunk.Attempts, NextAttemptAt = chunk.NextAttemptAt, CreatedAt = chunk.CreatedAt
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    public async Task<VoiceRecordingChunk?> TryClaimNextPendingAsync(DateTime now, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var staleBefore = now - StaleProcessing;
        // Birkaç aday: biri başka pod'a kaptırılırsa sıradakini dene.
        var candidates = await db.VoiceRecordingChunks.AsNoTracking()
            .Where(c => (c.TranscriptStatus == Pending && c.NextAttemptAt <= now)
                     || (c.TranscriptStatus == Processing && c.ClaimedAt < staleBefore))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Sequence)
            .Select(c => new { c.Id, c.TranscriptStatus, c.ClaimedAt })
            .Take(5)
            .ToListAsync(ct);

        foreach (var cand in candidates)
        {
            var claimed = await db.VoiceRecordingChunks
                .Where(c => c.Id == cand.Id && c.TranscriptStatus == cand.TranscriptStatus && c.ClaimedAt == cand.ClaimedAt)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.TranscriptStatus, Processing)
                    .SetProperty(c => c.ClaimedAt, now), ct) > 0;
            if (!claimed) continue;
            var e = await db.VoiceRecordingChunks.AsNoTracking().FirstAsync(c => c.Id == cand.Id, ct);
            return ToDomain(e, withData: true);
        }
        return null;
    }

    public async Task CompleteAsync(string chunkId, string? text, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.VoiceRecordingChunks.Where(c => c.Id == chunkId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.TranscriptStatus, nameof(VoiceTranscriptStatus.Done))
            .SetProperty(c => c.TranscriptText, text), ct);
    }

    public async Task FailAttemptAsync(string chunkId, DateTime nextAttemptAt, bool final, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var status = final ? nameof(VoiceTranscriptStatus.Failed) : Pending;
        await db.VoiceRecordingChunks.Where(c => c.Id == chunkId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Attempts, c => c.Attempts + 1)
            .SetProperty(c => c.NextAttemptAt, nextAttemptAt)
            .SetProperty(c => c.TranscriptStatus, status), ct);
    }

    public async Task PostponeAsync(string chunkId, DateTime nextAttemptAt, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.VoiceRecordingChunks.Where(c => c.Id == chunkId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.NextAttemptAt, nextAttemptAt)
            .SetProperty(c => c.TranscriptStatus, Pending), ct);
    }

    public async Task<IReadOnlyList<VoiceRecordingChunk>> ListMetaAsync(string callId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.VoiceRecordingChunks.AsNoTracking()
            .Where(c => c.CallId == callId)
            .OrderBy(c => c.OffsetMs).ThenBy(c => c.Track)
            .Select(c => new VoiceRecordingChunkEntity
            {
                Id = c.Id, CallId = c.CallId, SessionId = c.SessionId, Track = c.Track, Sequence = c.Sequence,
                OffsetMs = c.OffsetMs, DurationMs = c.DurationMs, ContentType = c.ContentType,
                TranscriptStatus = c.TranscriptStatus, TranscriptText = c.TranscriptText, Attempts = c.Attempts,
                NextAttemptAt = c.NextAttemptAt, AudioPurgedAt = c.AudioPurgedAt, CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);
        return list.Select(e => ToDomain(e, withData: false)).ToList();
    }

    public async Task<VoiceRecordingChunk?> GetAsync(string chunkId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.VoiceRecordingChunks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chunkId, ct);
        return e is null ? null : ToDomain(e, withData: true);
    }

    public async Task<int> PurgeAudioCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        return await db.VoiceRecordingChunks
            .Where(c => c.CreatedAt < cutoffUtc && c.AudioPurgedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Data, Array.Empty<byte>())
                .SetProperty(c => c.SizeBytes, 0)
                .SetProperty(c => c.AudioPurgedAt, now), ct);
    }

    public string Name => "voice_recordings";

    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.VoiceRecordingChunks.Where(c => sessionIds.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
    }
}
```

DI (`PersistenceAdapterServiceCollectionExtensions.cs`, `AddSessionDataStore<IAttachmentStore, …>` satırının altına):

```csharp
        AddSessionDataStore<IVoiceCallStore, PostgresVoiceCallStore>(services);
        AddSessionDataStore<IVoiceRecordingStore, PostgresVoiceRecordingStore>(services);
```

`Schemas.cs`: `public const string Voice = "voice";`

- [ ] **Step 5: Migration**

Run: `dotnet ef migrations add AddVoiceCalls --project src/CustomerSupportBot.Adapters.Persistence`
Expected: `Migrations/{timestamp}_AddVoiceCalls.cs` oluşur; içinde `EnsureSchema("voice")`, iki tablo, `ux_calls_agent_open` ve `ux_calls_session_open` için `filter: "status IN ('Ringing','Active')"`. Dosyayı aç ve filtrelerin oluştuğunu doğrula.

- [ ] **Step 6: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests -- --filter-class "*PostgresVoiceStoresTests"`
Expected: PASS (5 test). Ardından tüm persistence testleri: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests` — PASS.

- [ ] **Step 7: Commit**

```bash
git add src/CustomerSupportBot.Adapters.Persistence tests/CustomerSupportBot.Adapters.Persistence.Tests/PostgresVoiceStoresTests.cs
git commit -m "feat(voice-call): postgres stores with one-open-call-per-agent index"
```

---

### Task 4: Köprü — yönlü sinyal ve döküm satırı (meta alanlarıyla)

**Files:**
- Modify: `src/CustomerSupportBot.Domain/Model/ChatBridgeMessage.cs`
- Modify: `src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryChatBridge.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/Postgres/PostgresChatBridge.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Entities/Chat/ChatBridgeMessageEntity.cs`
- Modify: `src/CustomerSupportBot.Adapters.Persistence/EfCore/Configurations/Chat/ChatBridgeMessageConfiguration.cs`
- Create: migration `AddBridgeVoiceMeta`
- Test: `tests/CustomerSupportBot.Adapters.Persistence.Tests/InMemoryChatBridgeVoiceTests.cs`, `tests/CustomerSupportBot.Adapters.Persistence.Tests/PostgresChatBridgeVoiceTests.cs`

**Interfaces:**
- Produces:
  - `ChatBridgeSender.VoiceSignal` (yeni enum üyesi, sona eklenir).
  - `ChatBridgeMessage.VoiceCallId : string?`, `VoiceTrack : string?` (`"agent"|"customer"`), `OffsetMs : int?`.
  - `IChatBridge.PublishVoiceSignal(string sessionId, bool toCustomer, string payloadJson) : void` — kalıcı değil; yalnızca hedef tarafa.
  - `IChatBridge.PublishVoiceTranscriptAsync(string sessionId, string callId, string track, int offsetMs, string text) : Task` — geçmişe yazılır, yalnız temsilci tarafına yayınlanır.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CustomerSupportBot.Adapters.Persistence.Tests/InMemoryChatBridgeVoiceTests.cs
// Sinyal yalnızca hedef tarafa gider ve geçmişe yazılmaz; döküm satırı yalnız temsilciye gider ve geçmişte kalır.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryChatBridgeVoiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<ChatBridgeMessage>> CollectAsync(IAsyncEnumerable<ChatBridgeMessage> stream, int count)
    {
        var list = new List<ChatBridgeMessage>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try { await foreach (var m in stream.WithCancellation(cts.Token)) { list.Add(m); if (list.Count == count) break; } }
        catch (OperationCanceledException) { }
        return list;
    }

    [Fact]
    public async Task VoiceSignal_GoesOnlyToTarget_AndIsNotPersisted()
    {
        var bridge = new InMemoryChatBridge();
        var toUser = CollectAsync(bridge.SubscribeToUserAsync("s1", Ct), 1);
        var toAdmin = CollectAsync(bridge.SubscribeToAdminAsync("s1", Ct), 1);
        await Task.Delay(50, Ct);

        bridge.PublishVoiceSignal("s1", toCustomer: true, """{"callId":"c1","type":"ring"}""");

        (await toUser).Should().ContainSingle(m => m.Sender == ChatBridgeSender.VoiceSignal && m.Text.Contains("ring"));
        (await toAdmin).Should().BeEmpty();
        (await bridge.GetHistoryAsync("s1")).Should().BeEmpty();
    }

    [Fact]
    public async Task VoiceTranscript_GoesToAdminOnly_WithMeta_AndIsPersisted()
    {
        var bridge = new InMemoryChatBridge();
        var toUser = CollectAsync(bridge.SubscribeToUserAsync("s1", Ct), 1);
        var toAdmin = CollectAsync(bridge.SubscribeToAdminAsync("s1", Ct), 1);
        await Task.Delay(50, Ct);

        await bridge.PublishVoiceTranscriptAsync("s1", "c1", "customer", 20000, "Müşteri: Kargom gelmedi.");

        var admin = (await toAdmin).Single();
        admin.VoiceCallId.Should().Be("c1");
        admin.VoiceTrack.Should().Be("customer");
        admin.OffsetMs.Should().Be(20000);
        (await toUser).Should().BeEmpty();
        (await bridge.GetHistoryAsync("s1")).Should().ContainSingle(m => m.VoiceCallId == "c1");
    }
}
```

```csharp
// tests/CustomerSupportBot.Adapters.Persistence.Tests/PostgresChatBridgeVoiceTests.cs
// Döküm satırının meta alanları DB'ye yazılır ve geçmiş yeniden yüklendiğinde (yeni pod) geri gelir.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresChatBridgeVoiceTests(PostgresCatalogFixture fixture)
{
    [Fact]
    public async Task TranscriptMeta_SurvivesHydration()
    {
        var ct = TestContext.Current.CancellationToken;
        var sid = $"vb-{Guid.NewGuid():N}";
        var writer = PostgresChatBridgeTestFactory.Create(fixture);
        await writer.PublishVoiceTranscriptAsync(sid, "c1", "agent", 10000, "Temsilci (Elif): Merhaba");

        var freshPod = PostgresChatBridgeTestFactory.Create(fixture);
        var history = await freshPod.GetHistoryAsync(sid);
        history.Should().ContainSingle();
        history[0].VoiceCallId.Should().Be("c1");
        history[0].VoiceTrack.Should().Be("agent");
        history[0].OffsetMs.Should().Be(10000);
    }
}
```

`PostgresChatBridgeTestFactory`: mevcut `PostgresChatBridgeHydrationTests.cs` köprüyü nasıl kuruyorsa (fixture.DbFactory + `NoopMessageBus` + logger) o kurulumu `internal static class PostgresChatBridgeTestFactory { public static PostgresChatBridge Create(PostgresCatalogFixture f) => …; }` olarak aynı test projesinde `PostgresChatBridgeTestFactory.cs` dosyasına çıkar ve `PostgresChatBridgeHydrationTests` da onu kullansın (oturum satırı FK gerekiyorsa o testteki oturum ekleme yardımcısını da buraya taşı).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests -- --filter-class "*ChatBridgeVoiceTests"`
Expected: FAIL (derleme hatası).

- [ ] **Step 3: Domain + port**

`ChatBridgeMessage.cs`: `ChatBridgeSender` enum'unun SONUNA ekle:

```csharp
    /// <summary>
    /// Sesli görüşme sinyali (WebRTC teklif/yanıt/ağ adresi, çalma, kabul…). Text: JSON yük. Geçmişe
    /// yazılmaz ve yönlüdür: yalnızca hedef tarafa yayınlanır.
    /// </summary>
    VoiceSignal
```

`ChatBridgeMessage` sınıfına:

```csharp
    /// <summary>Sesli görüşme döküm satırıysa görüşme kimliği; değilse <c>null</c>.</summary>
    public string? VoiceCallId { get; set; }

    /// <summary>Döküm satırının konuşanı: <c>agent</c> | <c>customer</c>.</summary>
    public string? VoiceTrack { get; set; }

    /// <summary>Döküm satırının görüşme başından itibaren konumu (ms) — kayıtta o ana atlamak için.</summary>
    public int? OffsetMs { get; set; }
```

`IChatBridge.cs`'e (`PublishBotTyping`'in altına):

```csharp
    /// <summary>
    /// Sesli görüşme sinyali — kalıcı DEĞİL, yalnızca hedef tarafa: <paramref name="toCustomer"/> true ise
    /// müşteri kanalına, değilse temsilci kanalına. <paramref name="payloadJson"/> mesajın Text'idir.
    /// </summary>
    void PublishVoiceSignal(string sessionId, bool toCustomer, string payloadJson);

    /// <summary>Döküm satırı — geçmişe yazılır, yalnızca temsilci kanalına yayınlanır (müşteri görmez).</summary>
    Task PublishVoiceTranscriptAsync(string sessionId, string callId, string track, int offsetMs, string text);
```

- [ ] **Step 4: InMemoryChatBridge**

`PublishBotTyping` metodunun altına (dosyadaki `Broadcast`/`Append` yardımcı adlarını kullan; `PublishAdminOnlyMessageAsync`'in yaptığı kalıcılaştırma çağrısını aynen kopyala):

```csharp
    public void PublishVoiceSignal(string sessionId, bool toCustomer, string payloadJson)
    {
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.VoiceSignal, Text = payloadJson };
        Broadcast(toCustomer ? _toUser : _toAdmin, sessionId, msg);
    }

    public Task PublishVoiceTranscriptAsync(string sessionId, string callId, string track, int offsetMs, string text)
    {
        var msg = new ChatBridgeMessage
        {
            SessionId = sessionId, Sender = ChatBridgeSender.System, Text = text,
            VoiceCallId = callId, VoiceTrack = track, OffsetMs = offsetMs
        };
        Append(sessionId, msg);
        Broadcast(_toAdmin, sessionId, msg);
        return Task.CompletedTask;
    }
```

(`InMemoryChatBridge`'deki gerçek alan/metot adları farklıysa — ör. `_toAdmin` yerine başka bir sözlük — `PublishAdminOnlyMessageAsync` gövdesini örnek alarak aynı adları kullan.)

- [ ] **Step 5: PostgresChatBridge + entity + Redis**

Entity'ye:

```csharp
    public string? VoiceCallId { get; set; }
    public string? VoiceTrack { get; set; }
    public int? OffsetMs { get; set; }
```

Config'e (`CreatedAt` özelliğinin altına):

```csharp
        builder.Property(m => m.VoiceCallId).HasColumnName("voice_call_id").HasMaxLength(32);
        builder.Property(m => m.VoiceTrack).HasColumnName("voice_track").HasMaxLength(16);
        builder.Property(m => m.OffsetMs).HasColumnName("offset_ms");
```

`PostgresChatBridge.cs`:
1. `AppendAsync` içinde entity oluşturulan yere (`MessageId = msg.Id, …` bloğu) ekle: `VoiceCallId = msg.VoiceCallId, VoiceTrack = msg.VoiceTrack, OffsetMs = msg.OffsetMs,`
2. Hidrasyonda `new ChatBridgeMessage { … }` (entity → domain, ~366. satır) ekle: `VoiceCallId = e.VoiceCallId, VoiceTrack = e.VoiceTrack, OffsetMs = e.OffsetMs,`
3. `PublishRedis` yüküne ekle: `msg.VoiceCallId, msg.VoiceTrack, msg.OffsetMs`
4. Redis alıcısında (`OnRemoteBridge…`, ~398) JSON'dan oku ve `new ChatBridgeMessage { … }` içine ata:

```csharp
            VoiceCallId = root.TryGetProperty("VoiceCallId", out var vc) && vc.ValueKind == JsonValueKind.String ? vc.GetString() : null,
            VoiceTrack  = root.TryGetProperty("VoiceTrack", out var vt) && vt.ValueKind == JsonValueKind.String ? vt.GetString() : null,
            OffsetMs    = root.TryGetProperty("OffsetMs", out var vo) && vo.ValueKind == JsonValueKind.Number ? vo.GetInt32() : null,
```

(alıcıdaki JSON kök değişkeninin adını mevcut koddan al.) Redis alıcısı mesajı hangi kanaldan geldiyse oraya yayınlar — `VoiceSignal` için ayrıca bir şey gerekmez.

5. Yeni metotlar (`PublishBotTyping`'in altına):

```csharp
    public void PublishVoiceSignal(string sessionId, bool toCustomer, string payloadJson)
    {
        // Transient — DB'ye yazılmaz (BotTyping gibi); yalnızca hedef tarafa.
        var msg = new ChatBridgeMessage { SessionId = sessionId, Sender = ChatBridgeSender.VoiceSignal, Text = payloadJson };
        Broadcast(toCustomer ? _toUser : _toAdmin, sessionId, msg);
        PublishRedis(toCustomer ? "csbot:bridge:touser" : "csbot:bridge:toadmin", msg);
    }

    public async Task PublishVoiceTranscriptAsync(string sessionId, string callId, string track, int offsetMs, string text)
    {
        var msg = new ChatBridgeMessage
        {
            SessionId = sessionId, Sender = ChatBridgeSender.System, Text = text,
            VoiceCallId = callId, VoiceTrack = track, OffsetMs = offsetMs
        };
        await AppendAsync(sessionId, msg).ConfigureAwait(false);
        Broadcast(_toAdmin, sessionId, msg);
        PublishRedis("csbot:bridge:toadmin", msg);
    }
```

6. Migration: `dotnet ef migrations add AddBridgeVoiceMeta --project src/CustomerSupportBot.Adapters.Persistence` — üç nullable sütun eklenir.

- [ ] **Step 6: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Adapters.Persistence.Tests`
Expected: PASS (yeni 3 test + mevcut köprü testleri).

- [ ] **Step 7: Commit**

```bash
git add src/CustomerSupportBot.Domain/Model/ChatBridgeMessage.cs src/CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.cs src/CustomerSupportBot.Adapters.Persistence tests/CustomerSupportBot.Adapters.Persistence.Tests
git commit -m "feat(voice-call): directional voice signals and transcript lines on the chat bridge"
```

---

### Task 5: Uygulama servisi — VoiceCallService ve TURN kimliği

**Files:**
- Create: `src/CustomerSupportBot.Application/Ports/Inbound/IVoiceCallPort.cs`
- Create: `src/CustomerSupportBot.Application/Services/Voice/TurnCredentialFactory.cs`
- Create: `src/CustomerSupportBot.Application/Services/Voice/VoiceCallService.cs`
- Modify: `src/CustomerSupportBot.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/CustomerSupportBot.Application.Tests/Voice/VoiceCallServiceTests.cs`, `tests/CustomerSupportBot.Application.Tests/Voice/TurnCredentialFactoryTests.cs`

**Interfaces:**
- Consumes: `IVoiceCallStore`, `IVoiceRecordingStore`, `IChatBridge` (Task 4), `IChatModeRegistry.GetMode(string) : ChatMode`, `ISessionManager.GetAsync(string, ct) : Task<AgentSession?>` (`AgentSession.State.AuthenticatedCustomerId`), `IOptionsMonitor<VoiceCallOptions>`, `TimeProvider`.
- Produces (`CustomerSupportBot.Application.Ports.Inbound`):

```csharp
public sealed record StaffCaller(string AgentId, string DisplayName, bool IsAdmin);
public enum VoiceCallError { NotFound, Forbidden, Busy, NotInHumanMode, InvalidState, Invalid, TooLarge, Disabled }
public sealed record VoiceCallResult(VoiceCall? Call, VoiceCallError? Error = null)
{
    public bool Ok => Error is null;
}
public sealed record IceServerConfig(IReadOnlyList<IceServer> IceServers);
public sealed record IceServer(IReadOnlyList<string> Urls, string? Username = null, string? Credential = null);
public sealed record VoiceTranscriptLine(string ChunkId, string Track, int OffsetMs, string? Text, string Status);
public sealed record VoiceCallView(VoiceCall Call, IReadOnlyList<VoiceTranscriptLine> Lines);

public interface IVoiceCallPort
{
    Task<VoiceCallResult> StartAsync(string sessionId, StaffCaller staff, CancellationToken ct = default);
    Task<VoiceCallResult> AcceptAsync(string callId, string? customerId, CancellationToken ct = default);
    Task<VoiceCallResult> DeclineAsync(string callId, string? customerId, string reason, CancellationToken ct = default);
    Task<VoiceCallResult> HangupByStaffAsync(string callId, StaffCaller staff, string reason, CancellationToken ct = default);
    Task<VoiceCallResult> HangupByCustomerAsync(string callId, string? customerId, string reason, CancellationToken ct = default);
    Task<VoiceCallResult> SignalFromStaffAsync(string callId, StaffCaller staff, string payloadJson, CancellationToken ct = default);
    Task<VoiceCallResult> SignalFromCustomerAsync(string callId, string? customerId, string payloadJson, CancellationToken ct = default);
    Task<VoiceCallResult> UploadChunkAsync(string callId, StaffCaller staff, VoiceTrack track, int sequence, int offsetMs,
        int durationMs, string contentType, byte[] data, CancellationToken ct = default);
    Task<VoiceCall?> GetOpenForStaffAsync(StaffCaller staff, CancellationToken ct = default);
    Task<(VoiceCallView? View, VoiceCallError? Error)> GetViewAsync(string callId, StaffCaller staff, CancellationToken ct = default);
    Task<(VoiceRecordingChunk? Chunk, VoiceCallError? Error)> GetChunkAudioAsync(string callId, string chunkId, StaffCaller staff, CancellationToken ct = default);
    Task<(IceServerConfig? Config, VoiceCallError? Error)> GetIceConfigForStaffAsync(string callId, StaffCaller staff, CancellationToken ct = default);
    Task<(IceServerConfig? Config, VoiceCallError? Error)> GetIceConfigForCustomerAsync(string callId, string? customerId, CancellationToken ct = default);
    Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Zaman aşımı süpürmesi: çalan → Missed (45 sn), parçası kesilen aktif → Failed (60 sn). Kapatılan sayı.</summary>
    Task<int> SweepAsync(CancellationToken ct = default);
}
```

Sinyal yükü sözleşmesi (JSON, `Text` içinde; bütün türler bir `callId` taşır):
- Sunucunun ürettikleri: `{"callId","type":"ring","agentName"}` (→müşteri), `{"callId","type":"accepted"}` (→temsilci), `{"callId","type":"declined","reason"}` (→temsilci), `{"callId","type":"ended","reason","by":"agent|customer|system"}` (→ diğer taraf; `system` ise iki tarafa).
- İstemcinin ilettikleri (sunucu yalnızca `callId`'yi doğrular, gerisini olduğu gibi aktarır): `{"callId","type":"offer|answer|ice","data":…}`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CustomerSupportBot.Application.Tests/Voice/TurnCredentialFactoryTests.cs
// TURN REST API kimliği: coturn use-auth-secret ile aynı hesap (HMAC-SHA1, base64).

using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Services.Voice;

namespace CustomerSupportBot.Application.Tests.Voice;

public class TurnCredentialFactoryTests
{
    [Fact]
    public void Credential_IsHmacSha1OfUsername_AndUsernameCarriesExpiry()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var (user, pass) = TurnCredentialFactory.Create("devturnsecret", "call-1", now, TimeSpan.FromMinutes(10));

        user.Should().Be($"{now.AddMinutes(10).ToUnixTimeSeconds()}:call-1");
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("devturnsecret"));
        pass.Should().Be(Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(user))));
    }
}
```

```csharp
// tests/CustomerSupportBot.Application.Tests/Voice/VoiceCallServiceTests.cs
// Sesli görüşme kuralları: yalnızca insan modunda başlar, temsilci başına tek görüşme, rıza = kabul,
// sinyal yalnızca karşı tarafa, görüşmenin tarafı olmayan reddedilir, zaman aşımları.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Voice;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CustomerSupportBot.Application.Tests.Voice;

public class VoiceCallServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly StaffCaller Elif = new("agent-1", "Elif", false);
    private static readonly StaffCaller Can = new("agent-2", "Can", false);

    private readonly InMemoryVoiceCallStore _calls = new();
    private readonly InMemoryVoiceRecordingStore _recordings = new();
    private readonly IChatBridge _bridge = Substitute.For<IChatBridge>();
    private readonly IChatModeRegistry _modes = Substitute.For<IChatModeRegistry>();
    private readonly ISessionManager _sessions = Substitute.For<ISessionManager>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly VoiceCallOptions _options = new() { Turn = { Urls = ["turn:localhost:3478"], SharedSecret = "s" } };

    private VoiceCallService Service() => new(_calls, _recordings, _bridge, _modes, _sessions,
        new OptionsMonitorStub<VoiceCallOptions>(_options), _time, NullLogger<VoiceCallService>.Instance);

    private void HumanMode(string sid, string customerId = "1001")
    {
        _modes.GetMode(sid).Returns(ChatMode.Human);
        var session = new AgentSession { SessionId = sid };
        session.State.AuthenticatedCustomerId = customerId;
        _sessions.GetAsync(sid, Arg.Any<CancellationToken>()).Returns(session);
    }

    [Fact]
    public async Task Start_RequiresHumanMode()
    {
        _modes.GetMode("s1").Returns(ChatMode.Bot);
        (await Service().StartAsync("s1", Elif, Ct)).Error.Should().Be(VoiceCallError.NotInHumanMode);
    }

    [Fact]
    public async Task Start_RingsCustomer_AndSecondCallByAgentIsBusy()
    {
        HumanMode("s1"); HumanMode("s2");
        var svc = Service();
        var first = await svc.StartAsync("s1", Elif, Ct);
        first.Ok.Should().BeTrue();
        _bridge.Received(1).PublishVoiceSignal("s1", true, Arg.Is<string>(j => j.Contains("\"ring\"") && j.Contains(first.Call!.Id)));

        (await svc.StartAsync("s2", Elif, Ct)).Error.Should().Be(VoiceCallError.Busy);
    }

    [Fact]
    public async Task Accept_ByOwner_RecordsConsent_AndNotifiesAgent()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        var accepted = await svc.AcceptAsync(call.Id, "1001", Ct);
        accepted.Call!.Status.Should().Be(VoiceCallStatus.Active);
        accepted.Call.ConsentAt.Should().NotBeNull();
        _bridge.Received(1).PublishVoiceSignal("s1", false, Arg.Is<string>(j => j.Contains("\"accepted\"")));
    }

    [Fact]
    public async Task Accept_ByOtherCustomer_IsForbidden_AndStateUnchanged()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        (await svc.AcceptAsync(call.Id, "9999", Ct)).Error.Should().Be(VoiceCallError.Forbidden);
        (await _calls.GetAsync(call.Id, Ct))!.Status.Should().Be(VoiceCallStatus.Ringing);
    }

    [Fact]
    public async Task Accept_AfterDecline_IsInvalidState()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.DeclineAsync(call.Id, "1001", VoiceCallEndReasons.Declined, Ct);
        (await svc.AcceptAsync(call.Id, "1001", Ct)).Error.Should().Be(VoiceCallError.InvalidState);
    }

    [Fact]
    public async Task Signal_FromStaff_GoesToCustomer_OnlyForOwnCall()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.AcceptAsync(call.Id, "1001", Ct);
        _bridge.ClearReceivedCalls();

        var payload = $$"""{"callId":"{{call.Id}}","type":"offer","data":{"sdp":"x"}}""";
        (await svc.SignalFromStaffAsync(call.Id, Elif, payload, Ct)).Ok.Should().BeTrue();
        _bridge.Received(1).PublishVoiceSignal("s1", true, payload);

        (await svc.SignalFromStaffAsync(call.Id, Can, payload, Ct)).Error.Should().Be(VoiceCallError.Forbidden);
    }

    [Fact]
    public async Task Signal_WithMismatchedCallId_IsInvalid()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        (await svc.SignalFromCustomerAsync(call.Id, "1001", """{"callId":"other","type":"ice"}""", Ct))
            .Error.Should().Be(VoiceCallError.Invalid);
    }

    [Fact]
    public async Task Hangup_WritesDurationNote_AndAgentCanCallAgain()
    {
        HumanMode("s1"); HumanMode("s2");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.AcceptAsync(call.Id, "1001", Ct);
        _time.Advance(TimeSpan.FromSeconds(252));
        (await svc.HangupByCustomerAsync(call.Id, "1001", VoiceCallEndReasons.CustomerHangup, Ct)).Call!.Status
            .Should().Be(VoiceCallStatus.Ended);
        await _bridge.Received(1).PublishSystemMessageAsync("s1", "Sesli görüşme · 4 dk 12 sn");
        (await svc.StartAsync("s2", Elif, Ct)).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task UploadChunk_OnlyByCallAgent_IdempotentAndSizeLimited()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        await svc.AcceptAsync(call.Id, "1001", Ct);

        (await svc.UploadChunkAsync(call.Id, Elif, VoiceTrack.Customer, 0, 0, 10000, "audio/webm", [1, 2], Ct)).Ok.Should().BeTrue();
        (await svc.UploadChunkAsync(call.Id, Elif, VoiceTrack.Customer, 0, 0, 10000, "audio/webm", [1, 2], Ct)).Ok.Should().BeTrue();
        (await _recordings.ListMetaAsync(call.Id, Ct)).Should().ContainSingle();

        (await svc.UploadChunkAsync(call.Id, Can, VoiceTrack.Agent, 0, 0, 10000, "audio/webm", [1], Ct)).Error.Should().Be(VoiceCallError.Forbidden);
        (await svc.UploadChunkAsync(call.Id, Elif, VoiceTrack.Agent, 1, 10000, 10000, "audio/webm", new byte[_options.MaxChunkBytes + 1], Ct))
            .Error.Should().Be(VoiceCallError.TooLarge);
    }

    [Fact]
    public async Task UploadChunk_BeforeConsent_IsInvalidState()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        (await svc.UploadChunkAsync(call.Id, Elif, VoiceTrack.Agent, 0, 0, 10000, "audio/webm", [1], Ct))
            .Error.Should().Be(VoiceCallError.InvalidState);
    }

    [Fact]
    public async Task Sweep_MissesRingingAfter45s_AndFailsSilentActiveAfter60s()
    {
        HumanMode("s1"); HumanMode("s2");
        var svc = Service();
        var ringing = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        var active = (await svc.StartAsync("s2", Can, Ct)).Call!;
        await svc.AcceptAsync(active.Id, "1001", Ct);

        _time.Advance(TimeSpan.FromSeconds(46));
        (await svc.SweepAsync(Ct)).Should().Be(1);
        (await _calls.GetAsync(ringing.Id, Ct))!.Status.Should().Be(VoiceCallStatus.Missed);

        _time.Advance(TimeSpan.FromSeconds(20));
        (await svc.SweepAsync(Ct)).Should().Be(1);
        var failed = (await _calls.GetAsync(active.Id, Ct))!;
        failed.Status.Should().Be(VoiceCallStatus.Failed);
        failed.EndReason.Should().Be(VoiceCallEndReasons.ConnectionLost);
    }

    [Fact]
    public async Task IceConfig_ForCustomer_RequiresOwnership_AndHasShortLivedTurn()
    {
        HumanMode("s1");
        var svc = Service();
        var call = (await svc.StartAsync("s1", Elif, Ct)).Call!;
        (await svc.GetIceConfigForCustomerAsync(call.Id, "9999", Ct)).Error.Should().Be(VoiceCallError.Forbidden);
        var (config, _) = await svc.GetIceConfigForCustomerAsync(call.Id, "1001", Ct);
        config!.IceServers.Should().Contain(s => s.Urls.Contains("turn:localhost:3478") && s.Username!.EndsWith(":" + call.Id));
        config.IceServers.Should().NotContain(s => s.Credential == "s", "paylaşılan sır asla istemciye gitmez");
    }
}
```

`OptionsMonitorStub<T>`: `tests/CustomerSupportBot.Application.Tests` içinde zaten bir `IOptionsMonitor` ikizi varsa onu kullan; yoksa aynı klasöre ekle:

```csharp
// tests/CustomerSupportBot.Application.Tests/Voice/OptionsMonitorStub.cs
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests.Voice;

internal sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
```

Not: `FakeTimeProvider` için test projesinde `Microsoft.Extensions.TimeProvider.Testing` paketi yoksa ekle: `dotnet add tests/CustomerSupportBot.Application.Tests package Microsoft.Extensions.TimeProvider.Testing`. Application.Tests'in Persistence projesine referansı yoksa (InMemory depolar), `dotnet add tests/CustomerSupportBot.Application.Tests reference src/CustomerSupportBot.Adapters.Persistence` yerine Task 2'deki iki InMemory sınıfını test projesinde **kullanmak için** `tests/CustomerSupportBot.Tests.Shared` referansı üzerinden erişimi kontrol et; erişim yoksa bu testte NSubstitute ile `IVoiceCallStore`/`IVoiceRecordingStore` yerine InMemory kopyalarını test klasörüne almak yerine Persistence referansını ekle (Api.IntegrationTests zaten aynı şeyi yapıyor).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-namespace "*Voice"`
Expected: FAIL (derleme hatası).

- [ ] **Step 3: TurnCredentialFactory**

```csharp
// src/CustomerSupportBot.Application/Services/Voice/TurnCredentialFactory.cs
// TURN REST API (coturn use-auth-secret): kısa ömürlü kullanıcı adı/parola. Kalıcı sır tarayıcıya gitmez.

using System.Security.Cryptography;
using System.Text;

namespace CustomerSupportBot.Application.Services.Voice;

public static class TurnCredentialFactory
{
    public static (string Username, string Credential) Create(string sharedSecret, string callId, DateTimeOffset now, TimeSpan ttl)
    {
        var username = $"{now.Add(ttl).ToUnixTimeSeconds()}:{callId}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(sharedSecret));
        var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
        return (username, credential);
    }
}
```

- [ ] **Step 4: IVoiceCallPort.cs** — yukarıdaki **Interfaces** bloğundaki kayıtları ve arayüzü aynen dosyaya yaz (`using CustomerSupportBot.Domain.Model.Voice;`).

- [ ] **Step 5: VoiceCallService**

```csharp
// src/CustomerSupportBot.Application/Services/Voice/VoiceCallService.cs
// Temsilci–müşteri sesli görüşmesi: durum, yetki, sinyal aktarımı, kayıt parçaları, zaman aşımları.
// Ses buradan geçmez (WebRTC P2P); sinyaller köprüden yönlü ve kalıcılaştırılmadan aktarılır.

using System.Globalization;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Voice;

public sealed class VoiceCallService(
    IVoiceCallStore calls,
    IVoiceRecordingStore recordings,
    IChatBridge bridge,
    IChatModeRegistry modes,
    ISessionManager sessions,
    IOptionsMonitor<VoiceCallOptions> options,
    TimeProvider time,
    ILogger<VoiceCallService> logger) : IVoiceCallPort
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<VoiceCallResult> StartAsync(string sessionId, StaffCaller staff, CancellationToken ct = default)
    {
        if (!options.CurrentValue.Enabled) return new(null, VoiceCallError.Disabled);
        if (modes.GetMode(sessionId) != ChatMode.Human) return new(null, VoiceCallError.NotInHumanMode);

        var call = VoiceCall.Start(sessionId, staff.AgentId, staff.DisplayName, Now);
        if (!await calls.TryCreateAsync(call, ct)) return new(null, VoiceCallError.Busy);

        bridge.PublishVoiceSignal(sessionId, toCustomer: true,
            Json(new { callId = call.Id, type = "ring", agentName = staff.DisplayName }));
        logger.LogInformation("[VoiceCall] Çalıyor call={Call} session={Sid} agent={Agent}", call.Id, sessionId, staff.AgentId);
        return new(call);
    }

    public async Task<VoiceCallResult> AcceptAsync(string callId, string? customerId, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        if (error is not null) return new(null, error);
        return await TransitionAsync(call!, c => c.Accept(Now), ct, onSuccess: c =>
            bridge.PublishVoiceSignal(c.SessionId, toCustomer: false, Json(new { callId = c.Id, type = "accepted" })));
    }

    public async Task<VoiceCallResult> DeclineAsync(string callId, string? customerId, string reason, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        if (error is not null) return new(null, error);
        var why = reason == VoiceCallEndReasons.NoMicrophone ? VoiceCallEndReasons.NoMicrophone : VoiceCallEndReasons.Declined;
        return await TransitionAsync(call!, c => c.Decline(Now, why), ct, onSuccess: c =>
            bridge.PublishVoiceSignal(c.SessionId, toCustomer: false, Json(new { callId = c.Id, type = "declined", reason = why })));
    }

    public async Task<VoiceCallResult> HangupByStaffAsync(string callId, StaffCaller staff, string reason, CancellationToken ct = default)
    {
        var (call, error) = await LoadForStaffActionAsync(callId, staff, ct);
        if (error is not null) return new(null, error);
        return await HangupAsync(call!, NormalizeReason(reason, VoiceCallEndReasons.AgentHangup), by: "agent", ct);
    }

    public async Task<VoiceCallResult> HangupByCustomerAsync(string callId, string? customerId, string reason, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        if (error is not null) return new(null, error);
        return await HangupAsync(call!, NormalizeReason(reason, VoiceCallEndReasons.CustomerHangup), by: "customer", ct);
    }

    public async Task<VoiceCallResult> SignalFromStaffAsync(string callId, StaffCaller staff, string payloadJson, CancellationToken ct = default)
    {
        var (call, error) = await LoadForStaffActionAsync(callId, staff, ct);
        if (error is not null) return new(null, error);
        return Relay(call!, payloadJson, toCustomer: true);
    }

    public async Task<VoiceCallResult> SignalFromCustomerAsync(string callId, string? customerId, string payloadJson, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        if (error is not null) return new(null, error);
        return Relay(call!, payloadJson, toCustomer: false);
    }

    public async Task<VoiceCallResult> UploadChunkAsync(string callId, StaffCaller staff, VoiceTrack track, int sequence,
        int offsetMs, int durationMs, string contentType, byte[] data, CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        if (data.Length == 0 || sequence < 0 || offsetMs < 0 || durationMs <= 0) return new(null, VoiceCallError.Invalid);
        if (data.Length > o.MaxChunkBytes) return new(null, VoiceCallError.TooLarge);

        var call = await calls.GetAsync(callId, ct);
        if (call is null) return new(null, VoiceCallError.NotFound);
        if (call.AgentId != staff.AgentId) return new(null, VoiceCallError.Forbidden);
        var lateOk = call.EndedAt is { } ended && Now - ended <= TimeSpan.FromSeconds(o.LateChunkGraceSeconds) && call.ConsentAt is not null;
        if (call.Status != VoiceCallStatus.Active && !lateOk) return new(null, VoiceCallError.InvalidState);

        await recordings.TryAddAsync(new VoiceRecordingChunk
        {
            CallId = call.Id, SessionId = call.SessionId, Track = track, Sequence = sequence,
            OffsetMs = offsetMs, DurationMs = durationMs, ContentType = contentType, Data = data,
            CreatedAt = Now, NextAttemptAt = Now
        }, ct);
        await calls.TouchChunkAsync(call.Id, Now, ct);
        return new(call);
    }

    public Task<VoiceCall?> GetOpenForStaffAsync(StaffCaller staff, CancellationToken ct = default) =>
        calls.GetOpenForAgentAsync(staff.AgentId, ct);

    public async Task<(VoiceCallView? View, VoiceCallError? Error)> GetViewAsync(string callId, StaffCaller staff, CancellationToken ct = default)
    {
        var call = await calls.GetAsync(callId, ct);
        if (call is null) return (null, VoiceCallError.NotFound);
        if (!staff.IsAdmin && call.AgentId != staff.AgentId) return (null, VoiceCallError.Forbidden);
        var lines = (await recordings.ListMetaAsync(callId, ct))
            .Select(c => new VoiceTranscriptLine(c.Id, TrackName(c.Track), c.OffsetMs, c.TranscriptText, c.TranscriptStatus.ToString()))
            .ToList();
        return (new VoiceCallView(call, lines), null);
    }

    public async Task<(VoiceRecordingChunk? Chunk, VoiceCallError? Error)> GetChunkAudioAsync(string callId, string chunkId, StaffCaller staff, CancellationToken ct = default)
    {
        var call = await calls.GetAsync(callId, ct);
        if (call is null) return (null, VoiceCallError.NotFound);
        if (!staff.IsAdmin && call.AgentId != staff.AgentId) return (null, VoiceCallError.Forbidden);
        var chunk = await recordings.GetAsync(chunkId, ct);
        if (chunk is null || chunk.CallId != callId || chunk.Data.Length == 0) return (null, VoiceCallError.NotFound);
        return (chunk, null);
    }

    public async Task<(IceServerConfig? Config, VoiceCallError? Error)> GetIceConfigForStaffAsync(string callId, StaffCaller staff, CancellationToken ct = default)
    {
        var (call, error) = await LoadForStaffActionAsync(callId, staff, ct);
        return error is not null ? (null, error) : (BuildIceConfig(call!.Id), null);
    }

    public async Task<(IceServerConfig? Config, VoiceCallError? Error)> GetIceConfigForCustomerAsync(string callId, string? customerId, CancellationToken ct = default)
    {
        var (call, error) = await LoadForCustomerAsync(callId, customerId, ct);
        return error is not null ? (null, error) : (BuildIceConfig(call!.Id), null);
    }

    public Task<IReadOnlyList<VoiceCall>> ListForSessionAsync(string sessionId, CancellationToken ct = default) =>
        calls.ListForSessionAsync(sessionId, ct);

    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        var closed = 0;
        foreach (var call in await calls.ListOpenAsync(ct))
        {
            if (call.Status == VoiceCallStatus.Ringing && Now - call.CreatedAt >= TimeSpan.FromSeconds(o.RingTimeoutSeconds))
            {
                var r = await TransitionAsync(call, c => c.Miss(Now), ct, onSuccess: c => NotifyBoth(c, "missed"));
                if (r.Ok) closed++;
            }
            else if (call.Status == VoiceCallStatus.Active
                     && Now - (call.LastChunkAt ?? call.AnsweredAt ?? call.CreatedAt) >= TimeSpan.FromSeconds(o.ChunkStaleSeconds))
            {
                var r = await HangupAsync(call, VoiceCallEndReasons.ConnectionLost, by: "system", ct);
                if (r.Ok) closed++;
            }
        }
        return closed;
    }

    // ── yardımcılar ───────────────────────────────────────────────────────────

    private async Task<VoiceCallResult> HangupAsync(VoiceCall call, string reason, string by, CancellationToken ct)
    {
        if (!call.IsOpen) return new(call);   // iki taraf aynı anda kapattı — sessizce kabul
        var wasActive = call.Status == VoiceCallStatus.Active;
        return await TransitionAsync(call, c => c.Hangup(Now, reason), ct, onSuccess: c =>
        {
            var payload = Json(new { callId = c.Id, type = "ended", reason, by });
            if (by != "customer") bridge.PublishVoiceSignal(c.SessionId, toCustomer: true, payload);
            if (by != "agent") bridge.PublishVoiceSignal(c.SessionId, toCustomer: false, payload);
            if (wasActive && c.Duration is { } d)
                _ = bridge.PublishSystemMessageAsync(c.SessionId, $"Sesli görüşme · {FormatDuration(d)}");
        });
    }

    private async Task<VoiceCallResult> TransitionAsync(VoiceCall call, Action<VoiceCall> change, CancellationToken ct, Action<VoiceCall> onSuccess)
    {
        var expected = call.Status;
        try { change(call); }
        catch (VoiceCallStateException) { return new(null, VoiceCallError.InvalidState); }

        if (!await calls.TryUpdateAsync(call, expected, ct)) return new(null, VoiceCallError.InvalidState);
        onSuccess(call);
        return new(call);
    }

    private VoiceCallResult Relay(VoiceCall call, string payloadJson, bool toCustomer)
    {
        if (!call.IsOpen) return new(null, VoiceCallError.InvalidState);
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("callId", out var id) || id.GetString() != call.Id
                || !doc.RootElement.TryGetProperty("type", out var type) || type.GetString() is not ("offer" or "answer" or "ice"))
                return new(null, VoiceCallError.Invalid);
        }
        catch (JsonException) { return new(null, VoiceCallError.Invalid); }

        bridge.PublishVoiceSignal(call.SessionId, toCustomer, payloadJson);
        return new(call);
    }

    private void NotifyBoth(VoiceCall call, string type)
    {
        var payload = Json(new { callId = call.Id, type = "ended", reason = type, by = "system" });
        bridge.PublishVoiceSignal(call.SessionId, toCustomer: true, payload);
        bridge.PublishVoiceSignal(call.SessionId, toCustomer: false, payload);
    }

    private async Task<(VoiceCall? Call, VoiceCallError? Error)> LoadForCustomerAsync(string callId, string? customerId, CancellationToken ct)
    {
        var call = await calls.GetAsync(callId, ct);
        if (call is null) return (null, VoiceCallError.NotFound);
        var session = await sessions.GetAsync(call.SessionId, ct);
        if (string.IsNullOrEmpty(customerId) || session is null
            || !string.Equals(session.State.AuthenticatedCustomerId, customerId, StringComparison.Ordinal))
            return (null, VoiceCallError.Forbidden);
        return (call, null);
    }

    private async Task<(VoiceCall? Call, VoiceCallError? Error)> LoadForStaffActionAsync(string callId, StaffCaller staff, CancellationToken ct)
    {
        var call = await calls.GetAsync(callId, ct);
        if (call is null) return (null, VoiceCallError.NotFound);
        // Görüşmeyi yalnızca başlatan temsilci yürütür; yönetici dinleyebilir (GetView) ama yürütemez.
        return call.AgentId == staff.AgentId ? (call, null) : (null, VoiceCallError.Forbidden);
    }

    private IceServerConfig BuildIceConfig(string callId)
    {
        var o = options.CurrentValue;
        var servers = new List<IceServer>();
        if (o.StunUrls.Count > 0) servers.Add(new IceServer(o.StunUrls));
        if (o.Turn.Urls.Count > 0 && !string.IsNullOrWhiteSpace(o.Turn.SharedSecret))
        {
            var (user, cred) = TurnCredentialFactory.Create(o.Turn.SharedSecret, callId, time.GetUtcNow(),
                TimeSpan.FromMinutes(o.Turn.CredentialTtlMinutes));
            servers.Add(new IceServer(o.Turn.Urls, user, cred));
        }
        return new IceServerConfig(servers);
    }

    private static string NormalizeReason(string? reason, string fallback) => reason switch
    {
        VoiceCallEndReasons.ConnectionLost or VoiceCallEndReasons.ConnectFailed => reason,
        _ => fallback
    };

    public static string TrackName(VoiceTrack track) => track == VoiceTrack.Agent ? "agent" : "customer";

    internal static string FormatDuration(TimeSpan d) =>
        d.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalMinutes} dk {d.Seconds} sn")
            : string.Create(CultureInfo.InvariantCulture, $"{d.Seconds} sn");

    private static string Json(object o) => JsonSerializer.Serialize(o);
}
```

DI (`ApplicationServiceCollectionExtensions.cs`):

```csharp
        services.Configure<VoiceCallOptions>(configuration.GetSection(VoiceCallOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IVoiceCallPort, Services.Voice.VoiceCallService>();
```

(`TryAddSingleton` için `using Microsoft.Extensions.DependencyInjection.Extensions;`; projede `TimeProvider` zaten kayıtlıysa bu satırı ekleme.)

- [ ] **Step 6: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-namespace "*Voice"`
Expected: PASS (13 test).

- [ ] **Step 7: Commit**

```bash
git add src/CustomerSupportBot.Application tests/CustomerSupportBot.Application.Tests
git commit -m "feat(voice-call): voice call service, signaling relay and TURN credentials"
```

---

### Task 6: API uçları ve SSE sinyal olayları

**Files:**
- Create: `src/CustomerSupportBot.Api/Endpoints/VoiceCallEndpoints.cs`
- Modify: `src/CustomerSupportBot.Api/Program.cs` (eşleme)
- Modify: `src/CustomerSupportBot.Api/Services/ChatEventOrchestrator.cs` (müşteri SSE: `voice_signal`)
- Modify: `src/CustomerSupportBot.Api/Endpoints/AgentPanelEndpoints.cs`, `src/CustomerSupportBot.Api/Endpoints/AdminEndpoints.cs` (personel SSE: `voice_signal` + meta alanları)
- Modify: `StreamEventTypes` sabitleri (`VoiceSignal = "voice_signal"`) — dosyayı `grep -rn "BridgeMessage =" src/CustomerSupportBot.Api` ile bul.
- Modify: `src/CustomerSupportBot.Api/appsettings.json` (`VoiceCall` bölümü)
- Test: `tests/CustomerSupportBot.Api.IntegrationTests/VoiceCallEndpointTests.cs`

**Interfaces:**
- Consumes: `IVoiceCallPort` (Task 5).
- Produces (HTTP):

| Yöntem | Yol | Politika | Gövde/Sorgu | Yanıt |
|---|---|---|---|---|
| POST | `/chat-sessions/{sid}/voice-calls` | AdminOrAgent | — | 201 `VoiceCallDto` / 409 `voice_call_busy` / 409 `not_in_human_mode` |
| GET | `/voice-calls/mine` | AdminOrAgent | — | 200 `VoiceCallDto` / 204 |
| POST | `/voice-calls/{id}/signal` | AdminOrAgent | JSON yük | 204 |
| POST | `/voice-calls/{id}/hangup` | AdminOrAgent | `?reason=` | 200 `VoiceCallDto` |
| POST | `/voice-calls/{id}/chunks` | AdminOrAgent | ham ses; `?track=agent|customer&seq&offsetMs&durationMs` | 204 |
| GET | `/voice-calls/{id}` | AdminOrAgent | — | 200 `{ call, lines[] }` |
| GET | `/voice-calls/{id}/chunks/{chunkId}` | AdminOrAgent | — | ses dosyası |
| GET | `/voice-calls/{id}/ice-config` | AdminOrAgent | — | `{ iceServers: [{ urls, username?, credential? }] }` |
| GET | `/chat-sessions/{sid}/voice-calls` | AdminOrAgent | — | görüşme listesi |
| POST | `/chat/voice-calls/{id}/accept` | Customer | — | 200 `VoiceCallDto` |
| POST | `/chat/voice-calls/{id}/decline` | Customer | `?reason=` | 200 |
| POST | `/chat/voice-calls/{id}/signal` | Customer | JSON yük | 204 |
| POST | `/chat/voice-calls/{id}/hangup` | Customer | `?reason=` | 200 |
| GET | `/chat/voice-calls/{id}/ice-config` | Customer | — | ice config |

`VoiceCallDto`: `{ id, sessionId, agentDisplayName, status (küçük harf), createdAt, answeredAt, endedAt, endReason, durationSeconds }`. Hata gövdesi: `{ error: "voice_call_busy" | "not_in_human_mode" | "invalid_state" | "forbidden" | "not_found" | "invalid" | "too_large" | "disabled" }` — durum kodları: Busy/NotInHumanMode/InvalidState → 409, Forbidden → 403, NotFound → 404, Invalid → 400, TooLarge → 413, Disabled → 503.

SSE: müşteri olay akışında `msg.Sender == VoiceSignal` → olay adı `voice_signal`, veri `msg.Text` (ham JSON). Personel akışında (`/chat-sessions/{sid}/subscribe` hem admin hem agent) aynı: `VoiceSignal` → `voice_signal`; diğer mesajlarda `bridge_message` yüküne `voiceCallId`, `voiceTrack`, `offsetMs` eklenir.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CustomerSupportBot.Api.IntegrationTests/VoiceCallEndpointTests.cs
// Uç yetkileri ve durum kodları: personel başlatır, müşteri kabul eder, başka müşteri 403, ikinci arama 409,
// parça yükleme yalnız görüşmenin temsilcisi.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class VoiceCallEndpointTests : IClassFixture<VoiceCallEndpointTests.Factory>
{
    public sealed class Factory : TestWebApplicationFactory
    {
        public IChatModeRegistry Modes { get; } = Substitute.For<IChatModeRegistry>();
        public ISessionManager Sessions { get; } = Substitute.For<ISessionManager>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVoiceCallStore>();
                services.AddSingleton<IVoiceCallStore, InMemoryVoiceCallStore>();
                services.RemoveAll<IVoiceRecordingStore>();
                services.AddSingleton<IVoiceRecordingStore, InMemoryVoiceRecordingStore>();
                services.RemoveAll<IChatModeRegistry>();
                services.AddSingleton(Modes);
                services.RemoveAll<ISessionManager>();
                services.AddSingleton(Sessions);
            });
        }
    }

    private readonly Factory _factory;
    public VoiceCallEndpointTests(Factory factory) => _factory = factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string role, string? agentId = null, string? customerId = null)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<IJwtAccessTokenProvider>().GenerateAccessToken(
            new UserInfo(Guid.NewGuid().ToString("N"), $"u-{Guid.NewGuid():N}", "", role,
                agentId, true, DateTime.UtcNow, null, customerId),
            DateTime.UtcNow).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string HumanSession(string customerId = "1001")
    {
        var sid = $"s-{Guid.NewGuid():N}";
        _factory.Modes.GetMode(sid).Returns(ChatMode.Human);
        var session = new AgentSession { SessionId = sid };
        session.State.AuthenticatedCustomerId = customerId;
        _factory.Sessions.GetAsync(sid, Arg.Any<CancellationToken>()).Returns(session);
        return sid;
    }

    private static async Task<string> IdOf(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;

    [Fact]
    public async Task Start_Accept_Upload_Hangup_HappyPath()
    {
        var agent = ClientFor("Agent", agentId: $"agent-{Guid.NewGuid():N}");
        var customer = ClientFor("Customer", customerId: "1001");
        var sid = HumanSession();

        var start = await agent.PostAsync($"/chat-sessions/{sid}/voice-calls", null, Ct);
        start.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await IdOf(start);

        (await customer.PostAsync($"/chat/voice-calls/{id}/accept", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var body = new ByteArrayContent([1, 2, 3]);
        body.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        (await agent.PostAsync($"/voice-calls/{id}/chunks?track=customer&seq=0&offsetMs=0&durationMs=10000", body, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hang = await agent.PostAsync($"/voice-calls/{id}/hangup", null, Ct);
        (await hang.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().Should().Be("ended");
    }

    [Fact]
    public async Task SecondCall_ByBusyAgent_Is409()
    {
        var agentId = $"agent-{Guid.NewGuid():N}";
        var agent = ClientFor("Agent", agentId: agentId);
        (await agent.PostAsync($"/chat-sessions/{HumanSession()}/voice-calls", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await agent.PostAsync($"/chat-sessions/{HumanSession()}/voice-calls", null, Ct);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString().Should().Be("voice_call_busy");
    }

    [Fact]
    public async Task OtherCustomer_CannotAccept_Or_Signal()
    {
        var agent = ClientFor("Agent", agentId: $"agent-{Guid.NewGuid():N}");
        var id = await IdOf(await agent.PostAsync($"/chat-sessions/{HumanSession("1001")}/voice-calls", null, Ct));
        var stranger = ClientFor("Customer", customerId: "9999");
        (await stranger.PostAsync($"/chat/voice-calls/{id}/accept", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostAsJsonAsync($"/chat/voice-calls/{id}/signal", new { callId = id, type = "ice" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OtherAgent_CannotUploadOrHangup()
    {
        var owner = ClientFor("Agent", agentId: $"agent-{Guid.NewGuid():N}");
        var id = await IdOf(await owner.PostAsync($"/chat-sessions/{HumanSession()}/voice-calls", null, Ct));
        var other = ClientFor("Agent", agentId: $"agent-{Guid.NewGuid():N}");
        (await other.PostAsync($"/voice-calls/{id}/hangup", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_CannotUseStaffEndpoints()
    {
        var customer = ClientFor("Customer", customerId: "1001");
        (await customer.PostAsync($"/chat-sessions/{HumanSession()}/voice-calls", null, Ct)).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}
```

(`UserInfo` kurucu parametre sırası `ChatAttachmentEndpointTests`'tekiyle aynıdır: beşinci parametre bağlı temsilci kimliği, sonuncusu müşteri kimliği. `linked_agent_id` claim'i bu alandan üretilir.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/CustomerSupportBot.Api.IntegrationTests -- --filter-class "*VoiceCallEndpointTests"`
Expected: FAIL (404'ler / derleme).

- [ ] **Step 3: VoiceCallEndpoints**

```csharp
// src/CustomerSupportBot.Api/Endpoints/VoiceCallEndpoints.cs
// Temsilci–müşteri sesli görüşmesi. Personel uçları "AdminOrAgent" grubunda (Program.cs), müşteri uçları
// "Customer" politikasıyla. Ses bu uçlardan geçmez — yalnızca sinyal ve kayıt parçaları.

using System.Security.Claims;
using System.Text;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportBot.Api.Endpoints;

public static class VoiceCallEndpoints
{
    private const long MaxChunkRequestBytes = 3 * 1024 * 1024;
    private const long MaxSignalBytes = 64 * 1024;

    /// <summary>Personel uçları — AdminOrAgent grubuna eşlenir.</summary>
    public static IEndpointRouteBuilder MapStaffVoiceCallEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat-sessions/{sid}/voice-calls", async (string sid, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.StartAsync(sid, Staff(ctx), ct);
            return r.Ok ? Results.Created($"/voice-calls/{r.Call!.Id}", Dto(r.Call)) : Error(r.Error!.Value);
        });

        app.MapGet("/chat-sessions/{sid}/voice-calls", async (string sid, IVoiceCallPort port, CancellationToken ct) =>
            Results.Ok((await port.ListForSessionAsync(sid, ct)).Select(Dto)));

        app.MapGet("/voice-calls/mine", async (HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
            await port.GetOpenForStaffAsync(Staff(ctx), ct) is { } call ? Results.Ok(Dto(call)) : Results.NoContent());

        app.MapPost("/voice-calls/{id}/signal", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var payload = await ReadBodyAsync(ctx.Request, MaxSignalBytes, ct);
            if (payload is null) return Error(VoiceCallError.Invalid);
            var r = await port.SignalFromStaffAsync(id, Staff(ctx), payload, ct);
            return r.Ok ? Results.NoContent() : Error(r.Error!.Value);
        });

        app.MapPost("/voice-calls/{id}/hangup", async (string id, string? reason, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.HangupByStaffAsync(id, Staff(ctx), reason ?? "", ct);
            return r.Ok ? Results.Ok(Dto(r.Call!)) : Error(r.Error!.Value);
        });

        app.MapPost("/voice-calls/{id}/chunks", async (string id, string track, int seq, int offsetMs, int durationMs,
            HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            if (!Enum.TryParse<VoiceTrack>(track, ignoreCase: true, out var t)) return Error(VoiceCallError.Invalid);
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ct);
            var contentType = ctx.Request.ContentType ?? "audio/webm";
            if (!contentType.StartsWith("audio/webm", StringComparison.OrdinalIgnoreCase)
                && !contentType.StartsWith("audio/ogg", StringComparison.OrdinalIgnoreCase))
                return Error(VoiceCallError.Invalid);
            var r = await port.UploadChunkAsync(id, Staff(ctx), t, seq, offsetMs, durationMs, contentType, ms.ToArray(), ct);
            return r.Ok ? Results.NoContent() : Error(r.Error!.Value);
        }).WithMetadata(new RequestSizeLimitAttribute(MaxChunkRequestBytes));

        app.MapGet("/voice-calls/{id}", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var (view, error) = await port.GetViewAsync(id, Staff(ctx), ct);
            return view is null ? Error(error!.Value) : Results.Ok(new
            {
                call = Dto(view.Call),
                lines = view.Lines.Select(l => new { chunkId = l.ChunkId, track = l.Track, offsetMs = l.OffsetMs, text = l.Text, status = l.Status.ToLowerInvariant() })
            });
        });

        app.MapGet("/voice-calls/{id}/chunks/{chunkId}", async (string id, string chunkId, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var (chunk, error) = await port.GetChunkAudioAsync(id, chunkId, Staff(ctx), ct);
            return chunk is null ? Error(error!.Value) : Results.File(chunk.Data, chunk.ContentType);
        });

        app.MapGet("/voice-calls/{id}/ice-config", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var (config, error) = await port.GetIceConfigForStaffAsync(id, Staff(ctx), ct);
            return config is null ? Error(error!.Value) : Results.Ok(IceDto(config));
        });

        return app;
    }

    /// <summary>Müşteri uçları.</summary>
    public static IEndpointRouteBuilder MapCustomerVoiceCallEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/chat/voice-calls").RequireAuthorization("Customer").RequireRateLimiting("general");

        g.MapPost("/{id}/accept", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.AcceptAsync(id, CustomerId(ctx), ct);
            return r.Ok ? Results.Ok(Dto(r.Call!)) : Error(r.Error!.Value);
        });

        g.MapPost("/{id}/decline", async (string id, string? reason, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.DeclineAsync(id, CustomerId(ctx), reason ?? VoiceCallEndReasons.Declined, ct);
            return r.Ok ? Results.Ok(Dto(r.Call!)) : Error(r.Error!.Value);
        });

        g.MapPost("/{id}/signal", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var payload = await ReadBodyAsync(ctx.Request, MaxSignalBytes, ct);
            if (payload is null) return Error(VoiceCallError.Invalid);
            var r = await port.SignalFromCustomerAsync(id, CustomerId(ctx), payload, ct);
            return r.Ok ? Results.NoContent() : Error(r.Error!.Value);
        });

        g.MapPost("/{id}/hangup", async (string id, string? reason, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var r = await port.HangupByCustomerAsync(id, CustomerId(ctx), reason ?? "", ct);
            return r.Ok ? Results.Ok(Dto(r.Call!)) : Error(r.Error!.Value);
        });

        g.MapGet("/{id}/ice-config", async (string id, HttpContext ctx, IVoiceCallPort port, CancellationToken ct) =>
        {
            var (config, error) = await port.GetIceConfigForCustomerAsync(id, CustomerId(ctx), ct);
            return config is null ? Error(error!.Value) : Results.Ok(IceDto(config));
        });

        return app;
    }

    // ── yardımcılar ───────────────────────────────────────────────────────────

    /// <summary>
    /// Temsilci kimliği: Agent rolünde bağlı kayıt (<c>linked_agent_id</c>); yönetici için kullanıcı kimliği
    /// (<c>user:{id}</c>) — yöneticinin de aynı anda tek görüşmesi olur.
    /// </summary>
    private static StaffCaller Staff(HttpContext ctx)
    {
        var u = ctx.User;
        var isAdmin = u.IsInRole("Admin");
        var linked = u.FindFirstValue("linked_agent_id");
        var agentId = !string.IsNullOrEmpty(linked) ? linked : $"user:{u.FindFirstValue(ClaimTypes.NameIdentifier)}";
        var name = u.FindFirstValue(ClaimTypes.GivenName) ?? u.FindFirstValue(ClaimTypes.Name) ?? "Temsilci";
        return new StaffCaller(agentId, name, isAdmin);
    }

    private static string? CustomerId(HttpContext ctx) => ctx.User.FindFirst("linked_customer_id")?.Value;

    private static async Task<string?> ReadBodyAsync(HttpRequest request, long max, CancellationToken ct)
    {
        if (request.ContentLength > max) return null;
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(ct);
        return text.Length == 0 || text.Length > max ? null : text;
    }

    private static object Dto(VoiceCall c) => new
    {
        id = c.Id,
        sessionId = c.SessionId,
        agentDisplayName = c.AgentDisplayName,
        status = c.Status.ToString().ToLowerInvariant(),
        createdAt = c.CreatedAt,
        answeredAt = c.AnsweredAt,
        endedAt = c.EndedAt,
        endReason = c.EndReason,
        durationSeconds = c.Duration is { } d ? (int?)d.TotalSeconds : null
    };

    private static object IceDto(IceServerConfig c) => new
    {
        iceServers = c.IceServers.Select(s => new { urls = s.Urls, username = s.Username, credential = s.Credential })
    };

    private static IResult Error(VoiceCallError error) => error switch
    {
        VoiceCallError.Busy => Results.Conflict(new { error = "voice_call_busy" }),
        VoiceCallError.NotInHumanMode => Results.Conflict(new { error = "not_in_human_mode" }),
        VoiceCallError.InvalidState => Results.Conflict(new { error = "invalid_state" }),
        VoiceCallError.Forbidden => Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden),
        VoiceCallError.NotFound => Results.NotFound(new { error = "not_found" }),
        VoiceCallError.TooLarge => Results.Json(new { error = "too_large" }, statusCode: StatusCodes.Status413PayloadTooLarge),
        VoiceCallError.Disabled => Results.Json(new { error = "disabled" }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.BadRequest(new { error = "invalid" })
    };
}
```

Not: `ClaimTypes.GivenName` personel token'ında yoksa `Name` kullanılır; temsilcinin görünen adı için `IHumanAgentPort.GetAgent(agentId)?.DisplayName` tercih edilir — `Staff` yardımcısına `IHumanAgentPort` alıp `agents.GetAgent(linked)?.DisplayName ?? name` yap (AgentPanelEndpoints'teki `agentLabel` deseni). Her uçta `IHumanAgentPort agents` parametresi ekleyip `Staff(ctx, agents)` çağır.

`Program.cs`: `app.MapChatAttachmentEndpoints();` altına `app.MapCustomerVoiceCallEndpoints();`; `agentScope.MapAgentConversationClosingEndpoints();` altına `agentScope.MapStaffVoiceCallEndpoints();`. `using CustomerSupportBot.Api.Endpoints;` zaten var.

- [ ] **Step 4: SSE olayları**

`StreamEventTypes` sınıfına: `public const string VoiceSignal = "voice_signal";`

`ChatEventOrchestrator.cs` döngüsünde (`if (msg.Sender == ChatBridgeSender.BotTyping) { … continue; }` bloğundan sonra):

```csharp
                if (msg.Sender == ChatBridgeSender.VoiceSignal)
                {
                    await write(StreamEventTypes.VoiceSignal, JsonDocument.Parse(msg.Text).RootElement.Clone());
                    continue;
                }
```

(`write` delegesinin imzası nesne kabul ediyorsa `JsonElement` doğrudan serileşir; `using System.Text.Json;` ekle.)

`AgentPanelEndpoints.cs` (~400) ve `AdminEndpoints.cs`'teki `/chat-sessions/{sid}/subscribe` döngülerinde, `WriteEventAsync(…BridgeMessage…)` çağrısından önce:

```csharp
                    if (msg.Sender == ChatBridgeSender.VoiceSignal)
                    {
                        await SseWriter.WriteEventAsync(response, StreamEventTypes.VoiceSignal,
                            JsonDocument.Parse(msg.Text).RootElement.Clone(), ct);
                        continue;
                    }
```

ve `BridgeMessage` anonim yüküne `voiceCallId = msg.VoiceCallId, voiceTrack = msg.VoiceTrack, offsetMs = msg.OffsetMs,` ekle.

- [ ] **Step 5: appsettings**

`src/CustomerSupportBot.Api/appsettings.json` kök nesnesine:

```json
  "VoiceCall": {
    "Enabled": true,
    "RingTimeoutSeconds": 45,
    "ChunkStaleSeconds": 60,
    "MaxChunkBytes": 2097152,
    "TranscriptionModel": "gpt-4o-transcribe",
    "TranscriptionLanguage": "tr",
    "TranscriptionMaxAttempts": 3,
    "StunUrls": [ "stun:stun.l.google.com:19302" ],
    "Turn": {
      "Urls": [ "turn:localhost:3478?transport=udp", "turn:localhost:3478?transport=tcp" ],
      "SharedSecret": "aibot-dev-turn-secret",
      "CredentialTtlMinutes": 10
    }
  },
```

ve `DataRetention` bölümüne `"VoiceRecordingRetentionDays": 90`.

- [ ] **Step 6: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Api.IntegrationTests -- --filter-class "*VoiceCallEndpointTests"` → PASS (5 test). Ardından tüm API testleri: `dotnet test --project tests/CustomerSupportBot.Api.IntegrationTests` → PASS.

- [ ] **Step 7: Commit**

```bash
git add src/CustomerSupportBot.Api tests/CustomerSupportBot.Api.IntegrationTests/VoiceCallEndpointTests.cs
git commit -m "feat(voice-call): staff and customer voice call endpoints, SSE voice_signal"
```

---

### Task 7: Döküm adaptörü, döküm işlemcisi ve arka plan işi

**Files:**
- Create: `src/CustomerSupportBot.Adapters.AI/Audio/OpenAiAudioTranscriber.cs`
- Modify: `src/CustomerSupportBot.Api/Extensions/AiServicesExtensions.cs` (kayıt)
- Create: `src/CustomerSupportBot.Application/Services/Voice/VoiceTranscriptionProcessor.cs`
- Create: `src/CustomerSupportBot.Api/Workers/VoiceCallWorker.cs`
- Modify: `src/CustomerSupportBot.Api/Extensions/ApplicationServicesExtensions.cs` (`AddHostedService<VoiceCallWorker>()`)
- Modify: `src/CustomerSupportBot.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs` (`AddSingleton<VoiceTranscriptionProcessor>()`)
- Test: `tests/CustomerSupportBot.Application.Tests/Voice/VoiceTranscriptionProcessorTests.cs`

**Interfaces:**
- Consumes: `IVoiceRecordingStore`, `IVoiceCallStore`, `IChatBridge.PublishVoiceTranscriptAsync`, `ISessionManager.AppendUserMessageAsync/AppendAssistantMessageAsync`, `IAudioTranscriber`, `ILlmSpendGuard.CheckAsync(string? sessionId, CancellationToken)` (null = limit içinde), `IVoiceCallPort.SweepAsync`.
- Produces: `VoiceTranscriptionProcessor.ProcessNextAsync(CancellationToken) : Task<bool>` — bir parça işlediyse `true`.

Davranış:
- Parçayı sahiplen → bütçe doluysa `PostponeAsync(+1 dk)`, `true` dön.
- Dök → boş/boşluk ise `CompleteAsync(id, null)`, satır yok.
- Dolu ise `CompleteAsync(id, text)`; satır metni `Müşteri: {text}` ya da `Temsilci ({call.AgentDisplayName}): {text}`; `PublishVoiceTranscriptAsync(sessionId, callId, "customer"|"agent", offsetMs, satır)`; müşteri izi → `AppendUserMessageAsync(sessionId, text)`, temsilci izi → `AppendAssistantMessageAsync(sessionId, text)` (duygu analizi ve bota dönüşte bağlam).
- Hata → `Attempts+1 >= TranscriptionMaxAttempts` ise `FailAttemptAsync(final: true)` ve satır `"{Konuşan}: (döküm alınamadı)"`; değilse `FailAttemptAsync(now + 2^attempts * 10 sn, final: false)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CustomerSupportBot.Application.Tests/Voice/VoiceTranscriptionProcessorTests.cs
// Döküm: konuşan etiketiyle yalnız temsilciye giden satır, geçmişe ekleme, boş parça satırsız,
// 3 başarısız denemeden sonra "(döküm alınamadı)", bütçe doluyken deneme harcanmadan ertelenir.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Voice;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CustomerSupportBot.Application.Tests.Voice;

public class VoiceTranscriptionProcessorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly InMemoryVoiceCallStore _calls = new();
    private readonly InMemoryVoiceRecordingStore _recordings = new();
    private readonly IChatBridge _bridge = Substitute.For<IChatBridge>();
    private readonly ISessionManager _sessions = Substitute.For<ISessionManager>();
    private readonly IAudioTranscriber _transcriber = Substitute.For<IAudioTranscriber>();
    private readonly ILlmSpendGuard _budget = Substitute.For<ILlmSpendGuard>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    private VoiceTranscriptionProcessor Processor() => new(_recordings, _calls, _bridge, _sessions, _transcriber, _budget,
        new OptionsMonitorStub<VoiceCallOptions>(new VoiceCallOptions()), _time, NullLogger<VoiceTranscriptionProcessor>.Instance);

    private async Task<(VoiceCall Call, VoiceRecordingChunk Chunk)> SeedAsync(VoiceTrack track)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var call = VoiceCall.Start("s1", "agent-1", "Elif", now);
        call.Accept(now);
        await _calls.TryCreateAsync(call, Ct);
        var chunk = new VoiceRecordingChunk { CallId = call.Id, SessionId = "s1", Track = track, Sequence = 2, OffsetMs = 20000, DurationMs = 10000, Data = [1], CreatedAt = now, NextAttemptAt = now };
        await _recordings.TryAddAsync(chunk, Ct);
        return (call, chunk);
    }

    [Fact]
    public async Task CustomerChunk_PublishesLabelledLine_AndAppendsUserMessage()
    {
        var (call, chunk) = await SeedAsync(VoiceTrack.Customer);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(" Kargom gelmedi. ");

        (await Processor().ProcessNextAsync(Ct)).Should().BeTrue();

        await _bridge.Received(1).PublishVoiceTranscriptAsync("s1", call.Id, "customer", 20000, "Müşteri: Kargom gelmedi.");
        await _sessions.Received(1).AppendUserMessageAsync("s1", "Kargom gelmedi.", Arg.Any<CancellationToken>());
        (await _recordings.GetAsync(chunk.Id, Ct))!.TranscriptStatus.Should().Be(VoiceTranscriptStatus.Done);
    }

    [Fact]
    public async Task AgentChunk_UsesAgentName_AndAppendsAssistantMessage()
    {
        var (call, _) = await SeedAsync(VoiceTrack.Agent);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Hemen bakıyorum.");

        await Processor().ProcessNextAsync(Ct);

        await _bridge.Received(1).PublishVoiceTranscriptAsync("s1", call.Id, "agent", 20000, "Temsilci (Elif): Hemen bakıyorum.");
        await _sessions.Received(1).AppendAssistantMessageAsync("s1", "Hemen bakıyorum.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SilentChunk_ProducesNoLine()
    {
        await SeedAsync(VoiceTrack.Customer);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("   ");
        await Processor().ProcessNextAsync(Ct);
        await _bridge.DidNotReceiveWithAnyArgs().PublishVoiceTranscriptAsync(default!, default!, default!, default, default!);
    }

    [Fact]
    public async Task ThreeFailures_MarkFailed_AndPublishPlaceholder()
    {
        var (call, chunk) = await SeedAsync(VoiceTrack.Customer);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("503"));
        var p = Processor();

        for (var i = 0; i < 3; i++)
        {
            (await p.ProcessNextAsync(Ct)).Should().BeTrue();
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        (await _recordings.GetAsync(chunk.Id, Ct))!.TranscriptStatus.Should().Be(VoiceTranscriptStatus.Failed);
        await _bridge.Received(1).PublishVoiceTranscriptAsync("s1", call.Id, "customer", 20000, "Müşteri: (döküm alınamadı)");
    }

    [Fact]
    public async Task BudgetExceeded_PostponesWithoutSpendingAttempt()
    {
        var (_, chunk) = await SeedAsync(VoiceTrack.Customer);
        _budget.CheckAsync("s1", Arg.Any<CancellationToken>())
            .Returns(new LlmBudgetExceeded(LlmBudgetScope.Daily, 10m, 10m));

        await Processor().ProcessNextAsync(Ct);

        var after = (await _recordings.GetAsync(chunk.Id, Ct))!;
        after.Attempts.Should().Be(0);
        after.TranscriptStatus.Should().Be(VoiceTranscriptStatus.Pending);
        await _transcriber.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, default!, default);
    }

    [Fact]
    public async Task DuplicateUpload_YieldsSingleLine()
    {
        var (call, chunk) = await SeedAsync(VoiceTrack.Customer);
        await _recordings.TryAddAsync(new VoiceRecordingChunk { CallId = call.Id, SessionId = "s1", Track = VoiceTrack.Customer, Sequence = 2, Data = [1], CreatedAt = chunk.CreatedAt, NextAttemptAt = chunk.CreatedAt }, Ct);
        _transcriber.TranscribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("Merhaba");
        var p = Processor();
        await p.ProcessNextAsync(Ct);
        (await p.ProcessNextAsync(Ct)).Should().BeFalse();
        await _bridge.Received(1).PublishVoiceTranscriptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
    }
}
```

`LlmBudgetExceeded` / `LlmBudgetScope` adları için `ChatPortService`'teki `exceeded.Scope`, `exceeded.SpentUsd`, `exceeded.LimitUsd` kullanımına bak; kaydın gerçek adı ve kurucu sırası neyse (`grep -rn "record .*Exceeded" src/CustomerSupportBot.Application`) testte onu kullan.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class "*VoiceTranscriptionProcessorTests"`
Expected: FAIL (derleme hatası).

- [ ] **Step 3: Processor**

```csharp
// src/CustomerSupportBot.Application/Services/Voice/VoiceTranscriptionProcessor.cs
// Kayıt parçalarını sırayla yazıya döker; satırı konuşan etiketiyle yalnız temsilci tarafına yayınlar ve
// konuşma geçmişine ekler (duygu analizi + bota dönüşte bağlam). Çok pod'da sahiplenme depoda.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Voice;

public sealed class VoiceTranscriptionProcessor(
    IVoiceRecordingStore recordings,
    IVoiceCallStore calls,
    IChatBridge bridge,
    ISessionManager sessions,
    IAudioTranscriber transcriber,
    ILlmSpendGuard budget,
    IOptionsMonitor<VoiceCallOptions> options,
    TimeProvider time,
    ILogger<VoiceTranscriptionProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var chunk = await recordings.TryClaimNextPendingAsync(now, ct);
        if (chunk is null) return false;

        if (await budget.CheckAsync(chunk.SessionId, ct) is not null)
        {
            await recordings.PostponeAsync(chunk.Id, now.AddMinutes(1), ct);
            return true;
        }

        var call = await calls.GetAsync(chunk.CallId, ct);
        var speaker = chunk.Track == VoiceTrack.Customer ? "Müşteri" : $"Temsilci ({call?.AgentDisplayName ?? "Temsilci"})";
        var track = VoiceCallService.TrackName(chunk.Track);

        string text;
        try
        {
            text = (await transcriber.TranscribeAsync(chunk.Data, chunk.ContentType, ct)).Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var max = options.CurrentValue.TranscriptionMaxAttempts;
            var final = chunk.Attempts + 1 >= max;
            logger.LogWarning(ex, "[VoiceCall] Döküm başarısız chunk={Chunk} deneme={Attempt}/{Max}", chunk.Id, chunk.Attempts + 1, max);
            await recordings.FailAttemptAsync(chunk.Id, now.AddSeconds(10 * Math.Pow(2, chunk.Attempts)), final, ct);
            if (final)
                await bridge.PublishVoiceTranscriptAsync(chunk.SessionId, chunk.CallId, track, chunk.OffsetMs, $"{speaker}: (döküm alınamadı)");
            return true;
        }

        if (text.Length == 0)
        {
            await recordings.CompleteAsync(chunk.Id, null, ct);
            return true;
        }

        await recordings.CompleteAsync(chunk.Id, text, ct);
        await bridge.PublishVoiceTranscriptAsync(chunk.SessionId, chunk.CallId, track, chunk.OffsetMs, $"{speaker}: {text}");
        if (chunk.Track == VoiceTrack.Customer)
            await sessions.AppendUserMessageAsync(chunk.SessionId, text, ct);
        else
            await sessions.AppendAssistantMessageAsync(chunk.SessionId, text, ct);
        return true;
    }
}
```

DI: `services.AddSingleton<Services.Voice.VoiceTranscriptionProcessor>();`

- [ ] **Step 4: OpenAI adaptörü**

```csharp
// src/CustomerSupportBot.Adapters.AI/Audio/OpenAiAudioTranscriber.cs
// IAudioTranscriber — OpenAI ses dökümü (gpt-4o-transcribe / whisper-1). Parça tek başına çözülebilir webm/ogg.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using Microsoft.Extensions.Options;
using OpenAI.Audio;

namespace CustomerSupportBot.Adapters.AI.Audio;

public sealed class OpenAiAudioTranscriber(AudioClient client, IOptionsMonitor<VoiceCallOptions> options) : IAudioTranscriber
{
    public async Task<string> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default)
    {
        var extension = contentType.Contains("ogg", StringComparison.OrdinalIgnoreCase) ? "ogg" : "webm";
        using var stream = new MemoryStream(audio);
        var result = await client.TranscribeAudioAsync(stream, $"chunk.{extension}", new AudioTranscriptionOptions
        {
            Language = options.CurrentValue.TranscriptionLanguage,
            ResponseFormat = AudioTranscriptionFormat.Text
        }, ct);
        return result.Value.Text ?? "";
    }
}
```

Kayıt (`AiServicesExtensions.AddAiServices`, `IImageAnalysisPort` kaydının altına):

```csharp
        // Sesli görüşme kayıt parçalarının dökümü — OpenAI sağlayıcısında Realtime anahtarı yoksa genel anahtar.
        services.AddSingleton<IAudioTranscriber>(sp =>
        {
            var ai = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            var voice = sp.GetRequiredService<IOptionsMonitor<CustomerSupportBot.Application.Ports.Outbound.VoiceCallOptions>>();
            var audio = ai.Provider == AiProvider.AzureOpenAI
                ? new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(ai.AzureOpenAI.Endpoint!), new System.ClientModel.ApiKeyCredential(ai.AzureOpenAI.ApiKey!))
                    .GetAudioClient(voice.CurrentValue.TranscriptionModel)
                : new OpenAI.OpenAIClient(ai.Realtime.ApiKey ?? ai.OpenAI.ApiKey).GetAudioClient(voice.CurrentValue.TranscriptionModel);
            return new CustomerSupportBot.Adapters.AI.Audio.OpenAiAudioTranscriber(audio, voice);
        });
```

(`AiClientFactory.CreateAzureChatClient` Azure istemcisini nasıl kuruyorsa — kimlik bilgisi türü dahil — aynı yolu kullan; Azure'da `TranscriptionModel` dağıtım adıdır.)

- [ ] **Step 5: Worker**

```csharp
// src/CustomerSupportBot.Api/Workers/VoiceCallWorker.cs
// Sesli görüşme arka plan işi: döküm kuyruğunu boşaltır (iş varken beklemeden) ve 5 sn'de bir zaman
// aşımı süpürmesi yapar (çalan → cevapsız, parçası kesilen aktif → başarısız).

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Voice;

namespace CustomerSupportBot.Api.Workers;

public sealed class VoiceCallWorker(
    VoiceTranscriptionProcessor processor,
    IVoiceCallPort calls,
    ILogger<VoiceCallWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SweepEvery = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastSweep = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastSweep >= SweepEvery)
                {
                    var closed = await calls.SweepAsync(stoppingToken);
                    if (closed > 0) logger.LogInformation("[VoiceCall] Zaman aşımıyla kapatılan görüşme: {Count}", closed);
                    lastSweep = DateTime.UtcNow;
                }
                if (!await processor.ProcessNextAsync(stoppingToken))
                    await Task.Delay(Idle, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "[VoiceCall] Arka plan işi hatası");
                await Task.Delay(Idle, stoppingToken);
            }
        }
    }
}
```

`ApplicationServicesExtensions.cs` (`HumanInvolvementTrackingService` kaydının altına): `services.AddHostedService<VoiceCallWorker>();`

- [ ] **Step 6: Run tests**

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-namespace "*Voice"` → PASS (19 test). `dotnet build CustomerSupportBot.slnx` (ya da çözüm dosyası) → 0 hata.

- [ ] **Step 7: Commit**

```bash
git add src tests/CustomerSupportBot.Application.Tests
git commit -m "feat(voice-call): transcription worker with retries, budget and timeout sweep"
```

---

### Task 8: Saklama süresi ve KVKK dışa aktarımı

**Files:**
- Modify: `src/CustomerSupportBot.Application/Services/Privacy/DataPrivacyService.cs`
- Modify: `src/CustomerSupportBot.Application/Ports/Inbound/IDataPrivacyPort.cs`
- Modify: `src/CustomerSupportBot.Api/Workers/DataRetentionService.cs` (başlangıç logu)
- Test: `tests/CustomerSupportBot.Application.Tests/Privacy/DataPrivacyVoiceTests.cs` (klasör mevcut desene göre)

**Interfaces:**
- Consumes: `IVoiceRecordingStore.PurgeAudioCreatedBeforeAsync`, `IVoiceCallStore.ListForSessionAsync`, `IVoiceRecordingStore.ListMetaAsync`.
- Produces: `RetentionResult`'a `int VoiceRecordingsPurged = 0` (son, varsayılanlı parametre); `ExportedSession`'a `IReadOnlyList<ExportedVoiceCall>? VoiceCalls = null`; `public sealed record ExportedVoiceCall(string AgentDisplayName, DateTime CreatedAt, DateTime? EndedAt, int? DurationSeconds, IReadOnlyList<ExportedVoiceLine> Lines); public sealed record ExportedVoiceLine(string Speaker, int OffsetMs, string Text);`

- [ ] **Step 1: Write the failing tests**

Mevcut `DataPrivacyService` testlerinin kurulumunu (`grep -rln "new DataPrivacyService" tests`) örnek al; yeni dosyada:

```csharp
// Ses kaydı saklama süresi: 90 günden eski parçaların sesi silinir, döküm kalır; dışa aktarım dökümü içerir.

[Fact]
public async Task Retention_PurgesVoiceAudioOlderThanConfiguredDays()
{
    var voice = Substitute.For<IVoiceRecordingStore>();
    voice.PurgeAudioCreatedBeforeAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(4);
    var service = CreateService(voiceRecordings: voice, options: new DataRetentionOptions { VoiceRecordingRetentionDays = 90 });

    var result = await service.RunRetentionAsync(TestContext.Current.CancellationToken);

    result.VoiceRecordingsPurged.Should().Be(4);
    await voice.Received(1).PurgeAudioCreatedBeforeAsync(
        Arg.Is<DateTime>(d => d < DateTime.UtcNow.AddDays(-89) && d > DateTime.UtcNow.AddDays(-91)),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task Export_IncludesVoiceTranscriptLines()
{
    // CreateService içinde ISessionManager.GetAllSessionsAsync("1001") → [s1]; voiceCalls.ListForSessionAsync("s1") →
    // [bitmiş görüşme]; voiceRecordings.ListMetaAsync(call.Id) → [Customer @0 "Merhaba", Agent @10000 "Buyurun"].
    var export = await service.ExportAsync("1001", TestContext.Current.CancellationToken);
    var lines = export.Sessions.Single().VoiceCalls!.Single().Lines;
    lines.Select(l => (l.Speaker, l.Text)).Should().Equal(("Müşteri", "Merhaba"), ("Temsilci", "Buyurun"));
}
```

(`CreateService` yardımcısını mevcut test dosyasındaki kurulumdan türet; `RunRetentionAsync`/`ExportAsync` gerçek metot adlarını `IDataPrivacyPort`'tan al.)

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class "*DataPrivacyVoiceTests"` → FAIL.

- [ ] **Step 3: Implement**

`DataPrivacyService` kurucusuna sona `IVoiceRecordingStore? voiceRecordings = null, IVoiceCallStore? voiceCalls = null` ekle (alanlara ata). Saklama taramasında fotoğraf silmenin hemen altına:

```csharp
        var voicePurged = 0;
        if (_voiceRecordings is not null)
        {
            try
            {
                voicePurged = await _voiceRecordings.PurgeAudioCreatedBeforeAsync(
                    DateTime.UtcNow.AddDays(-options.VoiceRecordingRetentionDays), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add("voice_recordings: " + ex.Message);
            }
        }
```

log mesajına `{Voice} ses kaydı` ekle ve `return new RetentionResult(true, sessionsErased, attachmentsDeleted, failures, voicePurged);` (kayıt tanımında son parametre `int VoiceRecordingsPurged = 0`). Dışa aktarımda her oturum için:

```csharp
            IReadOnlyList<ExportedVoiceCall>? voiceCalls = null;
            if (_voiceCalls is not null && _voiceRecordings is not null)
            {
                var list = new List<ExportedVoiceCall>();
                foreach (var call in await _voiceCalls.ListForSessionAsync(info.SessionId, ct))
                {
                    var lines = (await _voiceRecordings.ListMetaAsync(call.Id, ct))
                        .Where(c => !string.IsNullOrWhiteSpace(c.TranscriptText))
                        .Select(c => new ExportedVoiceLine(c.Track == VoiceTrack.Customer ? "Müşteri" : "Temsilci", c.OffsetMs, c.TranscriptText!))
                        .ToList();
                    list.Add(new ExportedVoiceCall(call.AgentDisplayName, call.CreatedAt, call.EndedAt,
                        call.Duration is { } d ? (int)d.TotalSeconds : null, lines));
                }
                voiceCalls = list;
            }
```

ve `new ExportedSession(…, dispositions, voiceCalls)`. Ses dosyaları dışa aktarıma girmez (boyut); spec'teki not.

- [ ] **Step 4: Run tests** — PASS; `dotnet test --project tests/CustomerSupportBot.Application.Tests` tümü PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CustomerSupportBot.Application src/CustomerSupportBot.Api/Workers/DataRetentionService.cs tests/CustomerSupportBot.Application.Tests
git commit -m "feat(voice-call): 90-day audio retention and transcript in data export"
```

---

### Task 9: coturn ve dağıtım belgeleri

**Files:**
- Modify: `deploy/docker-compose.yml`
- Create: `deploy/turnserver.conf`
- Modify: `docs/deployment.md`, `docs/operations.md` (VoiceCall ayarları), `docs/security.md` (kayıt rızası, TURN kimliği, saklama)

- [ ] **Step 1: Compose servisi** (`mailpit` servisinin altına)

```yaml
  # coturn — sesli görüşmede (temsilci ↔ müşteri, WebRTC) NAT/kurumsal ağ arkasındaki taraflar için röle.
  # Kimlik: TURN REST API (use-auth-secret); API kısa ömürlü kullanıcı/parola üretir (VoiceCall:Turn).
  # static-auth-secret, appsettings'teki VoiceCall:Turn:SharedSecret ile aynı olmalı (yerel geliştirme değeri).
  coturn:
    image: coturn/coturn:latest
    container_name: aibot_coturn
    restart: unless-stopped
    command: ["-c", "/etc/coturn/turnserver.conf"]
    volumes:
      - ./turnserver.conf:/etc/coturn/turnserver.conf:ro
    ports:
      - "127.0.0.1:3478:3478/udp"
      - "127.0.0.1:3478:3478/tcp"
      - "127.0.0.1:49160-49200:49160-49200/udp"
```

- [ ] **Step 2: `deploy/turnserver.conf`**

```
# Yerel geliştirme TURN sunucusu (aibot_coturn). Üretimde: gerçek external-ip, TLS (5349) ve güçlü sır.
listening-port=3478
realm=aibot.local
use-auth-secret
static-auth-secret=aibot-dev-turn-secret
min-port=49160
max-port=49200
external-ip=127.0.0.1
fingerprint
no-cli
no-tls
no-dtls
log-file=stdout
```

- [ ] **Step 3: Doğrula**

Run: `docker compose -p aibot -f deploy/docker-compose.yml up -d coturn && docker logs aibot_coturn 2>&1 | tail -5`
Expected: `listener opened on : 127.0.0.1:3478` benzeri satırlar, hata yok.

- [ ] **Step 4: Belgeler**

`docs/deployment.md`: servis haritasına `coturn` satırı, port tablosuna `TURN 3478 udp/tcp` ve `49160-49200/udp`, "Sesli görüşme (TURN)" alt başlığı: neden gerekir, `VoiceCall:Turn:SharedSecret` ↔ `static-auth-secret` eşleşmesi, üretim notları (TLS, external-ip). Komutlar `docker compose -p aibot -f deploy/docker-compose.yml …` biçiminde. `docs/operations.md`: `VoiceCall` ayar tablosu (Task 6 Step 5'teki anahtarlar) ve `DataRetention:VoiceRecordingRetentionDays`. `docs/security.md`: kayıt rızası zorunlu, kaydı temsilci tarayıcısı yapar (müşteri bozamaz), TURN sırrı tarayıcıya gitmez (10 dk kimlik), ses 90 gün, dinleme yetkisi (yönetici + görüşmeyi yapan temsilci).

- [ ] **Step 5: Commit**

```bash
git add deploy docs/deployment.md docs/operations.md docs/security.md
git commit -m "feat(voice-call): coturn service and deployment docs"
```

---

### Task 10: Web — WebRTC/kayıt modülü ve temsilci arayüzü

**Files:**
- Create: `src/CustomerSupportBot.Web/wwwroot/js/agent-voice-call.js`
- Create: `src/CustomerSupportBot.Web/Components/StaffVoiceCallBar.razor` (+ `.razor.css`)
- Modify: `src/CustomerSupportBot.Web/wwwroot/js/admin-chat-bridge.js` (`voice_signal` olayı)
- Modify: `src/CustomerSupportBot.Web/Services/AdminApiService.cs`, `src/CustomerSupportBot.Web/Models/AdminModels.cs`
- Modify: `src/CustomerSupportBot.Web/Pages/Admin.razor`, `Admin.razor.cs` (sohbet başlığına çubuk, döküm satırı çizimi)
- Modify: `src/CustomerSupportBot.Web/Components/Icon.razor` (`phone-off`, `mic`, `mic-off`)
- Test: `tests/CustomerSupportBot.Web.Tests/VoiceCallStateTests.cs`

**Interfaces:**
- Consumes: Task 6 uçları; SSE `voice_signal`.
- Produces:
  - JS `window.csbVoice`:
    - `staffStart({ apiBase, token, callId, dotnetRef })` — çalan görüşmeyi izlemeye başlar (henüz medya yok).
    - `staffOnSignal(json)` — `accepted` gelince mikrofonu açar, `RTCPeerConnection` kurar, offer gönderir; `answer`/`ice` işler; `declined`/`ended` gelince temizler.
    - `customerAnswer({ apiBase, token, callId, dotnetRef })` — mikrofon izni ister (reddedilirse `false` döner), `accept` çağırır, bağlantıyı bekler.
    - `customerOnSignal(json)`, `hangup(reason)`, `setMuted(bool)`, `isActive()`.
    - .NET'e geri çağrılar (`dotnetRef.invokeMethodAsync`): `OnVoiceState(string state)` (`ringing|connecting|connected|ended`), `OnVoiceRecording(bool on)`, `OnVoiceError(string message)`.
  - `VoiceCallState` (Web, `Models/AdminModels.cs`): `public sealed record VoiceCallDto(string Id, string SessionId, string AgentDisplayName, string Status, DateTimeOffset CreatedAt, DateTimeOffset? AnsweredAt, DateTimeOffset? EndedAt, string? EndReason, int? DurationSeconds);`
  - `ChatHistoryMessage`'a `string? VoiceCallId = null, string? VoiceTrack = null, int? OffsetMs = null` (son, varsayılanlı).
  - `AdminApiService`: `StartVoiceCallAsync(string sessionId) : Task<(VoiceCallDto? Call, string? Error)>`, `HangupVoiceCallAsync(string callId) : Task`, `GetMyVoiceCallAsync() : Task<VoiceCallDto?>`.
  - Saf yardımcı `VoiceCallText` (Web `Helpers/VoiceCallText.cs`): `StatusLabel(string status, string? endReason) : string`, `FormatElapsed(TimeSpan) : string` (`"04:12"`), `StartError(string? code) : string`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CustomerSupportBot.Web.Tests/VoiceCallStateTests.cs
using CustomerSupportBot.Web.Helpers;

namespace CustomerSupportBot.Web.Tests;

public class VoiceCallStateTests
{
    [Theory]
    [InlineData("ringing", null, "Aranıyor…")]
    [InlineData("active", null, "Görüşmede")]
    [InlineData("declined", "no_microphone", "Müşterinin mikrofonu yok")]
    [InlineData("declined", "declined", "Müşteri reddetti")]
    [InlineData("missed", "missed", "Cevap verilmedi")]
    [InlineData("failed", "connection_lost", "Bağlantı koptu")]
    [InlineData("ended", "customer_hangup", "Görüşme bitti")]
    public void StatusLabel(string status, string? reason, string expected) =>
        VoiceCallText.StatusLabel(status, reason).Should().Be(expected);

    [Fact]
    public void FormatElapsed_IsMinutesSeconds() =>
        VoiceCallText.FormatElapsed(TimeSpan.FromSeconds(252)).Should().Be("04:12");

    [Theory]
    [InlineData("voice_call_busy", "Zaten bir sesli görüşmedesiniz.")]
    [InlineData("not_in_human_mode", "Sesli görüşme için önce sohbeti devralın.")]
    [InlineData(null, "Sesli görüşme başlatılamadı.")]
    public void StartError(string? code, string expected) =>
        VoiceCallText.StartError(code).Should().Be(expected);
}
```

- [ ] **Step 2: Run** — `dotnet test --project tests/CustomerSupportBot.Web.Tests -- --filter-class "*VoiceCallStateTests"` → FAIL.

- [ ] **Step 3: `Helpers/VoiceCallText.cs`**

```csharp
// Helpers/VoiceCallText.cs — sesli görüşme durum metinleri (temsilci ve müşteri arayüzü ortak).
namespace CustomerSupportBot.Web.Helpers;

public static class VoiceCallText
{
    public static string StatusLabel(string status, string? endReason) => status switch
    {
        "ringing" => "Aranıyor…",
        "active" => "Görüşmede",
        "declined" when endReason == "no_microphone" => "Müşterinin mikrofonu yok",
        "declined" => "Müşteri reddetti",
        "missed" => "Cevap verilmedi",
        "cancelled" => "Arama iptal edildi",
        "failed" => "Bağlantı koptu",
        _ => "Görüşme bitti"
    };

    public static string FormatElapsed(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    public static string StartError(string? code) => code switch
    {
        "voice_call_busy" => "Zaten bir sesli görüşmedesiniz.",
        "not_in_human_mode" => "Sesli görüşme için önce sohbeti devralın.",
        "disabled" => "Sesli görüşme şu anda kapalı.",
        _ => "Sesli görüşme başlatılamadı."
    };
}
```

- [ ] **Step 4: `wwwroot/js/agent-voice-call.js`**

```js
// agent-voice-call.js — temsilci ↔ müşteri sesli görüşmesi (WebRTC). Sinyal: POST …/signal, gelen sinyal SSE
// `voice_signal` olayıyla bu modüle verilir. Kayıt YALNIZCA temsilci tarafında: yerel mikrofon ve uzak müşteri
// sesi ayrı ayrı, 10 sn'lik tam webm parçaları (kaydedici her parçada yeniden başlar) halinde yüklenir.
(function () {
    'use strict';
    var SEGMENT_MS = 10000;
    var CONNECT_TIMEOUT_MS = 20000;
    var DISCONNECT_GRACE_MS = 30000;

    var s = null; // aktif görüşme durumu

    function reset() {
        if (!s) return;
        try { (s.recorders || []).forEach(function (r) { r.stopped = true; if (r.rec && r.rec.state !== 'inactive') r.rec.stop(); }); } catch (e) { }
        try { if (s.pc) s.pc.close(); } catch (e) { }
        try { if (s.local) s.local.getTracks().forEach(function (t) { t.stop(); }); } catch (e) { }
        if (s.audio) { s.audio.srcObject = null; s.audio.remove(); }
        clearTimeout(s.connectTimer); clearTimeout(s.disconnectTimer);
        s = null;
    }

    function notify(method, arg) { if (s && s.ref) s.ref.invokeMethodAsync(method, arg).catch(function () { }); }

    function post(path, body, contentType) {
        var headers = { 'Authorization': 'Bearer ' + s.token };
        if (contentType) headers['Content-Type'] = contentType;
        return fetch(s.apiBase + path, { method: 'POST', headers: headers, body: body });
    }

    function sendSignal(type, data) {
        if (!s) return;
        post(s.signalPath, JSON.stringify({ callId: s.callId, type: type, data: data }), 'application/json').catch(function () { });
    }

    async function iceServers() {
        var r = await fetch(s.apiBase + s.icePath, { headers: { 'Authorization': 'Bearer ' + s.token } });
        if (!r.ok) return [];
        return (await r.json()).iceServers || [];
    }

    async function getMic() {
        return navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } });
    }

    async function createPeer() {
        var pc = new RTCPeerConnection({ iceServers: await iceServers() });
        s.pc = pc;
        s.pendingIce = [];
        s.local.getTracks().forEach(function (t) { pc.addTrack(t, s.local); });
        pc.onicecandidate = function (e) { if (e.candidate) sendSignal('ice', e.candidate.toJSON()); };
        pc.ontrack = function (e) {
            s.remote = e.streams[0];
            if (!s.audio) { s.audio = document.createElement('audio'); s.audio.autoplay = true; document.body.appendChild(s.audio); }
            s.audio.srcObject = s.remote;
            if (s.role === 'staff' && !s.recordingStarted) startRecording();
        };
        pc.onconnectionstatechange = function () {
            if (!s) return;
            var st = pc.connectionState;
            if (st === 'connected') {
                clearTimeout(s.connectTimer); clearTimeout(s.disconnectTimer);
                notify('OnVoiceState', 'connected');
            } else if (st === 'disconnected') {
                clearTimeout(s.disconnectTimer);
                s.disconnectTimer = setTimeout(function () { window.csbVoice.hangup('connection_lost'); }, DISCONNECT_GRACE_MS);
            } else if (st === 'failed') {
                window.csbVoice.hangup('connection_lost');
            }
        };
        s.connectTimer = setTimeout(function () {
            if (s && s.pc && s.pc.connectionState !== 'connected') window.csbVoice.hangup('connect_failed');
        }, CONNECT_TIMEOUT_MS);
        return pc;
    }

    async function addIce(candidate) {
        if (!s || !s.pc) return;
        if (!s.pc.remoteDescription) { s.pendingIce.push(candidate); return; }
        try { await s.pc.addIceCandidate(candidate); } catch (e) { }
    }

    async function flushIce() {
        var list = s.pendingIce || []; s.pendingIce = [];
        for (var i = 0; i < list.length; i++) { try { await s.pc.addIceCandidate(list[i]); } catch (e) { } }
    }

    // ── Kayıt (yalnız temsilci) ───────────────────────────────────────────────
    function startRecording() {
        s.recordingStarted = true;
        s.callStart = performance.now();
        s.recorders = [segmentRecorder(s.local, 'agent'), segmentRecorder(s.remote, 'customer')];
        notify('OnVoiceRecording', true);
    }

    function segmentRecorder(stream, track) {
        var mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus') ? 'audio/webm;codecs=opus' : 'audio/ogg;codecs=opus';
        var state = { seq: 0, stopped: false, rec: null };
        function next() {
            if (state.stopped || !s) return;
            var startedAt = performance.now();
            var rec = new MediaRecorder(stream, { mimeType: mime, audioBitsPerSecond: 32000 });
            state.rec = rec;
            var parts = [];
            rec.ondataavailable = function (e) { if (e.data && e.data.size) parts.push(e.data); };
            rec.onstop = function () {
                var blob = new Blob(parts, { type: mime.split(';')[0] });
                var offset = Math.max(0, Math.round(startedAt - s.callStart));
                var duration = Math.round(performance.now() - startedAt);
                upload(track, state.seq++, offset, duration, blob);
                next();
            };
            rec.start();
            setTimeout(function () { if (rec.state !== 'inactive') rec.stop(); }, SEGMENT_MS);
        }
        next();
        return state;
    }

    async function upload(track, seq, offsetMs, durationMs, blob, attempt) {
        if (!blob.size || !s) return;
        attempt = attempt || 0;
        var path = '/voice-calls/' + encodeURIComponent(s.callId) + '/chunks?track=' + track + '&seq=' + seq
            + '&offsetMs=' + offsetMs + '&durationMs=' + durationMs;
        try {
            var r = await post(path, blob, blob.type);
            if (r.ok || r.status === 409 || r.status === 403) return;
            throw new Error('HTTP ' + r.status);
        } catch (e) {
            if (attempt < 3) setTimeout(function () { upload(track, seq, offsetMs, durationMs, blob, attempt + 1); }, 1000 * (attempt + 1));
            else notify('OnVoiceError', 'Kayıt kesintiye uğradı.');
        }
    }

    window.csbVoice = {
        isActive: function () { return !!s; },

        staffStart: function (o) {
            reset();
            s = { role: 'staff', apiBase: o.apiBase, token: o.token, callId: o.callId, ref: o.dotnetRef,
                  signalPath: '/voice-calls/' + encodeURIComponent(o.callId) + '/signal',
                  hangupPath: '/voice-calls/' + encodeURIComponent(o.callId) + '/hangup',
                  icePath: '/voice-calls/' + encodeURIComponent(o.callId) + '/ice-config' };
            notify('OnVoiceState', 'ringing');
        },

        staffOnSignal: async function (json) {
            if (!s || s.role !== 'staff') return;
            var m = typeof json === 'string' ? JSON.parse(json) : json;
            if (m.callId !== s.callId) return;
            if (m.type === 'accepted') {
                try { s.local = await getMic(); }
                catch (e) { notify('OnVoiceError', 'Mikrofon izni verilmedi.'); window.csbVoice.hangup('agent_hangup'); return; }
                notify('OnVoiceState', 'connecting');
                var pc = await createPeer();
                var offer = await pc.createOffer();
                await pc.setLocalDescription(offer);
                sendSignal('offer', pc.localDescription.toJSON());
            } else if (m.type === 'answer') {
                await s.pc.setRemoteDescription(m.data); await flushIce();
            } else if (m.type === 'ice') {
                await addIce(m.data);
            } else if (m.type === 'declined' || m.type === 'ended') {
                notify('OnVoiceState', 'ended'); reset();
            }
        },

        customerAnswer: async function (o) {
            reset();
            s = { role: 'customer', apiBase: o.apiBase, token: o.token, callId: o.callId, ref: o.dotnetRef,
                  signalPath: '/chat/voice-calls/' + encodeURIComponent(o.callId) + '/signal',
                  hangupPath: '/chat/voice-calls/' + encodeURIComponent(o.callId) + '/hangup',
                  icePath: '/chat/voice-calls/' + encodeURIComponent(o.callId) + '/ice-config' };
            try { s.local = await getMic(); }
            catch (e) {
                await post('/chat/voice-calls/' + encodeURIComponent(o.callId) + '/decline?reason=no_microphone');
                reset(); return false;
            }
            var r = await post('/chat/voice-calls/' + encodeURIComponent(o.callId) + '/accept');
            if (!r.ok) { reset(); return false; }
            notify('OnVoiceState', 'connecting');
            await createPeer();
            return true;
        },

        customerOnSignal: async function (json) {
            if (!s || s.role !== 'customer') return;
            var m = typeof json === 'string' ? JSON.parse(json) : json;
            if (m.callId !== s.callId) return;
            if (m.type === 'offer') {
                await s.pc.setRemoteDescription(m.data); await flushIce();
                var answer = await s.pc.createAnswer();
                await s.pc.setLocalDescription(answer);
                sendSignal('answer', s.pc.localDescription.toJSON());
            } else if (m.type === 'ice') {
                await addIce(m.data);
            } else if (m.type === 'ended') {
                notify('OnVoiceState', 'ended'); reset();
            }
        },

        hangup: async function (reason) {
            if (!s) return;
            var path = s.hangupPath + '?reason=' + encodeURIComponent(reason || '');
            var ref = s.ref;
            // Son kayıt parçaları kapanmadan yüklensin diye kaydediciler önce durdurulur (onstop yükler).
            (s.recorders || []).forEach(function (r) { r.stopped = true; if (r.rec && r.rec.state !== 'inactive') r.rec.stop(); });
            try { await post(path); } catch (e) { }
            if (ref) ref.invokeMethodAsync('OnVoiceState', 'ended').catch(function () { });
            setTimeout(reset, 1500);
        },

        setMuted: function (muted) {
            if (s && s.local) s.local.getAudioTracks().forEach(function (t) { t.enabled = !muted; });
        }
    };
})();
```

Not: kaydedici durdurulunca `onstop` yüklemeyi yapar ve `next()` çağrısı `stopped` olduğu için yeni parça başlatmaz. `reset` 1,5 sn geciktirilir ki son yüklemeler başlasın.

- [ ] **Step 5: admin-chat-bridge.js**

`__adminSubscribeChat` içinde `bridge_message` dinleyicisinin altına:

```js
    es.addEventListener('voice_signal', function (e) {
        var data = e.data || '{}';
        if (window.csbVoice) window.csbVoice.staffOnSignal(data);
        ref.invokeMethodAsync('OnChatEvent', 'voice_signal', data).catch(function () { });
    });
```

- [ ] **Step 6: AdminApiService + modeller**

`AdminModels.cs`: `VoiceCallDto` kaydı (Interfaces bloğundaki imza) ve `ChatHistoryMessage`'a üç varsayılanlı alan. `AdminApiService.cs` (rol önekini `PrefixAsync()` ile alan mevcut desen — `/agent` önekli uçlar yok; sesli görüşme uçları öneksizdir, iki rol için aynı yol):

```csharp
    public async Task<(VoiceCallDto? Call, string? Error)> StartVoiceCallAsync(string sessionId)
    {
        using var response = await http.PostAsync($"/chat-sessions/{Uri.EscapeDataString(sessionId)}/voice-calls", null);
        if (response.IsSuccessStatusCode) return (await response.Content.ReadFromJsonAsync<VoiceCallDto>(), null);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return (null, body.TryGetProperty("error", out var e) ? e.GetString() : null);
    }

    public async Task HangupVoiceCallAsync(string callId) =>
        (await http.PostAsync($"/voice-calls/{Uri.EscapeDataString(callId)}/hangup?reason=agent_hangup", null)).Dispose();

    public async Task<VoiceCallDto?> GetMyVoiceCallAsync()
    {
        using var response = await http.GetAsync("/voice-calls/mine");
        return response.StatusCode == System.Net.HttpStatusCode.OK ? await response.Content.ReadFromJsonAsync<VoiceCallDto>() : null;
    }
```

- [ ] **Step 7: Icon + StaffVoiceCallBar**

`Icon.razor` `switch`'ine:

```razor
        case "phone-off":
            <path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7 2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-3.33-2.67m-2.67-3.34a19.79 19.79 0 0 1-3.07-8.63A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91" /><path d="m22 2-20 20" />
            break;
        case "mic":
            <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" /><path d="M19 10v2a7 7 0 0 1-14 0v-2" /><path d="M12 19v3" />
            break;
        case "mic-off":
            <path d="m2 2 20 20" /><path d="M18.89 13.23A7 7 0 0 0 19 12v-2" /><path d="M5 10v2a7 7 0 0 0 12 5" /><path d="M15 9.34V5a3 3 0 0 0-5.68-1.33" /><path d="M9 9v3a3 3 0 0 0 5.12 2.12" /><path d="M12 19v3" />
            break;
```

```razor
@* Components/StaffVoiceCallBar.razor — açık sohbetin başlığında sesli görüşme denetimi (temsilci). *@
@inject AdminApiService AdminApi
@inject AuthTokenStore TokenStore
@inject HttpClient Http
@inject IJSRuntime JS
@inject ToastService Toast
@implements IAsyncDisposable

@if (_call is null)
{
    <button type="button" class="btn-secondary" @onclick="StartAsync" disabled="@(_busyElsewhere || _starting)"
            title="@(_busyElsewhere ? "Zaten bir sesli görüşmedesiniz." : "Müşteriyi sesli ara")">
        <Icon Name="phone" /> Sesli görüşme
    </button>
}
else
{
    <div class="vcall-bar" role="status" aria-live="polite">
        <span class="vcall-dot @_state"></span>
        <span class="vcall-label">@Label</span>
        @if (_state == "connected")
        {
            <span class="vcall-time">@VoiceCallText.FormatElapsed(DateTime.UtcNow - _connectedAt)</span>
        }
        @if (_recording)
        {
            <span class="vcall-rec" title="Görüşme kaydediliyor"><Icon Name="mic" Size="13" /> Kayıt</span>
        }
        @if (_state is "connecting" or "connected")
        {
            <button type="button" class="btn-icon-bordered" @onclick="ToggleMuteAsync" aria-pressed="@(_muted ? "true" : "false")"
                    aria-label="@(_muted ? "Sesi aç" : "Sessize al")" title="@(_muted ? "Sesi aç" : "Sessize al")">
                <Icon Name="@(_muted ? "mic-off" : "mic")" />
            </button>
        }
        <button type="button" class="btn-danger" @onclick="HangupAsync"><Icon Name="phone-off" /> @(_state == "ringing" ? "İptal" : "Bitir")</button>
    </div>
}

@code {
    [Parameter, EditorRequired] public string SessionId { get; set; } = "";

    private VoiceCallDto? _call;
    private string _state = "idle";
    private bool _recording, _muted, _starting, _busyElsewhere;
    private DateTime _connectedAt;
    private Timer? _tick;
    private DotNetObjectReference<StaffVoiceCallBar>? _ref;

    private string Label => _state switch
    {
        "ringing" => "Aranıyor…",
        "connecting" => "Bağlanıyor…",
        "connected" => "Görüşmede",
        _ => ""
    };

    protected override async Task OnInitializedAsync()
    {
        await JS.InvokeVoidAsync("loadScript", "/js/agent-voice-call.js", "js-agent-voice-call");
        var mine = await AdminApi.GetMyVoiceCallAsync();
        _busyElsewhere = mine is not null && mine.SessionId != SessionId;
    }

    private async Task StartAsync()
    {
        _starting = true;
        try
        {
            var (call, error) = await AdminApi.StartVoiceCallAsync(SessionId);
            if (call is null) { Toast.ShowError(VoiceCallText.StartError(error)); _busyElsewhere = error == "voice_call_busy"; return; }
            _call = call;
            _ref ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("csbVoice.staffStart", new
            {
                apiBase = Http.BaseAddress?.ToString().TrimEnd('/'),
                token = await TokenStore.GetAccessTokenAsync(AuthScope.Staff),
                callId = call.Id,
                dotnetRef = _ref
            });
        }
        finally { _starting = false; }
    }

    /// <summary>Admin.razor.cs SSE'den çağırır: arayüz durumunu sinyale göre günceller (WebRTC'yi JS yürütür).</summary>
    public void OnSignal(string type, string? reason)
    {
        if (_call is null) return;
        if (type == "declined") { Toast.ShowInfo(VoiceCallText.StatusLabel("declined", reason)); Clear(); }
        else if (type == "ended" && reason == "missed") { Toast.ShowInfo(VoiceCallText.StatusLabel("missed", reason)); Clear(); }
        StateHasChanged();
    }

    [JSInvokable] public Task OnVoiceState(string state)
    {
        _state = state;
        if (state == "connected")
        {
            _connectedAt = DateTime.UtcNow;
            _tick ??= new Timer(_ => InvokeAsync(StateHasChanged), null, 1000, 1000);
        }
        if (state == "ended") Clear();
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable] public Task OnVoiceRecording(bool on) { _recording = on; return InvokeAsync(StateHasChanged); }
    [JSInvokable] public Task OnVoiceError(string message) { Toast.ShowError(message); return Task.CompletedTask; }

    private async Task ToggleMuteAsync() { _muted = !_muted; await JS.InvokeVoidAsync("csbVoice.setMuted", _muted); }

    private async Task HangupAsync() => await JS.InvokeVoidAsync("csbVoice.hangup", "agent_hangup");

    private void Clear()
    {
        _call = null; _state = "idle"; _recording = false; _muted = false;
        _tick?.Dispose(); _tick = null;
    }

    public async ValueTask DisposeAsync()
    {
        _tick?.Dispose();
        try { if (await JS.InvokeAsync<bool>("csbVoice.isActive")) await JS.InvokeVoidAsync("csbVoice.hangup", "agent_hangup"); } catch { }
        _ref?.Dispose();
    }
}
```

`Icon.razor`'da `phone` adı mevcut. `StaffVoiceCallBar.razor.css`:

```css
.vcall-bar { display: inline-flex; align-items: center; gap: 8px; padding: 4px 6px 4px 10px; border: 1px solid var(--color-border); border-radius: var(--radius-sm); background: var(--color-surface); }
.vcall-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--color-warn); }
.vcall-dot.connected { background: var(--color-success); box-shadow: 0 0 0 3px var(--color-success-bg); }
.vcall-label { font-size: var(--font-size-sm); font-weight: 600; color: var(--color-text); }
.vcall-time { font-family: var(--font-mono); font-size: var(--font-size-sm); color: var(--color-text-secondary); }
.vcall-rec { display: inline-flex; align-items: center; gap: 4px; padding: 1px 7px; border-radius: 10px; font-size: 11px; font-weight: 600; background: var(--color-danger-bg); color: var(--color-danger); }
```

- [ ] **Step 8: Admin.razor bağlantısı**

`Admin.razor` sohbet paneli başlığında (`<button class="btn-secondary @(_assistOpen …` satırından önce):

```razor
                                <StaffVoiceCallBar @ref="_voiceBar" SessionId="@_openChatSession.SessionId" @key="_openChatSession.SessionId" />
```

`Admin.razor.cs`: `private StaffVoiceCallBar? _voiceBar;` ve `OnChatEvent` başına:

```csharp
        if (type == "voice_signal")
        {
            try
            {
                using var vdoc = System.Text.Json.JsonDocument.Parse(data);
                var vt = vdoc.RootElement.TryGetProperty("type", out var tt) ? tt.GetString() ?? "" : "";
                var vr = vdoc.RootElement.TryGetProperty("reason", out var rr) ? rr.GetString() : null;
                await InvokeAsync(() => _voiceBar?.OnSignal(vt, vr));
            }
            catch { }
            return;
        }
```

`bridge_message` ayrıştırmasında meta alanlarını oku ve `new ChatHistoryMessage(sender, text, stamp, voiceCallId, voiceTrack, offsetMs)` ver:

```csharp
            var voiceCallId = root.TryGetProperty("voiceCallId", out var vc) && vc.ValueKind == System.Text.Json.JsonValueKind.String ? vc.GetString() : null;
            var voiceTrack  = root.TryGetProperty("voiceTrack", out var vtr) && vtr.ValueKind == System.Text.Json.JsonValueKind.String ? vtr.GetString() : null;
            int? offsetMs   = root.TryGetProperty("offsetMs", out var vo) && vo.ValueKind == System.Text.Json.JsonValueKind.Number ? vo.GetInt32() : null;
```

Sohbet akışında (`<div class="chat-bubble @(m.Sender…` dalından önce) döküm satırı:

```razor
                                    @if (m.VoiceCallId is not null)
                                    {
                                        <div class="voice-line @(m.VoiceTrack == "agent" ? "agent" : "customer")">
                                            <Icon Name="mic" Size="13" />
                                            <span class="voice-line-text">@m.Text</span>
                                            <button type="button" class="voice-line-play" @onclick="() => OpenPlayer(m.VoiceCallId!, m.OffsetMs ?? 0)"
                                                    aria-label="Kayıtta bu ana git">@FmtOffset(m.OffsetMs ?? 0)</button>
                                        </div>
                                    }
                                    else if (…mevcut dallar…)
```

(`OpenPlayer` ve `FmtOffset` Task 12'de tanımlanır; bu görevde `OpenPlayer` gövdesi boş bırakılmaz — Task 12'ye kadar yalnızca `private void OpenPlayer(string callId, int offsetMs) => _playerCall = (callId, offsetMs);` ve `private static string FmtOffset(int ms) => VoiceCallText.FormatElapsed(TimeSpan.FromMilliseconds(ms));` ekle; `_playerCall` alanı `(string CallId, int OffsetMs)?`.)

admin.css'e:

```css
.voice-line { display: flex; align-items: flex-start; gap: 8px; margin: 4px 0; padding: 8px 10px; border-radius: var(--radius-sm); background: var(--color-surface-alt); font-size: var(--font-size-sm); color: var(--color-text); }
.voice-line.agent { background: var(--color-primary-subtle); }
.voice-line-text { flex: 1; }
.voice-line-play { border: 0; background: transparent; color: var(--color-primary); font-family: var(--font-mono); font-size: 12px; cursor: pointer; }
```

- [ ] **Step 9: Run** — `dotnet test --project tests/CustomerSupportBot.Web.Tests` → PASS; `dotnet build src/CustomerSupportBot.Web` → 0 hata/uyarı.

- [ ] **Step 10: Commit**

```bash
git add src/CustomerSupportBot.Web tests/CustomerSupportBot.Web.Tests
git commit -m "feat(voice-call): WebRTC module, recording upload and staff call bar"
```

---

### Task 11: Web — müşteri tarafı gelen arama ve görüşme kartı

**Files:**
- Create: `src/CustomerSupportBot.Web/Components/CustomerVoiceCallCard.razor` (+ `.razor.css`)
- Modify: `src/CustomerSupportBot.Web/wwwroot/js/chat-bridge.js` (`voice_signal` olay listesine)
- Modify: `src/CustomerSupportBot.Web/Pages/Chat.razor` (olay yönlendirme + kart)
- Modify: `src/CustomerSupportBot.Web/Services/ChatApiService.cs` (`DeclineVoiceCallAsync`)

**Interfaces:**
- Consumes: `csbVoice.customerAnswer/customerOnSignal/hangup/setMuted` (Task 10), müşteri uçları (Task 6).
- Produces: `CustomerVoiceCallCard.OnSignal(string json)` — `Chat.razor` SSE'den çağırır.

- [ ] **Step 1: chat-bridge.js** — olay dizisine `'voice_signal'` ekle:

```js
        ['human_joined', 'human_left', 'bot_typing', 'human_message', 'handoff_pending', 'handoff_cleared', 'approval_resolved', 'voice_signal'].forEach(function (t) {
```

ve hemen altına (WebRTC sinyalleri .NET'e gitmeden işlensin):

```js
        es.addEventListener('voice_signal', function (e) { if (window.csbVoice) window.csbVoice.customerOnSignal(e.data || '{}'); });
```

- [ ] **Step 2: ChatApiService** — `public async Task DeclineVoiceCallAsync(string callId) => (await http.PostAsync($"/chat/voice-calls/{Uri.EscapeDataString(callId)}/decline?reason=declined", null)).Dispose();` (müşteri `HttpClient`'ı — dosyadaki mevcut alan adını kullan).

- [ ] **Step 3: CustomerVoiceCallCard.razor**

```razor
@* Components/CustomerVoiceCallCard.razor — müşteriye gelen temsilci araması: kayıt rızası + kabul/ret, sonra görüşme. *@
@inject ChatApiService ChatApi
@inject AuthTokenStore TokenStore
@inject HttpClient Http
@inject IJSRuntime JS
@implements IAsyncDisposable

@if (_callId is not null)
{
    <div class="cvc-card" role="dialog" aria-modal="false" aria-labelledby="cvc-title">
        @if (_state == "ringing")
        {
            <div class="cvc-head">
                <span class="cvc-avatar"><Icon Name="phone" Size="18" /></span>
                <div>
                    <strong id="cvc-title">@_agentName sizi arıyor</strong>
                    <p class="cvc-consent">Bu görüşme kalite ve kayıt amacıyla kaydedilecek ve yazıya dökülecek. Kabul ederek buna onay vermiş olursunuz.</p>
                </div>
            </div>
            <div class="cvc-actions">
                <button type="button" class="cvc-decline" @onclick="DeclineAsync"><Icon Name="phone-off" /> Reddet</button>
                <button type="button" class="cvc-accept" @onclick="AcceptAsync" disabled="@_answering"><Icon Name="phone" /> Kabul et</button>
            </div>
        }
        else
        {
            <div class="cvc-head">
                <span class="cvc-avatar live"><Icon Name="phone" Size="18" /></span>
                <div>
                    <strong id="cvc-title">@_agentName</strong>
                    <p class="cvc-sub">@(_state == "connected" ? VoiceCallText.FormatElapsed(DateTime.UtcNow - _connectedAt) : "Bağlanıyor…") · Kaydediliyor</p>
                </div>
            </div>
            <div class="cvc-actions">
                <button type="button" class="cvc-mute" @onclick="ToggleMuteAsync" aria-pressed="@(_muted ? "true" : "false")">
                    <Icon Name="@(_muted ? "mic-off" : "mic")" /> @(_muted ? "Sesi aç" : "Sessize al")
                </button>
                <button type="button" class="cvc-decline" @onclick="HangupAsync"><Icon Name="phone-off" /> Bitir</button>
            </div>
        }
        @if (_error is not null) { <p class="cvc-error" role="alert">@_error</p> }
    </div>
}

@code {
    /// <summary>Gelen arama AI sesli modunu kapatmalı — Chat.razor bunu dinler.</summary>
    [Parameter] public EventCallback OnIncomingCall { get; set; }

    private string? _callId, _agentName, _error;
    private string _state = "ringing";
    private bool _answering, _muted;
    private DateTime _connectedAt;
    private Timer? _tick;
    private DotNetObjectReference<CustomerVoiceCallCard>? _ref;

    protected override async Task OnInitializedAsync() =>
        await JS.InvokeVoidAsync("loadScript", "/js/agent-voice-call.js", "js-agent-voice-call");

    public async Task OnSignal(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        var callId = root.TryGetProperty("callId", out var c) ? c.GetString() : null;
        if (type == "ring" && _callId is null)
        {
            _callId = callId; _state = "ringing"; _error = null;
            _agentName = root.TryGetProperty("agentName", out var a) ? a.GetString() : "Temsilci";
            await OnIncomingCall.InvokeAsync();
        }
        else if (type == "ended" && callId == _callId)
        {
            Clear();
        }
        StateHasChanged();
    }

    private async Task AcceptAsync()
    {
        _answering = true;
        _ref ??= DotNetObjectReference.Create(this);
        var ok = await JS.InvokeAsync<bool>("csbVoice.customerAnswer", new
        {
            apiBase = Http.BaseAddress?.ToString().TrimEnd('/'),
            token = await TokenStore.GetAccessTokenAsync(AuthScope.Customer),
            callId = _callId,
            dotnetRef = _ref
        });
        _answering = false;
        if (!ok) { _error = "Mikrofona erişilemedi; görüşme yazılı devam edecek."; _callId = null; }
    }

    private async Task DeclineAsync()
    {
        if (_callId is null) return;
        if (_state == "ringing") await ChatApi.DeclineVoiceCallAsync(_callId);
        else await JS.InvokeVoidAsync("csbVoice.hangup", "customer_hangup");
        Clear();
    }

    private async Task HangupAsync() { await JS.InvokeVoidAsync("csbVoice.hangup", "customer_hangup"); Clear(); }

    private async Task ToggleMuteAsync() { _muted = !_muted; await JS.InvokeVoidAsync("csbVoice.setMuted", _muted); }

    [JSInvokable] public Task OnVoiceState(string state)
    {
        _state = state;
        if (state == "connected") { _connectedAt = DateTime.UtcNow; _tick ??= new Timer(_ => InvokeAsync(StateHasChanged), null, 1000, 1000); }
        if (state == "ended") Clear();
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable] public Task OnVoiceRecording(bool on) => Task.CompletedTask;
    [JSInvokable] public Task OnVoiceError(string message) { _error = message; return InvokeAsync(StateHasChanged); }

    private void Clear() { _callId = null; _state = "ringing"; _muted = false; _tick?.Dispose(); _tick = null; }

    public async ValueTask DisposeAsync()
    {
        _tick?.Dispose();
        try { if (await JS.InvokeAsync<bool>("csbVoice.isActive")) await JS.InvokeVoidAsync("csbVoice.hangup", "customer_hangup"); } catch { }
        _ref?.Dispose();
    }
}
```

`CustomerVoiceCallCard.razor.css` (müşteri teması `styles.css` değişkenleriyle):

```css
.cvc-card { position: fixed; right: 20px; bottom: 96px; z-index: 1200; width: min(360px, calc(100vw - 32px)); padding: 16px; border-radius: 16px; background: var(--color-surface); border: 1px solid var(--color-border); box-shadow: var(--shadow-xl); display: flex; flex-direction: column; gap: 14px; }
.cvc-head { display: flex; gap: 12px; align-items: flex-start; }
.cvc-avatar { flex: none; width: 40px; height: 40px; border-radius: 50%; display: inline-flex; align-items: center; justify-content: center; background: var(--color-primary-subtle); color: var(--color-primary-hover); }
.cvc-avatar.live { background: var(--color-success-bg); color: var(--color-success-text); }
.cvc-consent, .cvc-sub { margin: 4px 0 0; font-size: 13px; color: var(--color-text-secondary); }
.cvc-actions { display: flex; gap: 8px; }
.cvc-actions button { flex: 1; min-height: 44px; border-radius: 10px; font: inherit; font-weight: 600; display: inline-flex; align-items: center; justify-content: center; gap: 6px; cursor: pointer; }
.cvc-accept { border: 0; background: var(--color-success); color: #fff; }
.cvc-decline { border: 1px solid var(--color-danger-border); background: var(--color-surface); color: var(--color-danger); }
.cvc-mute { border: 1px solid var(--color-border); background: var(--color-surface); color: var(--color-text); }
.cvc-error { margin: 0; font-size: 13px; color: var(--color-danger); }
```

- [ ] **Step 4: Chat.razor** — sayfa gövdesine `<CustomerVoiceCallCard @ref="_voiceCard" OnIncomingCall="OnIncomingVoiceCallAsync" />`; `OnPersistentEvent(string type, string data)` metodunun başına:

```csharp
        if (type == "voice_signal")
        {
            if (_voiceCard is not null) await InvokeAsync(() => _voiceCard.OnSignal(data));
            return;
        }
```

`OnIncomingVoiceCallAsync`: AI sesli mod açıksa kapat — `Chat.razor`'da sesli modu kapatan mevcut metodu (`human_joined` işleyicisinin çağırdığı) aynen çağır. Alan: `private CustomerVoiceCallCard? _voiceCard;`.

- [ ] **Step 5: Run** — `dotnet build src/CustomerSupportBot.Web` → 0 hata; `dotnet test --project tests/CustomerSupportBot.Web.Tests` → PASS.

- [ ] **Step 6: Commit**

```bash
git add src/CustomerSupportBot.Web
git commit -m "feat(voice-call): customer incoming call card with recording consent"
```

---

### Task 12: Dinleme, varlık durumu ve uçtan uca doğrulama

**Files:**
- Create: `src/CustomerSupportBot.Web/Components/VoiceCallPlayer.razor` (+ `.razor.css`)
- Modify: `src/CustomerSupportBot.Web/wwwroot/js/agent-voice-call.js` (`csbVoicePlayer`)
- Modify: `src/CustomerSupportBot.Web/Services/AdminApiService.cs` (`GetVoiceCallAsync`)
- Modify: `src/CustomerSupportBot.Web/Pages/Admin.razor(.cs)` (oynatıcı modalı)
- Modify: `src/CustomerSupportBot.Application/Services/Escalation/AgentPresenceService.cs` + `IAgentPresencePort.cs` (`InVoiceCall`)
- Modify: `src/CustomerSupportBot.Web/Pages/Admin.razor` (temsilci şeridinde "Görüşmede")
- Create: `tests/e2e/voice-call.spec.md` (manuel/Playwright doğrulama betiği)
- Modify: belgeler: `docs/CustomerSupportBot.Web/Pages/Admin.md`, `Pages/Chat.md`, `docs/CustomerSupportBot.Api/README.md`, `docs/CustomerSupportBot.Application/README.md`, yeni bileşen belgeleri.

**Interfaces:**
- Consumes: `GET /voice-calls/{id}` (`{ call, lines[] }`), `GET /voice-calls/{id}/chunks/{chunkId}`.
- Produces: `AdminApiService.GetVoiceCallAsync(string callId) : Task<VoiceCallDetail?>`; `public sealed record VoiceCallLine(string ChunkId, string Track, int OffsetMs, string? Text, string Status); public sealed record VoiceCallDetail(VoiceCallDto Call, List<VoiceCallLine> Lines);`; `AgentPresenceView`'a `bool InVoiceCall`.

- [ ] **Step 1: Varlık testi (Application)** — mevcut `AgentPresenceService` testlerine:

```csharp
[Fact]
public async Task Presence_MarksAgentInVoiceCall()
{
    // IVoiceCallStore.ListOpenAsync → [VoiceCall { AgentId = "agent-1", Status = Active }]
    var view = (await service.ListAsync(TestContext.Current.CancellationToken)).Single(a => a.AgentId == "agent-1");
    view.InVoiceCall.Should().BeTrue();
}
```

Uygulama: `AgentPresenceService` kurucusuna `IVoiceCallStore? voiceCalls = null`; listelemede `var inCall = voiceCalls is null ? [] : (await voiceCalls.ListOpenAsync(ct)).Select(c => c.AgentId).ToHashSet();` ve görünüm kaydına `InVoiceCall: inCall.Contains(a.Id)` (kayıt tanımına son, varsayılanlı `bool InVoiceCall = false`). Admin.razor temsilci şeridinde: `@if (a.InVoiceCall) { <span class="presence-call"><Icon Name="phone" Size="12" /> Görüşmede</span> }`. Web modelindeki presence kaydına da `bool InVoiceCall = false`.

Run: `dotnet test --project tests/CustomerSupportBot.Application.Tests -- --filter-class "*AgentPresence*"` → önce FAIL, uygulamadan sonra PASS.

- [ ] **Step 2: Oynatıcı JS** (`agent-voice-call.js` sonuna)

```js
// İki izli kayıt oynatıcı: her iz kendi parça dizisini sırayla çalar, iki iz aynı anda başlar.
window.csbVoicePlayer = (function () {
    var p = null;
    function stop() {
        if (!p) return;
        p.players.forEach(function (x) { x.audio.pause(); x.urls.forEach(URL.revokeObjectURL); x.audio.remove(); });
        p = null;
    }
    async function blobUrl(apiBase, token, callId, chunkId) {
        var r = await fetch(apiBase + '/voice-calls/' + encodeURIComponent(callId) + '/chunks/' + encodeURIComponent(chunkId),
            { headers: { 'Authorization': 'Bearer ' + token } });
        if (!r.ok) return null;
        return URL.createObjectURL(await r.blob());
    }
    async function playTrack(o, chunks, startMs) {
        var audio = document.createElement('audio'); document.body.appendChild(audio);
        var player = { audio: audio, urls: [] };
        var i = chunks.findIndex(function (c) { return c.offsetMs + 10000 > startMs; });
        if (i < 0) return player;
        async function playAt(idx, seekMs) {
            if (!p || idx >= chunks.length) return;
            var url = await blobUrl(o.apiBase, o.token, o.callId, chunks[idx].chunkId);
            if (!url) return playAt(idx + 1, 0);
            player.urls.push(url);
            audio.src = url;
            audio.onloadedmetadata = function () { audio.currentTime = Math.max(0, seekMs / 1000); audio.play().catch(function () { }); };
            audio.onended = function () { playAt(idx + 1, 0); };
        }
        playAt(i, Math.max(0, startMs - chunks[i].offsetMs));
        return player;
    }
    return {
        play: async function (o) {
            stop();
            p = { players: [] };
            var agent = o.lines.filter(function (l) { return l.track === 'agent'; });
            var customer = o.lines.filter(function (l) { return l.track === 'customer'; });
            p.players = await Promise.all([playTrack(o, agent, o.startMs), playTrack(o, customer, o.startMs)]);
        },
        stop: stop
    };
})();
```

- [ ] **Step 3: VoiceCallPlayer.razor**

```razor
@* Components/VoiceCallPlayer.razor — sesli görüşme kaydı: döküm satırları ve iki izli dinleme. *@
@inject AdminApiService AdminApi
@inject AuthTokenStore TokenStore
@inject HttpClient Http
@inject IJSRuntime JS
@implements IAsyncDisposable

<div class="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="vcp-title" @onclick="CloseAsync">
    <div class="modal vcp" @onclick:stopPropagation>
        <header class="modal-header">
            <div>
                <h3 id="vcp-title">Sesli görüşme kaydı</h3>
                @if (_detail is not null)
                {
                    <p class="muted small">@_detail.Call.AgentDisplayName · @(_detail.Call.DurationSeconds is int d ? VoiceCallText.FormatElapsed(TimeSpan.FromSeconds(d)) : "—")</p>
                }
            </div>
            <button type="button" class="btn-icon" @onclick="CloseAsync" aria-label="Kapat"><Icon Name="x" Size="18" /></button>
        </header>
        <div class="modal-body">
            @if (_detail is null) { <p class="muted">Yükleniyor…</p> }
            else
            {
                <div class="vcp-controls">
                    <button type="button" class="btn-primary" @onclick="() => PlayAsync(0)"><Icon Name="play" Size="13" /> Baştan dinle</button>
                    <button type="button" class="btn-secondary" @onclick="StopAsync">Durdur</button>
                </div>
                <ol class="vcp-lines">
                    @foreach (var l in _detail.Lines.Where(l => l.Text is not null || l.Status == "failed"))
                    {
                        var line = l;
                        <li class="@(line.Track == "agent" ? "agent" : "customer")">
                            <button type="button" class="voice-line-play" @onclick="() => PlayAsync(line.OffsetMs)">@VoiceCallText.FormatElapsed(TimeSpan.FromMilliseconds(line.OffsetMs))</button>
                            <span class="vcp-speaker">@(line.Track == "agent" ? "Temsilci" : "Müşteri")</span>
                            <span>@(line.Text ?? "(döküm alınamadı)")</span>
                        </li>
                    }
                </ol>
            }
        </div>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public string CallId { get; set; } = "";
    [Parameter] public int StartOffsetMs { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private VoiceCallDetail? _detail;

    protected override async Task OnInitializedAsync()
    {
        await JS.InvokeVoidAsync("loadScript", "/js/agent-voice-call.js", "js-agent-voice-call");
        _detail = await AdminApi.GetVoiceCallAsync(CallId);
        if (_detail is not null && StartOffsetMs > 0) await PlayAsync(StartOffsetMs);
    }

    private async Task PlayAsync(int startMs)
    {
        if (_detail is null) return;
        await JS.InvokeVoidAsync("csbVoicePlayer.play", new
        {
            apiBase = Http.BaseAddress?.ToString().TrimEnd('/'),
            token = await TokenStore.GetAccessTokenAsync(AuthScope.Staff),
            callId = CallId,
            startMs,
            lines = _detail.Lines.Select(l => new { chunkId = l.ChunkId, track = l.Track, offsetMs = l.OffsetMs })
        });
    }

    private async Task StopAsync() => await JS.InvokeVoidAsync("csbVoicePlayer.stop");
    private async Task CloseAsync() { await StopAsync(); await OnClose.InvokeAsync(); }
    public async ValueTask DisposeAsync() { try { await StopAsync(); } catch { } }
}
```

`.razor.css`: `.vcp { width: min(640px, 92vw); } .vcp-controls { display:flex; gap:8px; margin-bottom:12px; } .vcp-lines { list-style:none; margin:0; padding:0; display:flex; flex-direction:column; gap:6px; max-height:60vh; overflow:auto; } .vcp-lines li { display:flex; gap:8px; align-items:flex-start; padding:8px 10px; border-radius: var(--radius-sm); background: var(--color-surface-alt); } .vcp-lines li.agent { background: var(--color-primary-subtle); } .vcp-speaker { font-weight:600; min-width:72px; }`

`AdminApiService.GetVoiceCallAsync`: `GET /voice-calls/{id}` → `VoiceCallDetail` (JSON `{ call, lines }`, `ReadFromJsonAsync<VoiceCallDetail>`; 403/404 → `null`).

Admin.razor sonuna (modal'lar bölümü): `@if (_playerCall is { } pc) { <VoiceCallPlayer CallId="@pc.CallId" StartOffsetMs="@pc.OffsetMs" OnClose="() => _playerCall = null" /> }`.

- [ ] **Step 4: Uçtan uca doğrulama** — `tests/e2e/voice-call.spec.md` içine aşağıdaki adımları yaz ve çalıştır (Playwright MCP ya da yerel Playwright; iki tarayıcı bağlamı, `--use-fake-ui-for-media-stream --use-fake-device-for-media-stream`):
  1. `docker compose -p aibot -f deploy/docker-compose.yml up -d postgres redis coturn`; API ve Web'i başlat.
  2. Müşteri bağlamı: giriş yap, sohbette temsilci iste. Temsilci bağlamı: eskalasyonu "Devral ve sohbet et".
  3. Temsilci: "Sesli görüşme" → müşteride "… sizi arıyor" kartı ve rıza metni görünür.
  4. Müşteri "Kabul et" → 20 sn içinde iki tarafta da "Görüşmede"; temsilcide "Kayıt" rozeti.
  5. 25 sn bekle → `SELECT track, sequence FROM voice.recording_chunks WHERE call_id = …` en az 2'şer parça (agent, customer).
  6. `IAudioTranscriber` için `appsettings.Development.json`'da gerçek anahtar yoksa döküm satırı "(döküm alınamadı)" olarak 3 denemeden sonra panelde görünür (beklenen; gerçek anahtarla metin görünür).
  7. Temsilci başka bir sohbette "Sesli görüşme" düğmesinin pasif ve açıklamalı olduğunu görür.
  8. Müşteri "Bitir" → iki tarafta kart kapanır; geçmişte "Sesli görüşme · 0 dk 2x sn" notu.
  9. Döküm satırındaki zamana tıklayınca oynatıcı açılır ve iki iz çalar.
  10. Müşteri yeni aramada "Reddet" → temsilcide "Müşteri reddetti" bildirimi, görüşme kurulmaz.

- [ ] **Step 5: Belgeler** — yeni dosyalar için `docs/CustomerSupportBot.*` altında mevcut biçimde birer belge (Domain `Model/Voice`, Application `Services/Voice/VoiceCallService.md` ve `VoiceTranscriptionProcessor.md`, Persistence `PostgresVoiceCallStore.md`/`PostgresVoiceRecordingStore.md`, Api `Endpoints/VoiceCallEndpoints.md` + `Workers/VoiceCallWorker.md`, AI `Audio/OpenAiAudioTranscriber.md`, Web `Components/StaffVoiceCallBar.md`, `CustomerVoiceCallCard.md`, `VoiceCallPlayer.md`); ilgili README dizinlerine birer satır.

- [ ] **Step 6: Tüm testler**

Run: `dotnet test` (çözüm) → tümü PASS (Persistence testleri Docker ister).

- [ ] **Step 7: Commit**

```bash
git add src tests docs
git commit -m "feat(voice-call): recording player, in-call presence, e2e checklist and docs"
```
