# Sesli Görüşme Ekranı Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sesli konuşmayı tam sayfa, koyu, sesle canlanan bir küreli "görüşme ekranına" taşımak.

**Architecture:** Ekranın durumu saf bir C# sınıfında (`VoiceCallModel`) tutulur ve birim testlenir; JS
(`realtime-ui.js`) olayları tek bir `[JSInvokable] VoiceCallEvent(type, a, b)` ile bu modele iletir.
Blazor bileşeni (`VoiceCallOverlay.razor`) modeli çizer. Kürenin kare başı animasyonu Blazor'u
yeniden çizmeden `voice-orb.js` ile bir CSS değişkenine (`--level`) yazılır; seviye
`realtime-client.js`'teki `AnalyserNode`'lardan okunur. Kontroller (sessize al, bitir, söz kes,
tekrar dene) JS'te yaşar (`window.__voiceCall`); tek doğruluk kaynağı istemcidir.

**Tech Stack:** Blazor WebAssembly (.NET 10), vanilla JS (Web Audio API), xUnit v3 + FluentAssertions
(`tests/CustomerSupportBot.Web.Tests`), Playwright (tarayıcı doğrulaması).

**Spec:** `docs/superpowers/specs/2026-10-04-voice-call-ui-design.md`

## Global Constraints

- Yalnızca `src/CustomerSupportBot.Web` ve `tests/CustomerSupportBot.Web.Tests` değişir; arka uç değişmez.
- Ekran her boyutta tam sayfa (`position: fixed; inset: 0`), koyu radyal zemin; küçültme yok.
- Durum etiketleri: "Bağlanıyor…", "Dinliyor", "Düşünüyor", "Konuşuyor", "Mikrofon kapalı", "Temsilciye aktarılıyor".
- Üst çubukta "Yapay zekâ ile konuşuyorsunuz" bildirimi ve mm:ss süre.
- Kontroller: Sessize al · Bitir (kırmızı, ortada) · Altyazı. Klavye: `Space` sessize al, `Esc` bitir.
- Altyazı yalnızca son tur; tercih `localStorage` anahtarı `csb-voice-captions` ("1"/"0"); erişilemezse açık.
- Söz kesme küreye dokunarak (yalnızca "Konuşuyor" durumunda); sesle söz kesme kapsam dışı.
- `prefers-reduced-motion: reduce` iken animasyon döngüsü başlamaz, CSS animasyonları kapalı.
- Sesler Web Audio ile üretilir (dosya yok): bağlanınca yükselen, bitince alçalan iki nota.
- Commit mesajları `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` ile biter. Push yok.

## Review Focus

1. **Sessizdeyken asistan konuşup bitirince** istemci dinlemeye döner — mikrofon kapalı kalmalı (Task 2, tarayıcı adımı).
2. **Yeni tur başladıktan sonra eski turun son transkripti gelirse** (model gecikmesi) yeni altyazının üzerine yazılmamalı (Task 1, test).
3. **Mikrofon izni reddi → "Tekrar dene"** ekranı sıfırlayıp tek bir yeni istemci başlatmalı; kapanış zamanlayıcısı tetiklenmemeli (Task 5, tarayıcı adımı).
4. **Odak bir düğmedeyken `Space`** yalnızca bir kez sessize almalı (düğmenin kendi tıklamasıyla çift tetiklenmemeli) (Task 5, tarayıcı adımı).
5. **Görüşme sırasında temsilci katılırsa** (`human_joined` → `__stopVoice`) ekran kapanmalı ve küre döngüsü durmalı (Task 5, tarayıcı adımı).

---

## Dosya yapısı

| Dosya | Durum | Sorumluluk |
|---|---|---|
| `src/CustomerSupportBot.Web/Models/VoiceCallModel.cs` | Yeni | Ekran durumu: olay → durum, kürenin hâli, etiket, altyazı, çip, süre. Saf C#. |
| `tests/CustomerSupportBot.Web.Tests/VoiceCallModelTests.cs` | Yeni | Modelin birim testleri. |
| `src/CustomerSupportBot.Web/wwwroot/js/realtime-client.js` | Değişir | `AnalyserNode`'lar, `getLevels()`, `setMuted()` / `muted`, sessizin dinlemeye geçişte korunması. |
| `src/CustomerSupportBot.Web/Components/VoiceCallOverlay.razor` | Yeni | Tam sayfa ekranın çizimi; süre için saniyelik yenileme. |
| `src/CustomerSupportBot.Web/wwwroot/css/voice-call.css` | Yeni | Ekran ve küre stilleri (`--level`). |
| `src/CustomerSupportBot.Web/wwwroot/index.html` | Değişir | `voice-call.css` bağlantısı. |
| `src/CustomerSupportBot.Web/Pages/Chat.razor` | Değişir | Bileşeni barındırır, `VoiceCallEvent`, altyazı tercihi, eski durum satırı kaldırılır, `voice-orb.js` yüklenir. |
| `src/CustomerSupportBot.Web/wwwroot/css/styles.css` | Değişir | Eski `.voice-status` / `.voice-pulse` / `.voice-mini` stilleri kaldırılır. |
| `src/CustomerSupportBot.Web/wwwroot/js/voice-orb.js` | Yeni | `requestAnimationFrame` döngüsü: seviye → `--level`. |
| `src/CustomerSupportBot.Web/wwwroot/js/chat-bridge.js` | Değişir | `chatApp.voiceCall(type, a, b)` → `VoiceCallEvent`. |
| `src/CustomerSupportBot.Web/wwwroot/js/realtime-ui.js` | Değişir | Ekranı sürer: olaylar, `window.__voiceCall`, klavye, sesler, odak; balon davranışı aynen. |
| `docs/CustomerSupportBot.Web/Pages/Chat.md`, `README.md` | Değişir | Belgeler. |

---

### Task 1: VoiceCallModel (ekran durumu, saf C#)

**Files:**
- Create: `src/CustomerSupportBot.Web/Models/VoiceCallModel.cs`
- Test: `tests/CustomerSupportBot.Web.Tests/VoiceCallModelTests.cs`

**Interfaces:**
- Produces:
  - `enum VoiceOrbState { Connecting, Listening, Thinking, Speaking, Muted, Handoff, Error }`
  - `sealed class VoiceCallModel` — `bool Open`, `bool Muted`, `string? UserCaption`, `bool UserCaptionLive`,
    `string AssistantCaption`, `string? Chip`, `string? Error`, `string? EndedMessage`,
    `VoiceOrbState Orb`, `string StateLabel`, `string OrbCssClass` (ör. `"speaking"`),
    `string Elapsed(DateTimeOffset now)`, `void Apply(string type, string? a, string? b, DateTimeOffset now)`.
  - Olay tipleri (`type`): `open`, `connected`, `state`(a=istemci durumu), `speech_started`,
    `speech_stopped`, `user_delta`(a=itemId, b=metin), `user_final`(a=itemId|null, b=metin),
    `assistant_delta`(a=metin), `response_done`, `tool_call`(a=ad, b=etiket), `tool_result`(a=ad),
    `muted`(a="true"/"false"), `error`(a=mesaj), `ended`(a=mesaj), `close`.

- [ ] **Step 1: Write the failing tests**

`tests/CustomerSupportBot.Web.Tests/VoiceCallModelTests.cs`:

```csharp
// Sesli görüşme ekranının durumu: JS olayları → kürenin hâli, altyazı, çipler, süre.

using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Tests;

public class VoiceCallModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);

    private static VoiceCallModel Opened()
    {
        var m = new VoiceCallModel();
        m.Apply("open", null, null, T0);
        return m;
    }

    [Fact]
    public void Open_StartsConnecting_AndResetsThePreviousCall()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Apply("user_delta", "i1", "merhaba", T0);
        m.Apply("muted", "true", null, T0);
        m.Apply("error", "⚠ koptu", null, T0);

        m.Apply("open", null, null, T0);

        m.Open.Should().BeTrue();
        m.Orb.Should().Be(VoiceOrbState.Connecting);
        m.StateLabel.Should().Be("Bağlanıyor…");
        m.UserCaption.Should().BeNull();
        m.Muted.Should().BeFalse();
        m.Error.Should().BeNull();
    }

    [Fact]
    public void ListeningThinkingSpeaking_FollowTheConversation()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Listening);

        m.Apply("speech_stopped", null, null, T0);
        m.Orb.Should().Be(VoiceOrbState.Thinking);
        m.StateLabel.Should().Be("Düşünüyor");

        m.Apply("state", "speaking", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Speaking);

        m.Apply("state", "listening", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Listening, "konuşma düşünme hâlini bitirir");
    }

    [Fact]
    public void Muted_IsShownWhileListening_ButSpeakingStillShowsTheAssistant()
    {
        var m = Opened();
        m.Apply("state", "listening", null, T0);
        m.Apply("muted", "true", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Muted);
        m.StateLabel.Should().Be("Mikrofon kapalı");

        m.Apply("state", "speaking", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Speaking);
        m.Muted.Should().BeTrue();

        m.Apply("muted", "false", null, T0);
        m.Apply("state", "listening", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Listening);
    }

    [Fact]
    public void Handoff_AndError_TakePriority()
    {
        var m = Opened();
        m.Apply("state", "speaking", null, T0);
        m.Apply("tool_call", "human_handoff_tool", "Temsilci talebi oluşturuluyor", T0);
        m.Orb.Should().Be(VoiceOrbState.Handoff);
        m.StateLabel.Should().Be("Temsilciye aktarılıyor");

        m.Apply("error", "⚠ Bağlantı koptu", null, T0);
        m.Orb.Should().Be(VoiceOrbState.Error);
        m.Error.Should().Be("⚠ Bağlantı koptu");
    }

    [Fact]
    public void Captions_ShowOnlyTheLatestTurn()
    {
        var m = Opened();
        m.Apply("user_delta", "i1", "Siparişim ", T0);
        m.Apply("user_delta", "i1", "nerede", T0);
        m.UserCaption.Should().Be("Siparişim nerede");
        m.UserCaptionLive.Should().BeTrue();

        m.Apply("assistant_delta", "Hangi ", null, T0);
        m.Apply("assistant_delta", "sipariş?", null, T0);
        m.Apply("response_done", null, null, T0);
        m.Apply("user_final", "i1", "Siparişim nerede?", T0);
        m.UserCaption.Should().Be("Siparişim nerede?");
        m.UserCaptionLive.Should().BeFalse();
        m.AssistantCaption.Should().Be("Hangi sipariş?");

        m.Apply("user_delta", "i2", "10248", T0);
        m.UserCaption.Should().Be("10248");
        m.AssistantCaption.Should().BeEmpty("yeni tur önceki yanıtı ekrandan kaldırır");
    }

    [Fact]
    public void LateFinalOfAnOlderTurn_DoesNotOverwriteTheCurrentCaption()
    {
        var m = Opened();
        m.Apply("user_delta", "i1", "birinci", T0);
        m.Apply("user_delta", "i2", "ikinci", T0);

        m.Apply("user_final", "i1", "birinci cümle", T0);

        m.UserCaption.Should().Be("ikinci");
    }

    [Fact]
    public void WithoutLiveDeltas_ANewResponseReplacesTheOldAssistantCaption()
    {
        var m = Opened();
        m.Apply("assistant_delta", "İlk yanıt.", null, T0);
        m.Apply("response_done", null, null, T0);
        m.Apply("user_final", null, "ikinci soru", T0);

        m.Apply("assistant_delta", "İkinci yanıt.", null, T0);

        m.AssistantCaption.Should().Be("İkinci yanıt.");
        m.UserCaption.Should().Be("ikinci soru");
    }

    [Fact]
    public void Chips_ShowToolsApprovalsAndHandoff()
    {
        var m = Opened();
        m.Apply("tool_call", "order_status_tool", "Sipariş durumu sorgulanıyor", T0);
        m.Chip.Should().Be("🔎 Sipariş durumu sorgulanıyor");
        m.Orb.Should().Be(VoiceOrbState.Thinking);
        m.Apply("tool_result", "order_status_tool", null, T0);
        m.Chip.Should().BeNull();

        m.Apply("tool_call", "return_request_tool", "İade talebi onaya gönderiliyor", T0);
        m.Chip.Should().Be("⏳ İade talebi onaya gönderiliyor");
        m.Apply("tool_result", "return_request_tool", null, T0);
        m.Chip.Should().Be("⏳ Talebiniz onaya gönderildi");

        m.Apply("tool_call", "human_handoff_tool", "Temsilci talebi oluşturuluyor", T0);
        m.Apply("tool_result", "human_handoff_tool", null, T0);
        m.Chip.Should().Be("👤 Temsilci talebi oluşturuldu");
    }

    [Fact]
    public void Elapsed_CountsFromConnection()
    {
        var m = Opened();
        m.Elapsed(T0.AddSeconds(30)).Should().Be("00:00");

        m.Apply("connected", null, null, T0);

        m.Elapsed(T0.AddSeconds(84)).Should().Be("01:24");
        m.Elapsed(T0.AddMinutes(75)).Should().Be("75:00");
    }

    [Fact]
    public void EndedAndClose()
    {
        var m = Opened();
        m.Apply("ended", "Görüşme sonlandırıldı.", null, T0);
        m.EndedMessage.Should().Be("Görüşme sonlandırıldı.");

        m.Apply("close", null, null, T0);
        m.Open.Should().BeFalse();
    }

    [Fact]
    public void OrbCssClass_IsTheLowercaseStateName()
    {
        var m = Opened();
        m.OrbCssClass.Should().Be("connecting");

        m.Apply("tool_call", "human_handoff_tool", "x", T0);

        m.OrbCssClass.Should().Be("handoff");
    }

    [Fact]
    public void UnknownEvent_IsIgnored()
    {
        var m = Opened();
        var act = () => m.Apply("something_new", "x", "y", T0);
        act.Should().NotThrow();
        m.Orb.Should().Be(VoiceOrbState.Connecting);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/CustomerSupportBot.Web.Tests -- --filter-class "*VoiceCallModelTests"`
Expected: build error — `VoiceCallModel` / `VoiceOrbState` not found.

- [ ] **Step 3: Implement the model**

`src/CustomerSupportBot.Web/Models/VoiceCallModel.cs`:

```csharp
// Models/VoiceCallModel.cs
// Sesli görüşme ekranının durumu — realtime-ui.js olaylarından beslenir, VoiceCallOverlay çizer.

namespace CustomerSupportBot.Web.Models;

/// <summary>Kürenin görünümü. Öncelik: Error > Handoff > Speaking > Muted > Connecting > Thinking > Listening.</summary>
public enum VoiceOrbState { Connecting, Listening, Thinking, Speaking, Muted, Handoff, Error }

/// <summary>
/// Görüşme ekranının tek durum kaynağı. JS (realtime-ui.js) her olayı <see cref="Apply"/> ile
/// iletir; bileşen yalnızca okur. Saf sınıf — tarayıcı olmadan birim testlenir.
///
/// <para>
/// <b>Altyazı yalnızca son tur:</b> dökümün tamamı zaten sohbete balon olarak yazılıyor. Yeni bir
/// kullanıcı konuşması (yeni itemId) önceki yanıtı ekrandan kaldırır; canlı parçası olmayan
/// modellerde ise biten bir yanıttan sonra gelen ilk asistan parçası eskisinin yerine geçer.
/// Kullanıcının son transkripti asistan yanıtından SONRA gelir (sağlayıcı sırası); eski bir turun
/// geç gelen son transkripti yeni turun altyazısının üzerine yazılmaz.
/// </para>
/// </summary>
public sealed class VoiceCallModel
{
    public const string HandoffTool = "human_handoff_tool";

    private static readonly HashSet<string> SideEffectTools =
    [
        "order_placement_tool", "order_cancel_tool", "return_request_tool", "complaint_registration_tool"
    ];

    private string _clientState = "connecting";
    private bool _thinking;
    private bool _handoff;
    private bool _assistantTurnDone;
    private string? _userItemId;
    private DateTimeOffset? _connectedAt;

    public bool Open { get; private set; }
    public bool Muted { get; private set; }
    public string? UserCaption { get; private set; }
    public bool UserCaptionLive { get; private set; }
    public string AssistantCaption { get; private set; } = "";
    public string? Chip { get; private set; }
    public string? Error { get; private set; }
    public string? EndedMessage { get; private set; }

    public VoiceOrbState Orb =>
        Error is not null ? VoiceOrbState.Error
        : _handoff ? VoiceOrbState.Handoff
        : _clientState == "speaking" ? VoiceOrbState.Speaking
        : Muted ? VoiceOrbState.Muted
        : _clientState == "connecting" ? VoiceOrbState.Connecting
        : _thinking ? VoiceOrbState.Thinking
        : VoiceOrbState.Listening;

    public string StateLabel => Orb switch
    {
        VoiceOrbState.Connecting => "Bağlanıyor…",
        VoiceOrbState.Listening => "Dinliyor",
        VoiceOrbState.Thinking => "Düşünüyor",
        VoiceOrbState.Speaking => "Konuşuyor",
        VoiceOrbState.Muted => "Mikrofon kapalı",
        VoiceOrbState.Handoff => "Temsilciye aktarılıyor",
        _ => Error ?? "Hata"
    };

    public string OrbCssClass => Orb.ToString().ToLowerInvariant();

    /// <summary>Bağlantıdan bu yana geçen süre, "mm:ss" (60 dakikayı aşınca dakika büyür).</summary>
    public string Elapsed(DateTimeOffset now)
    {
        if (_connectedAt is not { } start || now < start) return "00:00";
        var t = now - start;
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    public void Apply(string type, string? a, string? b, DateTimeOffset now)
    {
        switch (type)
        {
            case "open":
                Reset();
                Open = true;
                break;
            case "connected":
                _connectedAt ??= now;
                break;
            case "state":
                _clientState = a ?? _clientState;
                if (_clientState == "speaking") _thinking = false;
                if (_clientState == "listening") _connectedAt ??= now;
                break;
            case "speech_started":
                _thinking = false;
                break;
            case "speech_stopped":
                _thinking = true;
                break;
            case "user_delta":
                if (a != _userItemId)
                {
                    _userItemId = a;
                    UserCaption = "";
                    AssistantCaption = "";
                    _assistantTurnDone = false;
                }
                UserCaption += b ?? "";
                UserCaptionLive = true;
                break;
            case "user_final":
                if (a is null || _userItemId is null || a == _userItemId)
                {
                    UserCaption = b;
                    UserCaptionLive = false;
                }
                break;
            case "assistant_delta":
                if (_assistantTurnDone)
                {
                    AssistantCaption = "";
                    _assistantTurnDone = false;
                }
                AssistantCaption += a ?? "";
                _thinking = false;
                break;
            case "response_done":
                _assistantTurnDone = true;
                _thinking = false;
                break;
            case "tool_call":
                _thinking = true;
                if (a == HandoffTool) _handoff = true;
                Chip = (a == HandoffTool || (a is not null && SideEffectTools.Contains(a)) ? "⏳ " : "🔎 ") + (b ?? a);
                break;
            case "tool_result":
                Chip = a == HandoffTool ? "👤 Temsilci talebi oluşturuldu"
                    : a is not null && SideEffectTools.Contains(a) ? "⏳ Talebiniz onaya gönderildi"
                    : null;
                break;
            case "muted":
                Muted = a == "true";
                break;
            case "error":
                Error = string.IsNullOrWhiteSpace(a) ? "Bir hata oluştu." : a;
                break;
            case "ended":
                EndedMessage = a;
                break;
            case "close":
                Open = false;
                break;
        }
    }

    private void Reset()
    {
        _clientState = "connecting";
        _thinking = _handoff = _assistantTurnDone = false;
        _userItemId = null;
        _connectedAt = null;
        Muted = false;
        UserCaption = null;
        UserCaptionLive = false;
        AssistantCaption = "";
        Chip = Error = EndedMessage = null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/CustomerSupportBot.Web.Tests -- --filter-class "*VoiceCallModelTests"`
Expected: 12 tests, all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CustomerSupportBot.Web/Models/VoiceCallModel.cs tests/CustomerSupportBot.Web.Tests/VoiceCallModelTests.cs
git commit -m "feat(web): voice call screen state model" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: realtime-client.js — ses seviyesi ve kalıcı sessize alma

**Files:**
- Modify: `src/CustomerSupportBot.Web/wwwroot/js/realtime-client.js`

**Interfaces:**
- Produces: `client.getLevels()` → `{ input: number, output: number }` (0–1, RMS×4 kırpılmış);
  `client.setMuted(bool)`; `client.muted` (getter); geri çağrı `muted({ muted })`.

- [ ] **Step 1: Constructor alanları**

`this._micTrack = null;` satırının altına ekle:

```js
            this._muted = false;       // kullanıcı sessize aldı — dinlemeye dönüşte de kapalı kalır
            this._inAnalyser = null;   // mikrofon seviyesi (küre)
            this._outAnalyser = null;  // asistan sesi seviyesi (küre)
            this._levelBuf = null;
```

- [ ] **Step 2: Public API — `interrupt()` metodunun hemen üstüne ekle**

```js
        get muted() { return this._muted; }

        /** Kalıcı sessize alma: asistan konuşmayı bitirip dinlemeye dönüldüğünde de mikrofon kapalı kalır. */
        setMuted(muted) {
            this._muted = !!muted;
            if (this._micTrack) {
                try { this._micTrack.enabled = !this._muted && this.state !== 'speaking'; } catch { }
            }
            this._emit('muted', { muted: this._muted });
        }

        /** Kürenin anlık seviyeleri (0–1): mikrofon ve asistan sesi. Ses yoksa 0. */
        getLevels() {
            return { input: this._rms(this._inAnalyser), output: this._rms(this._outAnalyser) };
        }
```

- [ ] **Step 3: `_initAudio` — analizörler**

`this.sourceNode.connect(this.workletNode);` satırının altına:

```js
            // Küre için mikrofon seviyesi — analizör hiçbir yere bağlanmaz (yalnızca ölçer).
            this._inAnalyser = this.audioCtx.createAnalyser();
            this._inAnalyser.fftSize = 512;
            this.sourceNode.connect(this._inAnalyser);
```

`this._playCursor = this.playCtx.currentTime;` satırının altına:

```js
            // Asistan sesi tek bir analizörden geçip hoparlöre gider (küre seviyesi).
            this._outAnalyser = this.playCtx.createAnalyser();
            this._outAnalyser.fftSize = 512;
            this._outAnalyser.connect(this.playCtx.destination);
```

- [ ] **Step 4: Çalma kaynağını analizöre bağla**

Değiştir: `src.connect(this.playCtx.destination);` → `src.connect(this._outAnalyser || this.playCtx.destination);`

- [ ] **Step 5: `stop()` içinde temizle**

`this._micTrack = null;` (stop içindeki) satırının altına:

```js
            this._inAnalyser = null;
            this._outAnalyser = null;
```

- [ ] **Step 6: `_setState` sessizi korusun ve `_rms` yardımcısı**

`_setState` içinde `} else if (s === 'listening' || s === 'idle') {` bloğunu şununla değiştir:

```js
                } else if (s === 'listening' || s === 'idle') {
                    try { this._micTrack.enabled = !this._muted; } catch { }
                }
```

`_setState` metodunun hemen üstüne ekle:

```js
        _rms(analyser) {
            if (!analyser) return 0;
            if (!this._levelBuf || this._levelBuf.length !== analyser.fftSize)
                this._levelBuf = new Float32Array(analyser.fftSize);
            analyser.getFloatTimeDomainData(this._levelBuf);
            let sum = 0;
            for (let i = 0; i < this._levelBuf.length; i++) sum += this._levelBuf[i] * this._levelBuf[i];
            return Math.min(1, Math.sqrt(sum / this._levelBuf.length) * 4);
        }
```

Ayrıca dosya başındaki "Public API" yorumuna `client.setMuted(true);` ve `client.getLevels();` satırlarını ekle.

- [ ] **Step 7: Tarayıcıda doğrula (Review Focus #1)**

Web'i başlat (`dotnet run --project src/CustomerSupportBot.Web --urls http://localhost:5288`), herhangi bir
sayfayı aç ve Playwright `browser_evaluate` ile istemciyi gerçek ağ olmadan sına (müşteri sayfası
`/js/realtime-client.js`'i yükler; değilse `await loadScript('/js/realtime-client.js','t')`):

```js
async () => {
  const c = new RealtimeClient({ callbacks: {} });
  const track = { enabled: true };
  c._micTrack = track;
  c._setState('listening');
  c.setMuted(true);
  const afterMute = track.enabled;
  c._setState('speaking');
  c._setState('listening');
  const afterListen = track.enabled;
  c.setMuted(false);
  return { afterMute, afterListen, afterUnmute: track.enabled, levels: c.getLevels() };
}
```

Expected: `{ afterMute: false, afterListen: false, afterUnmute: true, levels: { input: 0, output: 0 } }`.

- [ ] **Step 8: Commit**

```bash
git add src/CustomerSupportBot.Web/wwwroot/js/realtime-client.js
git commit -m "feat(web): audio levels and persistent mute in the realtime client" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: VoiceCallOverlay bileşeni, stiller ve Chat.razor'a yerleştirme

**Files:**
- Create: `src/CustomerSupportBot.Web/Components/VoiceCallOverlay.razor`
- Create: `src/CustomerSupportBot.Web/wwwroot/css/voice-call.css`
- Modify: `src/CustomerSupportBot.Web/wwwroot/index.html:21` (stylesheet bağlantısı)
- Modify: `src/CustomerSupportBot.Web/Pages/Chat.razor` (bileşen, `VoiceCallEvent`, tercih, eski durum satırı)
- Modify: `src/CustomerSupportBot.Web/wwwroot/css/styles.css` (eski `.voice-status`/`.voice-pulse`/`.voice-mini`)

**Interfaces:**
- Consumes: `VoiceCallModel`, `VoiceOrbState` (Task 1).
- Produces: `[JSInvokable] public void VoiceCallEvent(string type, string? a, string? b)` (Chat.razor);
  küre öğesi `id="voiceOrb"`, sınıfı `vc-orb vc-orb--{OrbCssClass}`; Bitir düğmesi sınıfı `vc-btn--end`;
  bileşen düğmeleri JS'i çağırır: `__voiceCall.toggleMute`, `__voiceCall.end`, `__voiceCall.interrupt`,
  `__voiceCall.retry` (Task 5 tanımlar).

- [ ] **Step 1: Bileşen**

`src/CustomerSupportBot.Web/Components/VoiceCallOverlay.razor`:

```razor
@* Tam sayfa sesli görüşme ekranı. Durum VoiceCallModel'den okunur; düğmeler olayları üst
   bileşene bildirir (asıl iş realtime-ui.js'te — tek doğruluk kaynağı istemci). Küre boyutu ve
   parlaması voice-orb.js'in kare başı yazdığı --level CSS değişkeninden gelir; Blazor her karede
   yeniden çizilmez (style özniteliği burada çizilmediği için JS'in yazdığı değer korunur). *@
@using CustomerSupportBot.Web.Models
@implements IDisposable

@if (Model.Open)
{
    <div class="vc-overlay" role="dialog" aria-modal="true" aria-label="Sesli görüşme">
        <header class="vc-top">
            <div class="vc-title">
                <span class="vc-rec" aria-hidden="true"></span>
                <strong>Destek Asistanı</strong>
                <span class="vc-time">@Model.Elapsed(DateTimeOffset.UtcNow)</span>
            </div>
            <span class="vc-ai">Yapay zekâ ile konuşuyorsunuz</span>
        </header>

        <main class="vc-center">
            @if (Model.Error is not null)
            {
                <div class="vc-error" role="alert">
                    <p>@Model.Error</p>
                    <div class="vc-error-actions">
                        <button type="button" class="vc-text-btn" @onclick="OnRetry">Tekrar dene</button>
                        <button type="button" class="vc-text-btn" @onclick="OnEnd">Kapat</button>
                    </div>
                </div>
            }
            else
            {
                var speaking = Model.Orb == VoiceOrbState.Speaking;
                <button id="voiceOrb" type="button"
                        class="vc-orb vc-orb--@Model.OrbCssClass"
                        disabled="@(!speaking)"
                        title="@(speaking ? "Sözünü kesmek için dokunun" : null)"
                        aria-label="@(speaking ? "Asistanın sözünü kes" : Model.StateLabel)"
                        @onclick="OnInterrupt">
                    @if (Model.Muted)
                    {
                        <span class="vc-muted-badge" aria-hidden="true">🔇</span>
                    }
                </button>
                <div class="vc-state" aria-live="polite">@(Model.EndedMessage ?? Model.StateLabel)</div>
                @if (Model.Chip is not null)
                {
                    <div class="vc-chip">@Model.Chip</div>
                }
            }
        </main>

        @if (CaptionsOn && Model.Error is null)
        {
            <section class="vc-captions">
                @if (!string.IsNullOrEmpty(Model.UserCaption))
                {
                    <p class="vc-cap-user @(Model.UserCaptionLive ? "live" : "")">Sen: @Model.UserCaption</p>
                }
                @if (!string.IsNullOrEmpty(Model.AssistantCaption))
                {
                    <p class="vc-cap-bot">@Model.AssistantCaption</p>
                }
            </section>
        }

        <footer class="vc-controls">
            <div class="vc-ctl">
                <button type="button" class="vc-btn" aria-pressed="@(Model.Muted ? "true" : "false")"
                        aria-label="@(Model.Muted ? "Mikrofonu aç" : "Sessize al")" @onclick="OnToggleMute">
                    @if (Model.Muted)
                    {
                        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="2" y1="2" x2="22" y2="22"/><path d="M18.89 13.23A7 7 0 0 0 19 12v-2"/><path d="M5 10v2a7 7 0 0 0 12 5"/><path d="M15 9.34V5a3 3 0 0 0-5.68-1.33"/><path d="M9 9v3a3 3 0 0 0 5.12 2.12"/><line x1="12" y1="19" x2="12" y2="22"/></svg>
                    }
                    else
                    {
                        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="2" width="6" height="12" rx="3"/><path d="M5 10v2a7 7 0 0 0 14 0v-2"/><line x1="12" y1="19" x2="12" y2="22"/></svg>
                    }
                </button>
                <span class="vc-ctl-label">@(Model.Muted ? "Sesi aç" : "Sessize al")</span>
            </div>
            <div class="vc-ctl">
                <button type="button" class="vc-btn vc-btn--end" aria-label="Görüşmeyi bitir" @onclick="OnEnd">
                    <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/></svg>
                </button>
                <span class="vc-ctl-label">Bitir</span>
            </div>
            <div class="vc-ctl">
                <button type="button" class="vc-btn vc-btn--cc" aria-pressed="@(CaptionsOn ? "true" : "false")"
                        aria-label="@(CaptionsOn ? "Altyazıyı kapat" : "Altyazıyı aç")" @onclick="OnToggleCaptions">CC</button>
                <span class="vc-ctl-label">Altyazı</span>
            </div>
        </footer>
    </div>
}

@code {
    [Parameter, EditorRequired] public VoiceCallModel Model { get; set; } = default!;
    [Parameter] public bool CaptionsOn { get; set; } = true;
    [Parameter] public EventCallback OnToggleMute { get; set; }
    [Parameter] public EventCallback OnEnd { get; set; }
    [Parameter] public EventCallback OnToggleCaptions { get; set; }
    [Parameter] public EventCallback OnInterrupt { get; set; }
    [Parameter] public EventCallback OnRetry { get; set; }

    // Süre göstergesi için saniyede bir yeniden çizim — yalnızca ekran açıkken çalışır.
    private Timer? _clock;

    protected override void OnParametersSet()
    {
        if (Model.Open && _clock is null)
            _clock = new Timer(_ => InvokeAsync(StateHasChanged), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        else if (!Model.Open)
            StopClock();
    }

    private void StopClock()
    {
        _clock?.Dispose();
        _clock = null;
    }

    public void Dispose() => StopClock();
}
```

- [ ] **Step 2: Stiller**

`src/CustomerSupportBot.Web/wwwroot/css/voice-call.css`:

```css
/* Sesli görüşme ekranı — tam sayfa, her temada koyu. Küre boyutu/parlaması --level'dan (0–1)
   gelir; voice-orb.js her karede yazar. Hareket azaltmada animasyonlar kapalıdır. */

.vc-overlay {
    position: fixed;
    inset: 0;
    z-index: 1000;
    display: flex;
    flex-direction: column;
    align-items: center;
    background: radial-gradient(120% 80% at 50% 0%, #1e293b 0%, #0b1020 70%);
    color: #e2e8f0;
    font-family: Inter, system-ui, sans-serif;
    animation: vcFadeIn .2s ease-out;
}

@keyframes vcFadeIn { from { opacity: 0; } to { opacity: 1; } }

.vc-top {
    width: 100%;
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 12px;
    padding: 16px 20px;
    box-sizing: border-box;
    font-size: 14px;
}

.vc-title { display: flex; align-items: center; gap: 10px; }
.vc-time { opacity: .65; font-variant-numeric: tabular-nums; }
.vc-ai { font-size: 12px; opacity: .55; text-align: right; }

.vc-rec {
    width: 9px;
    height: 9px;
    border-radius: 50%;
    background: #ef4444;
    animation: vcBlink 1.4s ease-in-out infinite;
}

@keyframes vcBlink { 50% { opacity: .3; } }

.vc-center {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 16px;
    min-height: 0;
}

.vc-orb {
    --level: 0;
    position: relative;
    width: clamp(120px, 24vmin, 180px);
    aspect-ratio: 1;
    border: 0;
    padding: 0;
    border-radius: 50%;
    background: radial-gradient(circle at 35% 30%, #bfdbfe 0%, #60a5fa 30%, #2563eb 60%, #1e3a8a 100%);
    box-shadow: 0 0 calc(40px + 50px * var(--level)) rgba(59, 130, 246, calc(.4 + .4 * var(--level))),
                inset 0 -12px 30px rgba(15, 23, 42, .45);
    transform: scale(calc(1 + .18 * var(--level)));
    transition: background .4s ease, filter .4s ease, opacity .4s ease;
    cursor: default;
}

.vc-orb:not(:disabled) { cursor: pointer; }
.vc-orb:focus-visible, .vc-btn:focus-visible, .vc-text-btn:focus-visible {
    outline: 2px solid #93c5fd;
    outline-offset: 4px;
}

.vc-orb--connecting { opacity: .35; animation: vcBreathe 2.4s ease-in-out infinite; }

.vc-orb--listening {
    background: radial-gradient(circle at 35% 30%, #e0f2fe 0%, #7dd3fc 30%, #0ea5e9 60%, #0c4a6e 100%);
    box-shadow: 0 0 calc(40px + 50px * var(--level)) rgba(14, 165, 233, calc(.4 + .4 * var(--level))),
                inset 0 -12px 30px rgba(15, 23, 42, .45);
}

.vc-orb--thinking {
    background: conic-gradient(from 0deg, #1e3a8a, #60a5fa, #a78bfa, #1e3a8a);
    animation: vcSpin 2.2s linear infinite;
}

.vc-orb--muted { filter: grayscale(1); opacity: .45; }

.vc-orb--handoff {
    background: radial-gradient(circle at 35% 30%, #dcfce7 0%, #4ade80 30%, #16a34a 60%, #14532d 100%);
    box-shadow: 0 0 50px rgba(34, 197, 94, .5), inset 0 -12px 30px rgba(15, 23, 42, .45);
}

@keyframes vcBreathe {
    0%, 100% { opacity: .25; transform: scale(.92); }
    50%      { opacity: .5;  transform: scale(1); }
}

@keyframes vcSpin { to { transform: rotate(360deg); } }

.vc-muted-badge {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 34px;
    filter: grayscale(0);
}

.vc-state { font-size: 15px; opacity: .85; min-height: 1.4em; text-align: center; }

.vc-chip {
    font-size: 13px;
    padding: 5px 12px;
    border-radius: 999px;
    background: rgba(96, 165, 250, .15);
    color: #93c5fd;
}

.vc-captions {
    width: min(680px, 88%);
    display: flex;
    flex-direction: column;
    gap: 8px;
    text-align: center;
    margin-bottom: 8px;
}

.vc-captions p { margin: 0; }
.vc-cap-user { font-size: 14px; opacity: .55; }
.vc-cap-user.live { font-style: italic; }
.vc-cap-bot { font-size: 18px; line-height: 1.5; }

.vc-controls {
    display: flex;
    gap: 28px;
    align-items: flex-start;
    padding: 18px 0 calc(28px + env(safe-area-inset-bottom));
}

.vc-ctl { display: flex; flex-direction: column; align-items: center; gap: 8px; }
.vc-ctl-label { font-size: 12px; opacity: .6; }

.vc-btn {
    width: 58px;
    height: 58px;
    border-radius: 50%;
    border: 0;
    background: rgba(255, 255, 255, .1);
    color: #e2e8f0;
    display: flex;
    align-items: center;
    justify-content: center;
    font-weight: 700;
    font-size: 15px;
    cursor: pointer;
    transition: background .15s ease;
}

.vc-btn:hover { background: rgba(255, 255, 255, .18); }
.vc-btn[aria-pressed="true"] { background: rgba(96, 165, 250, .28); }
.vc-btn--end { width: 70px; height: 70px; background: #ef4444; color: #fff; }
.vc-btn--end:hover { background: #dc2626; }

.vc-error { text-align: center; max-width: 420px; padding: 0 16px; }
.vc-error p { font-size: 16px; margin: 0 0 16px; }
.vc-error-actions { display: flex; gap: 12px; justify-content: center; }

.vc-text-btn {
    border: 1px solid rgba(255, 255, 255, .25);
    background: transparent;
    color: #e2e8f0;
    border-radius: 999px;
    padding: 8px 18px;
    font-size: 14px;
    cursor: pointer;
}

.vc-text-btn:hover { background: rgba(255, 255, 255, .1); }

@media (max-width: 640px) {
    .vc-top { padding: 12px 16px; font-size: 13px; }
    .vc-ai { font-size: 11px; }
    .vc-cap-bot { font-size: 15px; }
    .vc-cap-user { font-size: 12.5px; }
    .vc-controls { gap: 22px; }
}

@media (prefers-reduced-motion: reduce) {
    .vc-overlay, .vc-rec, .vc-orb { animation: none !important; }
    .vc-orb { transform: none !important; transition: none; }
}
```

- [ ] **Step 3: Stil dosyasını bağla**

`src/CustomerSupportBot.Web/wwwroot/index.html` satır 21'deki `<link rel="stylesheet" href="css/styles.css" />`
satırının hemen altına:

```html
    <link rel="stylesheet" href="css/voice-call.css" />
```

- [ ] **Step 4: Chat.razor — bileşeni yerleştir, eski durum satırını kaldır**

(`CustomerSupportBot.Web.Models` `_Imports.razor`'da zaten var — ek `@using` gerekmez.)

Footer'daki şu bloğu tamamen sil:

```razor
            <div id="voiceStatus" class="voice-status" hidden="">
                <span class="voice-pulse"></span>
                <span id="voiceStatusText">Bağlanıyor…</span>
                <button id="voiceInterruptBtn" class="voice-mini" title="Asistanı kes">⏸</button>
            </div>
```

`</footer>` satırının hemen altına (sayfanın kök `</div>`'lerinden önce):

```razor

        <VoiceCallOverlay Model="_voiceCall"
                          CaptionsOn="_voiceCaptionsOn"
                          OnToggleMute="VoiceCallToggleMuteAsync"
                          OnEnd="VoiceCallEndAsync"
                          OnToggleCaptions="VoiceCallToggleCaptionsAsync"
                          OnInterrupt="VoiceCallInterruptAsync"
                          OnRetry="VoiceCallRetryAsync" />
```

- [ ] **Step 5: Chat.razor — durum, olay girişi ve düğme işleyicileri**

`[JSInvokable] public void VoiceSetSession(string id)` metodunun hemen üstüne ekle:

```csharp
    // ── Sesli görüşme ekranı ─────────────────────────────────────────────────
    // realtime-ui.js her olayı VoiceCallEvent ile bildirir; durum VoiceCallModel'de. Düğmeler işi
    // JS'e bırakır (__voiceCall.*): istemci tek doğruluk kaynağıdır, sonuç yine olay olarak döner.

    private readonly VoiceCallModel _voiceCall = new();
    private bool _voiceCaptionsOn = true;
    private const string VoiceCaptionsKey = "csb-voice-captions";

    [JSInvokable]
    public void VoiceCallEvent(string type, string? a, string? b) =>
        _ = InvokeAsync(() =>
        {
            _voiceCall.Apply(type, a, b, DateTimeOffset.UtcNow);
            StateHasChanged();
        });

    private async Task VoiceCallToggleMuteAsync() => await JsSafeAsync("__voiceCall.toggleMute");
    private async Task VoiceCallEndAsync() => await JsSafeAsync("__voiceCall.end");
    private async Task VoiceCallInterruptAsync() => await JsSafeAsync("__voiceCall.interrupt");
    private async Task VoiceCallRetryAsync() => await JsSafeAsync("__voiceCall.retry");

    private async Task VoiceCallToggleCaptionsAsync()
    {
        _voiceCaptionsOn = !_voiceCaptionsOn;
        // Tercih yalnızca bu tarayıcıda; depolama kapalıysa (gizli pencere vb.) sessizce yok sayılır.
        try { await JS.InvokeVoidAsync("localStorage.setItem", VoiceCaptionsKey, _voiceCaptionsOn ? "1" : "0"); } catch { }
    }

    private async Task JsSafeAsync(string identifier)
    {
        try { await JS.InvokeVoidAsync(identifier); } catch (JSException) { }
    }
```

`OnAfterRenderAsync` içinde `await JS.InvokeVoidAsync("loadScript", "/js/realtime-client.js", "js-realtime-client");`
satırının **üstüne** ekle (altyazı tercihi + küre döngüsü betiği):

```csharp
        try { _voiceCaptionsOn = await JS.InvokeAsync<string?>("localStorage.getItem", VoiceCaptionsKey) != "0"; }
        catch { _voiceCaptionsOn = true; }
        await JS.InvokeVoidAsync("loadScript", "/js/voice-orb.js", "js-voice-orb");
```

- [ ] **Step 6: Eski durum satırı stillerini kaldır**

`src/CustomerSupportBot.Web/wwwroot/css/styles.css` içinde `.voice-status {` kuralından `.voice-mini:hover { … }`
kuralının sonuna kadar olan bloğu sil (`.voice-status`, `.voice-status[hidden]` ve yorumu, `.voice-pulse`,
`@keyframes voicePulseDot`, `.voice-mini`, `.voice-mini:hover`). `@keyframes voicePulse` ve
`.btn-voice` kuralları **kalır** (🎙 düğmesi kullanıyor). Sonra doğrula:

Run: `grep -rn "voice-status\|voice-pulse\|voice-mini\|voiceStatus\|voiceInterruptBtn" src/CustomerSupportBot.Web/Pages src/CustomerSupportBot.Web/wwwroot/css`
Expected: çıktı yok.

- [ ] **Step 7: Derle**

Run: `dotnet build src/CustomerSupportBot.Web -v q -nologo`
Expected: `Build succeeded.` (Bu aşamada ekran açılmaz — JS henüz olay göndermiyor; Task 5'te bağlanır.)

- [ ] **Step 8: Commit**

```bash
git add src/CustomerSupportBot.Web/Components/VoiceCallOverlay.razor src/CustomerSupportBot.Web/wwwroot/css/voice-call.css src/CustomerSupportBot.Web/wwwroot/index.html src/CustomerSupportBot.Web/Pages/Chat.razor src/CustomerSupportBot.Web/wwwroot/css/styles.css
git commit -m "feat(web): full-page voice call overlay component" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: voice-orb.js — küre animasyon döngüsü

**Files:**
- Create: `src/CustomerSupportBot.Web/wwwroot/js/voice-orb.js`

**Interfaces:**
- Consumes: `client.getLevels()` (Task 2); küre öğesi `#voiceOrb` ve sınıfları `vc-orb--listening` / `vc-orb--speaking` (Task 3).
- Produces: `window.voiceOrb.start(getLevels)` / `window.voiceOrb.stop()`; `window.voiceOrb.running` (bool, doğrulama için).

- [ ] **Step 1: Dosyayı yaz**

```js
// voice-orb.js
// Sesli görüşme ekranındaki kürenin kare başı animasyonu. Seviye realtime-client.js'in
// AnalyserNode'larından okunur ve #voiceOrb'a --level (0–1) olarak yazılır; Blazor yeniden
// çizilmez. Hangi seviyenin kullanılacağını kürenin durum sınıfı belirler (durumun tek kaynağı
// C# modeli): dinlerken mikrofon, konuşurken asistan sesi, diğer durumlarda 0.
// Hareket azaltma tercihinde döngü hiç başlamaz.

(function () {
    'use strict';

    const ATTACK = 0.5;    // yükselişte hızlı tepki
    const RELEASE = 0.08;  // düşüşte yavaş sönme — titremeyi önler

    let raf = 0;
    let level = 0;

    const reducedMotion = () =>
        !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);

    function start(getLevels) {
        stop();
        if (reducedMotion()) return;
        const tick = () => {
            const orb = document.getElementById('voiceOrb');
            if (orb) {
                let target = 0;
                try {
                    const levels = getLevels() || {};
                    if (orb.classList.contains('vc-orb--speaking')) target = levels.output || 0;
                    else if (orb.classList.contains('vc-orb--listening')) target = levels.input || 0;
                } catch { target = 0; }
                level += (target - level) * (target > level ? ATTACK : RELEASE);
                orb.style.setProperty('--level', level.toFixed(3));
            }
            raf = requestAnimationFrame(tick);
        };
        raf = requestAnimationFrame(tick);
    }

    function stop() {
        if (raf) cancelAnimationFrame(raf);
        raf = 0;
        level = 0;
    }

    window.voiceOrb = {
        start,
        stop,
        get running() { return raf !== 0; }
    };
})();
```

- [ ] **Step 2: Tarayıcıda doğrula**

Web'i başlat, müşteri sohbet sayfasında (`/js/voice-orb.js` Task 3'te yükleniyor) `browser_evaluate`:

```js
async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const orb = document.createElement('button');
  orb.id = 'voiceOrb'; orb.className = 'vc-orb vc-orb--speaking';
  document.body.appendChild(orb);
  voiceOrb.start(() => ({ input: 0, output: 0.8 }));
  await sleep(300);
  const high = parseFloat(orb.style.getPropertyValue('--level'));
  orb.className = 'vc-orb vc-orb--thinking';
  await sleep(600);
  const low = parseFloat(orb.style.getPropertyValue('--level'));
  voiceOrb.stop();
  orb.remove();
  return { high, low, running: voiceOrb.running };
}
```

Expected: `high` > 0.7, `low` < `high`, `running: false`.

- [ ] **Step 3: Commit**

```bash
git add src/CustomerSupportBot.Web/wwwroot/js/voice-orb.js
git commit -m "feat(web): audio-reactive orb animation loop" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: realtime-ui.js ve chat-bridge.js — ekranı sür

**Files:**
- Modify: `src/CustomerSupportBot.Web/wwwroot/js/chat-bridge.js` (`chatApp.voiceCall`)
- Modify: `src/CustomerSupportBot.Web/wwwroot/js/realtime-ui.js`

**Interfaces:**
- Consumes: `VoiceCallEvent` olay tipleri (Task 1/3), `client.setMuted/muted/getLevels` (Task 2), `voiceOrb.start/stop` (Task 4).
- Produces: `window.__voiceCall = { toggleMute(), end(), interrupt(), retry() }`; `window.__stopVoice` (mevcut — artık ekranı da kapatır).

- [ ] **Step 1: chat-bridge.js — olay kanalı**

`window.chatApp = {` nesnesinde `api: { … },` bloğundan sonra, `ui: {` satırından önce ekle:

```js
        // Sesli görüşme ekranı (VoiceCallOverlay) — olaylar sırayla .NET'e gider.
        voiceCall: function (type, a, b) {
            ref.invokeMethodAsync('VoiceCallEvent', type,
                a === undefined ? null : a, b === undefined ? null : b).catch(function () { });
        },
```

- [ ] **Step 2: realtime-ui.js — init, ekran olayları, yardımcılar**

`STATE_LABELS` sabitini sil. `init()`'in başını şununla değiştir (statusEl/statusText/interruptBtn kaldırılır):

```js
    function init() {
        const voiceBtn = document.getElementById('voiceBtn');
        if (!voiceBtn) return;

        let client = null;
        let overlayOpen = false;
        let connectedOnce = false;
        let closeTimer = null;

        const call = (type, a, b) => { try { window.chatApp?.voiceCall?.(type, a, b); } catch { } };

        const setStatus = (state) => {
            const isActive = state !== 'idle' && state !== 'error';
            voiceBtn.classList.toggle('active', isActive && client !== null);
            call('state', state);
        };

        const openOverlay = () => {
            clearTimeout(closeTimer);
            closeTimer = null;
            overlayOpen = true;
            connectedOnce = false;
            call('open');
            window.voiceOrb?.start(() => client?.getLevels?.() || { input: 0, output: 0 });
            // Erişilebilirlik: odak diyaloğa taşınır (Bitir düğmesi), kapanınca 🎙'ye döner.
            setTimeout(() => document.querySelector('.vc-btn--end')?.focus(), 60);
        };

        const closeOverlay = () => {
            clearTimeout(closeTimer);
            closeTimer = null;
            if (!overlayOpen) return;
            overlayOpen = false;
            window.voiceOrb?.stop();
            call('close');
            try { voiceBtn.focus(); } catch { }
        };

        const closeSoon = (ms) => {
            clearTimeout(closeTimer);
            closeTimer = setTimeout(closeOverlay, ms);
        };
```

- [ ] **Step 3: realtime-ui.js — istemci geri çağrılarına ekran olayları**

`startVoice` içinde `client = new RealtimeClient({` satırından **önce**:

```js
            openOverlay();
            let me = null;   // bu çağrının istemcisi — eski istemcinin geç olayları yenisini silmesin
```

ve `client = new RealtimeClient({` satırını `client = me = new RealtimeClient({` yap.

Geri çağrılarda şu değişiklikleri yap (mevcut balon kodu aynen kalır; satırlar ekleniyor):

```js
                    connected: ({ sessionId: sid, tools }) => {
                        connectedOnce = true;
                        call('connected');
                        playTone('connect');
                        // … mevcut gövde (setSession, refreshSessionList, console.info) aynen …
                    },
                    muted: ({ muted }) => call('muted', muted ? 'true' : 'false'),
                    speech_started: () => call('speech_started'),
```

- `speech_stopped: ({ itemId }) => {` gövdesinin **ilk satırı**: `call('speech_stopped');`
- `user_transcript_delta` gövdesinde `if (!itemId || !text || !app?.ui) return;` satırının **altına**: `call('user_delta', itemId, text);`
- `user_transcript` gövdesinde `if (!text || !app?.ui) return;` satırının **altına**: `call('user_final', itemId || null, text);`
- `tool_call` gövdesinin ilk satırı: `call('tool_call', name, TOOL_LABELS[name] || name);`
- `tool_result: ({ name }) => completeToolChip(name),` → `tool_result: ({ name }) => { call('tool_result', name); completeToolChip(name); },`
- `assistant_text_delta` gövdesinde `if (!text) return;` satırının altına: `call('assistant_delta', text);`
- `assistant_text` gövdesini şununla değiştir:

```js
                    assistant_text: ({ text }) => {
                        if (text && !assistantText) call('assistant_delta', text);
                        if (text && !assistantText && assistantBubble && app?.ui?.appendResponseChunk) {
                            try { app.ui.appendResponseChunk(assistantBubble, text); } catch { }
                        }
                    },
```

- `response_done` gövdesinin ilk satırı: `call('response_done');`
- `conversation_ended` gövdesinde `setStatus('idle', label); setTimeout(() => setStatus('idle'), 3000);` iki satırını şununla değiştir:

```js
                        call('ended', label);
                        playTone('end');
                        setStatus('idle');
                        closeSoon(1500);
```

- `error` geri çağrısını şununla değiştir (başlatma hatasını `launchClient` gösterir — burada tekrar kapanış zamanlanmaz; Review Focus #3):

```js
                    error: ({ message }) => {
                        console.warn('RealtimeNative error:', message);
                        removePendingUserBubble();
                        settleAllUserBubbles();
                        finalizeAssistantBubble();
                        if (!connectedOnce) return;
                        if (client !== me) return;
                        call('error', '⚠ ' + (message || 'Bağlantı koptu.'));
                        setStatus('idle');
                        client = null;
                        closeSoon(2000);
                    },
```

- `close` geri çağrısını şununla değiştir:

```js
                    close: () => {
                        removePendingUserBubble();
                        settleAllUserBubbles();
                        finalizeAssistantBubble();
                        if (client !== me) return;   // kullanıcı bitirdi ya da yeni görüşme başladı
                        setStatus('idle');
                        // Bağlantı beklenmedik kapandıysa (bitir/hata/sonlandırma dışında) ekranı kapat.
                        if (overlayOpen && !closeTimer) { playTone('end'); closeOverlay(); }
                        client = null;
                    }
```

> `closeSoon` ile zamanlanmış bir kapanış varsa (`closeTimer` dolu) `close` ekranı ikinci kez kapatmaz.

- [ ] **Step 4: realtime-ui.js — başlatma hatası, durdurma, kontroller, klavye, sesler**

`launchClient`'i şununla değiştir (izin reddi ekranı açık bırakır; "Tekrar dene"/"Kapat" seçilir):

```js
        const launchClient = async (c) => {
            try {
                await c.start();
            } catch (err) {
                client = null;
                const msg = (err?.name === 'NotAllowedError')
                    ? 'Mikrofon izni reddedildi. Tarayıcı ayarlarından izin verip tekrar deneyin.'
                    : (err?.message || 'Sesli konuşma başlatılamadı.');
                call('error', '⚠ ' + msg);
                setStatus('idle');
            }
        };
```

`stop`'u şununla değiştir:

```js
        const stop = () => {
            const wasActive = client !== null;
            if (client) {
                client.stop();
                client = null;
            }
            setStatus('idle');
            if (wasActive) playTone('end');
            closeOverlay();
        };
```

`window.__stopVoice = stop;` satırının altına ekle:

```js
        // VoiceCallOverlay düğmeleri (Chat.razor → JS). İstemci tek doğruluk kaynağı: sessiz durumu
        // istemciden 'muted' olayıyla geri döner.
        window.__voiceCall = {
            toggleMute: () => { if (client) client.setMuted(!client.muted); },
            end: () => stop(),
            interrupt: () => { if (client) client.interrupt(); },
            retry: () => { if (client) return; startVoice(); }
        };

        // Klavye: ekran açıkken Space sessize al, Esc bitir. Space'in varsayılanı (odaktaki düğmeyi
        // tıklama) hem keydown hem keyup'ta engellenir — aksi hâlde odak bir düğmedeyken iki kez
        // tetiklenirdi (Review Focus #4).
        const isTyping = (t) => t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
        document.addEventListener('keydown', (e) => {
            if (!overlayOpen || isTyping(e.target)) return;
            if (e.key === 'Escape') { e.preventDefault(); stop(); }
            else if (e.key === ' ' || e.code === 'Space') {
                e.preventDefault();
                if (!e.repeat) window.__voiceCall.toggleMute();
            }
        });
        document.addEventListener('keyup', (e) => {
            if (overlayOpen && (e.key === ' ' || e.code === 'Space') && !isTyping(e.target)) e.preventDefault();
        });
```

Eski `interruptBtn` bloğunu (`if (interruptBtn) { … }`) ve `init` sonundaki `setStatus('idle');` çağrısını sil
(ilk açılışta ekran olayı gönderilmemeli). `voiceBtn` tıklama işleyicisi aynen kalır.

`TOOL_LABELS` sabitinin altına (IIFE içinde, `init` dışında) ekle:

```js
    // Kısa görüşme sesleri — dosya yok, Web Audio ile üretilir. Kısık seviye; tarayıcı engellerse sessizce geçer.
    let toneCtx = null;
    function playTone(kind) {
        try {
            toneCtx = toneCtx || new (window.AudioContext || window.webkitAudioContext)();
            const notes = kind === 'connect' ? [660, 880] : [660, 440];
            const t0 = toneCtx.currentTime;
            notes.forEach((freq, i) => {
                const osc = toneCtx.createOscillator();
                const gain = toneCtx.createGain();
                osc.type = 'sine';
                osc.frequency.value = freq;
                const start = t0 + i * 0.12;
                gain.gain.setValueAtTime(0.0001, start);
                gain.gain.exponentialRampToValueAtTime(0.06, start + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.11);
                osc.connect(gain).connect(toneCtx.destination);
                osc.start(start);
                osc.stop(start + 0.12);
            });
        } catch { /* ses çalınamazsa görüşme etkilenmez */ }
    }
```

- [ ] **Step 5: Derle**

Run: `dotnet build src/CustomerSupportBot.Web -v q -nologo`
Expected: `Build succeeded.`

- [ ] **Step 6: Tarayıcıda uçtan uca doğrula (sahte istemci)**

Web'i başlat; `/customer-login`'e git, sahte müşteri token'ı yaz ve `/`'a dön:

```js
async () => {
  const b64 = o => btoa(JSON.stringify(o)).replace(/=+$/,'').replace(/\+/g,'-').replace(/\//g,'_');
  const jwt = b64({alg:'HS256',typ:'JWT'}) + '.' + b64({sub:'u', exp: Math.floor(Date.now()/1000)+3600}) + '.sig';
  localStorage.setItem('cs.auth.customer', JSON.stringify({accessToken: jwt, refreshToken: 'rt', username: 'u@example.com', role: 'Customer'}));
  location.href = '/';
  return 'ok';
}
```

Sonra sahte `RealtimeClient` kur (gerçek mikrofon/ağ yok):

```js
async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  for (let i = 0; i < 60 && !document.getElementById('voiceBtn'); i++) await sleep(200);
  window.EventSource = function () { this.close = () => {}; this.addEventListener = () => {}; };
  window.__fake = { instances: 0, failNext: false };
  window.RealtimeClient = class {
    constructor({ callbacks }) { this.cb = callbacks; this.state = 'idle'; this._muted = false; window.__fake.c = this; window.__fake.instances++; }
    get muted() { return this._muted; }
    setMuted(m) { this._muted = !!m; this.cb.muted?.({ muted: this._muted }); }
    getLevels() { return { input: 0.5, output: 0.7 }; }
    async start() {
      if (window.__fake.failNext) { window.__fake.failNext = false; this.cb.error?.({ message: 'denied' }); const e = new Error('denied'); e.name = 'NotAllowedError'; throw e; }
      this.state = 'listening'; this.cb.state?.({ state: 'connecting' });
      this.cb.connected?.({ sessionId: 'sess-voice-1', tools: [] }); this.cb.state?.({ state: 'listening' });
    }
    interrupt() { window.__fake.interrupted = true; this.cb.state?.({ state: 'listening' }); }
    stop() { window.__fake.stopped = true; }
    emit(n, d) { this.cb[n]?.(d); }
  };
  return 'fake installed';
}
```

Doğrulama dizisi (her adımdan sonra `document.querySelector('.vc-state')?.textContent` ve `#voiceOrb` sınıfını oku; gerekirse ekran görüntüsü al):

```js
async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const st = () => ({ label: document.querySelector('.vc-state')?.textContent?.trim(),
                      orb: document.getElementById('voiceOrb')?.className,
                      chip: document.querySelector('.vc-chip')?.textContent,
                      user: document.querySelector('.vc-cap-user')?.textContent,
                      bot: document.querySelector('.vc-cap-bot')?.textContent,
                      open: !!document.querySelector('.vc-overlay') });
  const r = {};
  document.getElementById('voiceBtn').click(); await sleep(400);
  r.listening = st(); r.orbLoop = window.voiceOrb.running;
  const c = window.__fake.c;
  c.emit('speech_started', { itemId: 'i1' });
  c.emit('user_transcript_delta', { itemId: 'i1', text: 'Siparişim ' });
  c.emit('user_transcript_delta', { itemId: 'i1', text: 'nerede' }); await sleep(200);
  r.liveCaption = st();
  c.emit('speech_stopped', { itemId: 'i1' }); await sleep(150);
  r.thinking = st();
  c.emit('tool_call', { name: 'order_status_tool' }); await sleep(150);
  r.tool = st();
  c.emit('tool_result', { name: 'order_status_tool' });
  c.emit('state', { state: 'speaking' });
  c.emit('assistant_text_delta', { text: 'Siparişiniz yarın teslim edilecek.' }); await sleep(200);
  r.speaking = st();
  document.getElementById('voiceOrb').click(); await sleep(150);
  r.interrupted = window.__fake.interrupted;
  c.emit('response_done', {}); c.emit('user_transcript', { itemId: 'i1', text: 'Siparişim nerede?' }); await sleep(200);
  r.finalCaption = st();
  document.querySelector('.vc-btn--end').focus();
  document.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', code: 'Space', bubbles: true }));
  document.dispatchEvent(new KeyboardEvent('keyup', { key: ' ', code: 'Space', bubbles: true })); await sleep(200);
  r.mutedOnce = { muted: c.muted, state: st() };
  document.querySelector('.vc-btn--cc').click(); await sleep(150);
  r.captionsOff = { caps: !!document.querySelector('.vc-captions'), stored: localStorage.getItem('csb-voice-captions') };
  document.querySelector('.vc-btn--cc').click(); await sleep(100);
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })); await sleep(300);
  r.afterEsc = { open: st().open, stopped: window.__fake.stopped, orbLoop: window.voiceOrb.running };
  return r;
}
```

Expected:
- `listening`: `open: true`, label "Dinliyor", orb `…vc-orb--listening`, `orbLoop: true`.
- `liveCaption.user`: "Sen: Siparişim nerede".
- `thinking.label`: "Düşünüyor"; `tool.chip`: "🔎 Sipariş durumu sorgulanıyor".
- `speaking.orb` içerir `vc-orb--speaking`, `speaking.bot`: "Siparişiniz yarın teslim edilecek."; `interrupted: true`.
- `finalCaption.user`: "Sen: Siparişim nerede?".
- `mutedOnce.muted: true` (tek bir Space → bir kez), label "Mikrofon kapalı".
- `captionsOff`: `{ caps: false, stored: "0" }`.
- `afterEsc`: `{ open: false, stopped: true, orbLoop: false }`.

Ek kontroller (Review Focus #3 ve #5):

```js
async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const r = {};
  window.__fake.failNext = true; window.__fake.instances = 0;
  document.getElementById('voiceBtn').click(); await sleep(400);
  r.denied = { alert: document.querySelector('.vc-error')?.textContent?.trim() };
  await sleep(2500);
  r.stillOpenAfter2_5s = !!document.querySelector('.vc-overlay');
  [...document.querySelectorAll('.vc-text-btn')].find(b => b.textContent.includes('Tekrar')).click(); await sleep(400);
  r.retry = { instances: window.__fake.instances, label: document.querySelector('.vc-state')?.textContent?.trim() };
  window.__stopVoice(); await sleep(300);
  r.humanJoined = { open: !!document.querySelector('.vc-overlay'), orbLoop: window.voiceOrb.running };
  return r;
}
```

Expected: `denied.alert` "Mikrofon izni reddedildi" içerir; `stillOpenAfter2_5s: true`; `retry.instances: 2`,
`retry.label: "Dinliyor"`; `humanJoined: { open: false, orbLoop: false }`.

Son olarak telefon genişliğinde (`browser_resize` 390×844) ekranı açıp ekran görüntüsü al; düğmeler ve altyazı taşmamalı.
Hareket azaltma için `browser_run_code_unsafe` ile `await page.emulateMedia({ reducedMotion: 'reduce' })` sonrası ekranı aç:
`window.voiceOrb.running` `false` olmalı.

- [ ] **Step 7: Commit**

```bash
git add src/CustomerSupportBot.Web/wwwroot/js/realtime-ui.js src/CustomerSupportBot.Web/wwwroot/js/chat-bridge.js
git commit -m "feat(web): drive the voice call screen from the realtime UI" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Belgeler ve tam test

**Files:**
- Modify: `docs/CustomerSupportBot.Web/Pages/Chat.md`
- Modify: `README.md` (Sesli Konuşma satırı)

- [ ] **Step 1: Chat.md**

"### Tek sesli buton" başlığının altındaki metnin sonuna şu bölümü ekle:

```markdown
### Sesli görüşme ekranı

🎙'ye basınca tam sayfa, koyu bir görüşme ekranı (`Components/VoiceCallOverlay.razor`) açılır:
üstte süre ve "Yapay zekâ ile konuşuyorsunuz", ortada sesle canlanan küre ve durum etiketi, altta
son turun altyazısı, en altta Sessize al · Bitir · Altyazı. `Space` sessize alır, `Esc` bitirir;
asistan konuşurken küreye dokunmak sözünü keser.

- **Durum** saf bir modelde tutulur (`Models/VoiceCallModel.cs`, birim testli); `realtime-ui.js` her
  olayı `VoiceCallEvent(type, a, b)` ile iletir. Küre önceliği: hata > temsilciye aktarım > konuşuyor >
  sessiz > bağlanıyor > düşünüyor > dinliyor.
- **Küre** gerçek ses seviyesine tepki verir: `realtime-client.js`'in `AnalyserNode`'ları
  (`getLevels()`), `voice-orb.js` her karede `--level`'ı yazar — Blazor yeniden çizilmez. Hareket
  azaltma tercihinde döngü başlamaz.
- **Sessize alma** istemcide kalıcıdır (`setMuted`): asistan konuşmayı bitirip dinlemeye dönüldüğünde
  de mikrofon kapalı kalır.
- **Altyazı** yalnızca son tur; dökümün tamamı sohbete balon olarak yazılmaya devam eder. Tercih
  `localStorage["csb-voice-captions"]`.
- **Sesle söz kesme yok:** asistan konuşurken istemci yankıyı önlemek için mikrofonu kaynağında kapatır;
  söz kesme küreye dokunarak yapılır.
- Mikrofon izni reddinde ekran açık kalır ("Tekrar dene" / "Kapat"); bağlantı koparsa kısa hata ve 2 sn
  sonra kapanış; temsilci katılınca (`__stopVoice`) ekran kapanır.
```

Aynı dosyada mesaj kutusunun altındaki eski durum satırından (`#voiceStatus`, "⏸") söz eden cümle varsa
kaldır/güncelle (Run: `grep -n "voiceStatus\|⏸\|durum satırı" docs/CustomerSupportBot.Web/Pages/Chat.md`).

- [ ] **Step 2: README**

`README.md`'deki "**Sesli Konuşma (Realtime)**" maddesinin sonuna (satır sonundaki nokta/parantezden sonra) ekle:

```
 Arayüz tam sayfa bir görüşme ekranıdır: sesle canlanan küre, son turun canlı altyazısı, sessize al / bitir / altyazı düğmeleri; küreye dokunarak asistanın sözü kesilir.
```

- [ ] **Step 3: Tam derleme ve tüm testler**

Docker açık olmalı (`docker info`). Run:

```bash
dotnet build CustomerSupport.slnx -v q -nologo 2>&1 | grep -E " error |warning CS|Build succeeded" | sort -u
dotnet test --solution CustomerSupport.slnx --no-build 2>&1 | grep -E "^failed |total:|failed:"
```

Expected: `Build succeeded.`, `failed: 0`.

- [ ] **Step 4: Commit**

```bash
git add docs/CustomerSupportBot.Web/Pages/Chat.md README.md
git commit -m "docs: voice call screen" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
