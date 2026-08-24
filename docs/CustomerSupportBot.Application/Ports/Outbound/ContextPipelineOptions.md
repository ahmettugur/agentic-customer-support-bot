# ContextPipelineOptions

**Kaynak:** `Ports/Outbound/ContextPipelineOptions.cs`
**Ayar bölümü:** `appsettings.json` → `"ContextPipeline"`

## 1. Ne İşe Yarar

[`IContextPipeline`](IContextPipeline.md)'ın sınırlarını (provider timeout'u, karakter
bütçeleri) taşır.

## 2. Hangi Amaçla Kullanılır

`ContextPipeline` (Application/Services/Chat) her turda bu ayarları okuyarak provider'ları
zaman/karakter sınırı içinde çalıştırır.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca yapılandırma değerlerini taşımak.
- **Üstlenmediği:** Sınırların uygulanması — bu `ContextPipeline`'ın işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Ayrıntılı davranış için bkz. [ContextPipeline.md](../../Chat/ContextPipeline.md) — bu options
sınıfı orada anlatılan dört mekanizmadan ikisinin (timeout, bütçe) sayısal parametrelerini
sağlar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Temel ilke: **"Bağlam bir iyileştirmedir, zorunluluk değildir."** Üretimi turu süresiz
bekletmemeli ve prompt'u sınırsız şişirmemelidir. `ProviderTimeoutSeconds` olmadan, pipeline
içindeki ağ/LLM çağrıları (embedding araması, özet üretimi) yavaş bir provider'ı süresiz
bekletebilirdi — bağlam kurulumu workflow'dan ÖNCE çalıştığı için `WorkflowGuardOptions`
koruması henüz devrede değildir.

## 6. Metotlar / Üyeler

| Üye | Varsayılan | Açıklama |
|---|---|---|
| `int ProviderTimeoutSeconds` | `5` | Tek bir provider'a tanınan süre; aşılırsa atlanır. |
| `int MaxProviderChars` | `4000` | Tek bir provider'ın katkısı için üst sınır. |
| `int MaxTotalChars` | `12000` | Toplam bağlamın üst sınırı; düşük öncelikliler dışarıda bırakılır. |

`MaxTotalChars < MaxProviderChars` ise uygulama `ValidateOnStart` ile başlamayı reddeder.

## 7. Bağımlılıklar

Yok — saf options sınıfı.
