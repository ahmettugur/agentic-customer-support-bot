# InputGuard

**Dosya:** `Services/InputGuard.cs`  
**Implements:** `IInputGuard`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Kullanıcıdan gelen mesajı herhangi bir LLM'e iletmeden önce deterministik olarak filtreler. Uzunluk DoS, prompt injection, jailbreak girişimleri, HTML/script injection ve token bomb (ID enumeration) saldırılarını engeller.

---

## `Inspect`

```csharp
InputGuardResult Inspect(string? input)
```

**Döndürülen sonuç:**
```csharp
public record InputGuardResult(
    InputGuardVerdict Verdict,   // Allow / Sanitize / Reject
    string SanitizedInput,       // Unicode normalize + görünmez char temizlenmiş
    List<string> Flags,          // Tetiklenen kural etiketleri
    string? UserMessage          // Reject durumunda gösterilecek mesaj
);
```

---

## Kontrol sırası

### 1. Boş mesaj → Reject

```
flags: ["empty_input"]
msg: "Mesaj boş olamaz."
```

---

### 2. Uzunluk sınırı → Reject

```
MaxInputLength = 2000 karakter
flags: ["length_exceeded"]
msg: "Mesajınız çok uzun (en fazla 2000 karakter). Lütfen kısaltın."
```

---

### 3. Unicode normalizasyonu + görünmez karakter temizleme

Unicode `NormalizationForm.FormKC` uygulanır. Ardından şu aralıklardaki karakterler silinir:
```
U+200B–U+200F  (zero-width spaces)
U+202A–U+202E  (LR/RL override, BiDi)
U+2060–U+206F  (word joiner, invisible separator)
U+FEFF         (BOM)
```

Temizleme yapılırsa `flags: ["invisible_chars_stripped"]` eklenir. Bu kontrol hard reject değil; mesaj temizlenmiş hâliyle devam eder.

---

### 4. Prompt injection / jailbreak → Reject

Regex tabanlı, İngilizce + Türkçe pattern'ler:
- `ignore all previous instructions`
- `önceki talimatlarını yok say / unut`
- `system prompt / sistem prompt / reveal your`
- `you are now / sen artık / act as admin/developer/root`
- `jailbreak / DAN mode / developer mode / admin mode / root access`
- `bypass rules/filter/safety / disable safety/filter`
- `kurallarını değiştir / kaydı sil / başkasının verisini`

```
flags: ["injection_pattern:<eşleşen metin>"]
msg: "Bu konuda yardımcı olamam. Sipariş, ürün veya şikayet konularında destek olabilirim."
```

---

### 5. HTML/script injection → Reject

XSS saldırılarını ve admin paneline zararlı içerik sızmasını önler:
```html
<script> <iframe> <img> <svg> <object> <embed> <link> <meta> <style>
javascript:
on[eventname]=
```

```
flags: ["html_or_script_tag"]
msg: "Mesajınızda izin verilmeyen içerik tespit edildi."
```

---

### 6. ID enumeration (token bomb) → Reject

```
MaxIdMentions = 8
Pattern: \b(ORD|CMP|CUST)-\d+\b
```

Tek mesajda 8'den fazla ID (1030 / 1001 / 1027) varsa:
```
flags: ["too_many_ids:<sayı>"]
msg: "Tek mesajda en fazla 8 sipariş/şikayet numarası işleyebilirim."
```

---

### 7. Yumuşak JSON/payload sinyali → Reject

LLM payload sahteciliği riski — LLM'e giderse yanlış karar tetiklenebilir:
```
```json
[INST] [/INST]
<system> </system>
"approved": true
"rejected": false
```

```
flags: ["soft_suspicious:<eşleşen metin>"]
msg: "Mesajınızda izin verilmeyen içerik tespit edildi."
```

---

## Regex performansı

Tüm regex'ler compile-time `[GeneratedRegex]` attribute'u ile oluşturulmuştur. Her regex için `matchTimeoutMilliseconds: 200` koruma süresi tanımlıdır — ReDoS saldırılarına karşı önlem.

---

## ChatPortService entegrasyonu

```csharp
// ChatPortService.HandleAsync içinde:
var guardResult = _inputGuard.Inspect(request.Message);
if (guardResult.Verdict == InputGuardVerdict.Reject)
{
    yield return new StreamEvent.ResponseDelta(guardResult.UserMessage!);
    yield break;
}
var sanitizedInput = guardResult.SanitizedInput;
// pipeline devam eder sanitizedInput ile
```

---

## Yeni kural eklemek

1. `InputGuard.cs`'te `[GeneratedRegex]` attribute'lu yeni private partial Regex metodu ekleyin
2. `Inspect` metoduna `if (newPattern.IsMatch(normalized))` kontrolü ekleyin
3. Flag ve kullanıcı mesajı belirleyin
4. `matchTimeoutMilliseconds` ayarlamayı unutmayın
