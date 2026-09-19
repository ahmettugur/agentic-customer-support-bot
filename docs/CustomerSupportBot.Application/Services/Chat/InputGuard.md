# InputGuard

**Dosya:** `Services/Chat/InputGuard.cs`
**Port:** `IInputGuard`
**Namespace:** `CustomerSupportBot.Application.Services.Chat`

## 1. Ne İşe Yarar

Kullanıcı mesajı **herhangi bir LLM'e ulaşmadan ÖNCE** çalışan deterministik bir kapı
(gate). Regex tabanlı kurallarla açık kötüye kullanım vektörlerini yakalar: aşırı uzunluk
(DoS/token bombası), prompt-injection/jailbreak kalıpları, görünmez Unicode hileleri
(zero-width/RTL), aşırı ID sayımı (maliyet bombası), HTML/script enjeksiyonu. Ayrıca mesaj
içinde geçen PII'yi (e-posta, telefon, TC kimlik no, kredi kartı) LLM'e gitmeden önce maskeler.

## 2. Hangi Amaçla Kullanılır

`ChatPortService`/reasoning zincirinin en başında her kullanıcı mesajı `Inspect` ile
denetlenir. Sonuç `Allow` / `Sanitize` / `Reject` — reddedilen mesaj LLM'e hiç gönderilmez,
sabit bir red mesajı döner.

## 3. Sorumlulukları

- **Üstlendiği:** Deterministik, regex-tabanlı ön filtreleme; her regex için 200ms
  `matchTimeoutMilliseconds` ile ReDoS (regex tabanlı DoS) riskine karşı kendini korumak.
  Mesaj içindeki PII'yi tespit edip [`PiiMasker`](../Logging/PiiMasker.md) ile maskelemek.
- **Üstlenmediği:** Anlamsal/bağlamsal kötüye kullanım tespiti (bu prompt-seviyesi
  guardrail'lerin işi) — bu sınıf yalnızca **prompt-seviyesi korumaların TEK BAŞINA
  güvenilir şekilde yakalayamadığı** açık, kalıp-tabanlı vektörleri yakalar. PII maskeleme
  algoritmasının kendisi de burada değil — `PiiMasker`'da; bu sınıf yalnızca metin içinde
  PII'yi **bulup** o yardımcıya devreder.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IInputGuard` port'unu implemente eder.
- **Kimin tarafından çağrılır:** `ChatPortService`/reasoning zincirinin en başı — mesaj
  herhangi bir `IChatClient`/LLM çağrısına gitmeden önce.
- [`PiiMasker`](../Logging/PiiMasker.md) — maskeleme algoritmasının kendisini kullanır (DI
  değil, statik metot çağrısı).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden LLM'den önce, LLM'in kendisine değil:** Prompt içi talimatlarla ("kullanıcı seni
kandırmaya çalışırsa reddet") kötüye kullanımı önlemek güvenilir değildir — modelin kendisi
ikna edilebilir (jailbreak). Deterministik bir regex kapısı, modelin kararına bağlı olmayan,
**her zaman aynı sonucu üreten** bir ilk savunma hattıdır. Ayrıca modelin bu mesajları hiç
GÖRMEMESİ, hem maliyet (gereksiz token) hem de veri sızıntısı riskini (ör. admin paneline
sızabilecek HTML/script) daha ilk adımda keser.

**Yedi ayrı kontrol, sırayla, en ucuzdan en pahalıya:**
1. **Uzunluk sınırı** (`MaxInputLength = 2000`) — en ucuz kontrol, hemen reddeder.
2. **Unicode normalizasyonu + görünmez karakter temizliği** — zero-width/RTL-override
   karakterleri (bidi saldırıları, gizli talimat gizleme) temizler, mesajı REDDETMEZ, sadece
   temizler ve `invisible_chars_stripped` flag'i ekler.
3. **PII maskeleme** (e-posta, telefon, TC kimlik no, kredi kartı) — mesajı REDDETMEZ, yalnızca
   maskeler ve `pii_masked:*` flag'i ekler. Bu adımdan sonraki HER adım (injection/HTML/ID
   sayımı dahil) maskelenmiş metin üzerinde çalışır — ve bu adımdan sonra üretilen
   `SanitizedInput` hem LLM'e giden hem `ReasoningTrace.UserQuery`'ye yazılan metindir. Detay
   için bkz. [`PiiMasker`](../Logging/PiiMasker.md).
4. **Sert injection kalıbı** (`InjectionPattern`) → **reddet.** "ignore previous instructions",
   "system prompt", "jailbreak", "dan mode", "kuralları yok say" gibi çok dilli (TR/EN) kalıplar.
5. **HTML/script kalıbı** → **reddet.** `<script>`, `<img onerror=...>` gibi enjeksiyonlar —
   admin paneline veya trace store'a XSS sızıntısı riskine karşı.
6. **ID sayımı** (`MaxIdMentions = 8`, 4+ haneli rakam dizileri) → **reddet.** Tek mesajda
   `1030 1031 1032 ... 2030` gibi ardışık çok sayıda ID, her biri ayrı bir tool çağrısı/LLM
   turu tetikleyebileceği için bir **maliyet bombasıdır** — LLM'e gitmeden önce kesilir.
7. **Yumuşak şüpheli kalıp** (` ```json`, `[INST]`, `"approved":true` gibi) → **reddet.**
   Bunlar LLM'in çıktı biçimini taklit eden payload'lardır; LLM'e ulaşırsa modelin kendi
   çıktısıyla karıştırılıp yanlış bir karar (ör. sahte onay) tetikleyebilir.

> 🔒 **Kredi kartı: boşluklu format YAKALANIR, ama Luhn + "ardışık ID" testiyle birlikte.**
> İlk tasarım boşluklu grupları ("4111 1111 1111 1111") bilerek kapsam dışı bırakıyordu, çünkü
> bu domain'de birden çok sipariş/şikayet ID'si de boşlukla ayrılarak yazılır
> (`"1030 1031 1032 1033"`) — ikisi **yapısal olarak birebir aynı** şekle sahip (4× 4-haneli
> grup, boşluklu); regex tek başına ayırt edemez. Ama boşluklu format kartların en yaygın yazım
> biçimi olduğu için tamamen dışarıda bırakmak çok fazla kayıp (false negative) demekti. Çözüm
> iki bağımsız filtrenin BİRLİKTE kullanılması:
> 1. **Luhn checksum** — gerçek kart numaraları bunu sağlar; rastgele/ardışık rakamlar ~%90
>    ihtimalle sağlamaz. TEK BAŞINA yeterli değil: bu domain'in mesajları çok sayıda 4-haneli
>    ID içerebildiği için (`too_many_ids` testi 10 ID ile çalışır), Luhn'un ~%10'luk hata payı
>    birikerek ardışık-ID mesajlarının **~%19-29'unda** yanlış pozitife yol açtı (ölçüldü).
> 2. **`LooksLikeSequentialIdList`** — dört grup sabit küçük bir farkla (1-3) artıyorsa
>    (`1030,1031,1032,1033` deseni) kart SAYILMAZ, Luhn'u geçse bile. Bu domain'in ID'leri her
>    zaman ardışık/yakın ardışık üretildiği için bu test pratikte neredeyse tüm gerçek ID
>    listelerini eler.
>
> İkisi birlikte, 5000 rastgele ardışık sipariş-ID mesajı simülasyonunda **%0** yanlış pozitif
> verdi (kalan risk: mesajda TESADÜFEN Luhn'u geçen, ardışık OLMAYAN 4 rastgele 4-haneli sayı —
> ~%10, ama bu domain'de gerçekçi bir kullanıcı mesajı şekli değil). Bitişik (`4111111111111111`)
> ve tire ile gruplu (`4111-1111-1111-1111`) formatlar da yalnızca Luhn ile kapılır — bu domain
> hiçbir zaman ayraçsız/tireli 13+ haneli metin üretmediği için ardışık-ID riski orada yok.

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
`SoftSuspiciousPattern`, `HtmlScriptPattern`, `IdMentionPattern`, `InvisibleCharPattern`,
`CreditCardGroupedPattern` (boşluk/tire gruplu, backreference'lı), `CreditCardContiguousPattern`
(13-19 ayraçsız hane), `PhonePattern`, `TcknPattern`, `EmailPattern`.

| Üye | Açıklama |
|---|---|
| `MaskPii(string): (string Text, List<string> Flags)` (private) | 4 PII deseni sırayla (kredi kartı → telefon → TC kimlik no → e-posta) tespit edip maskeler; her tespitte `pii_masked:{tür}` flag'i ekler (maskelenmiş DEĞERİ değil, yalnızca türünü). |
| `MaskCreditCards(string): (string Text, bool Masked)` (private) | Gruplu + bitişik kart adaylarını Luhn ve (yalnızca gruplu adaylar için) `LooksLikeSequentialIdList` ile filtreleyip `PiiMasker.MaskCreditCard` ile maskeler. |
| `LooksLikeSequentialIdList(string, string, string, string): bool` (private) | 4 grubu int'e çevirir; ardışık farklar eşit ve 1-3 arasındaysa `true` (kart değil, ID listesi). |
| `PassesLuhn(string): bool` (private) | Standart Luhn mod-10 checksum. |

## 7. Bağımlılıklar

[`PiiMasker`](../Logging/PiiMasker.md) (statik çağrı, DI değil). Bunun dışında yok — durumsuz,
dışarıdan başka hiçbir servis inject etmez. Tüm regex'ler statik/compile-time.

## Bağlantılar

- [ChatPortService.md](ChatPortService.md) — bu guard'ı ilk adımda çağıran taraf
- [PiiMasker.md](../Logging/PiiMasker.md) — PII maskeleme algoritmasının kendisi
