# InputGuard

**Dosya:** `Services/Chat/InputGuard.cs`
**Port:** `IInputGuard`
**Namespace:** `CustomerSupportBot.Application.Services.Chat`

## 1. Ne İşe Yarar

Kullanıcı mesajı **herhangi bir LLM'e ulaşmadan ÖNCE** çalışan deterministik bir kapı
(gate). Regex tabanlı kurallarla açık kötüye kullanım vektörlerini yakalar: aşırı uzunluk
(DoS/token bombası), prompt-injection/jailbreak kalıpları, görünmez Unicode hileleri
(zero-width/RTL), aşırı ID sayımı (maliyet bombası), HTML/script enjeksiyonu.

## 2. Hangi Amaçla Kullanılır

`ChatPortService`/reasoning zincirinin en başında her kullanıcı mesajı `Inspect` ile
denetlenir. Sonuç `Allow` / `Sanitize` / `Reject` — reddedilen mesaj LLM'e hiç gönderilmez,
sabit bir red mesajı döner.

## 3. Sorumlulukları

- **Üstlendiği:** Deterministik, regex-tabanlı ön filtreleme; her regex için 200ms
  `matchTimeoutMilliseconds` ile ReDoS (regex tabanlı DoS) riskine karşı kendini korumak.
- **Üstlenmediği:** Anlamsal/bağlamsal kötüye kullanım tespiti (bu prompt-seviyesi
  guardrail'lerin işi) — bu sınıf yalnızca **prompt-seviyesi korumaların TEK BAŞINA
  güvenilir şekilde yakalayamadığı** açık, kalıp-tabanlı vektörleri yakalar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IInputGuard` port'unu implemente eder.
- **Kimin tarafından çağrılır:** `ChatPortService`/reasoning zincirinin en başı — mesaj
  herhangi bir `IChatClient`/LLM çağrısına gitmeden önce.
- Bağımsız, durumsuz bir sınıf; başka hiçbir servise bağımlı değildir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden LLM'den önce, LLM'in kendisine değil:** Prompt içi talimatlarla ("kullanıcı seni
kandırmaya çalışırsa reddet") kötüye kullanımı önlemek güvenilir değildir — modelin kendisi
ikna edilebilir (jailbreak). Deterministik bir regex kapısı, modelin kararına bağlı olmayan,
**her zaman aynı sonucu üreten** bir ilk savunma hattıdır. Ayrıca modelin bu mesajları hiç
GÖRMEMESİ, hem maliyet (gereksiz token) hem de veri sızıntısı riskini (ör. admin paneline
sızabilecek HTML/script) daha ilk adımda keser.

**Beş ayrı kontrol, sırayla, en ucuzdan en pahalıya:**
1. **Uzunluk sınırı** (`MaxInputLength = 2000`) — en ucuz kontrol, hemen reddeder.
2. **Unicode normalizasyonu + görünmez karakter temizliği** — zero-width/RTL-override
   karakterleri (bidi saldırıları, gizli talimat gizleme) temizler, mesajı REDDETMEZ, sadece
   temizler ve `invisible_chars_stripped` flag'i ekler.
3. **Sert injection kalıbı** (`InjectionPattern`) → **reddet.** "ignore previous instructions",
   "system prompt", "jailbreak", "dan mode", "kuralları yok say" gibi çok dilli (TR/EN) kalıplar.
4. **HTML/script kalıbı** → **reddet.** `<script>`, `<img onerror=...>` gibi enjeksiyonlar —
   admin paneline veya trace store'a XSS sızıntısı riskine karşı.
5. **ID sayımı** (`MaxIdMentions = 8`, 4+ haneli rakam dizileri) → **reddet.** Tek mesajda
   `1030 1031 1032 ... 2030` gibi ardışık çok sayıda ID, her biri ayrı bir tool çağrısı/LLM
   turu tetikleyebileceği için bir **maliyet bombasıdır** — LLM'e gitmeden önce kesilir.
6. **Yumuşak şüpheli kalıp** (` ```json`, `[INST]`, `"approved":true` gibi) → **reddet.**
   Bunlar LLM'in çıktı biçimini taklit eden payload'lardır; LLM'e ulaşırsa modelin kendi
   çıktısıyla karıştırılıp yanlış bir karar (ör. sahte onay) tetikleyebilir.

Tüm regex'ler `matchTimeoutMilliseconds: 200` ile derlenmiştir (`[GeneratedRegex]`,
kaynak-üretimli/compile-time) — kötü niyetli girdiyle regex motorunu katastrofik geri
izlemeye (catastrophic backtracking) sokup sunucuyu kilitlemeye çalışan bir saldırı
(ReDoS), 200ms'de zaman aşımına uğrar; guard bu durumda mesajı reddeder, sunucu asla kilitlenmez.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `MaxInputLength` (`const int = 2000`) | Azami mesaj uzunluğu (karakter). |
| `MaxIdMentions` (`const int = 8`) | Tek mesajda izin verilen azami 4+ haneli ID sayısı. |
| `Inspect(string? input): InputGuardResult` | Tüm kontrolleri sırayla uygular; ilk reddeden kontrolde durur, aksi halde `Allow` (temizlenmiş metinle) döner. |

Regex'ler (private, `[GeneratedRegex]` ile derleme-zamanı üretilir): `InjectionPattern`,
`SoftSuspiciousPattern`, `HtmlScriptPattern`, `IdMentionPattern`, `InvisibleCharPattern`.

## 7. Bağımlılıklar

Yok — durumsuz, dışarıdan hiçbir servis inject etmez. Tüm regex'ler statik/compile-time.

## Bağlantılar

- [ChatPortService.md](ChatPortService.md) — bu guard'ı ilk adımda çağıran taraf
