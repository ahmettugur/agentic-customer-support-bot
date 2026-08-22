# AiProviderOptions.cs — AI Sağlayıcı Seçenek Modelleri

**Dosya:** `Options/AiProviderOptions.cs`
**Namespace:** `CustomerSupportBot.Adapters.AI`
**İçerdiği tipler:** `AiProvider` (enum), `AiOptions`, `RealtimeOptions`, `OpenAiOptions`, `AzureOpenAiOptions`

> Bu beş tip tek dosyada ve tek dokümanda tutulur çünkü hepsi tek bir yapılandırma
> ağacının (`appsettings.json` → `"AI"`) parçalarıdır ve birbirinden ayrı anlamları yoktur —
> `AiOptions` kök, diğerleri onun alt-nesneleridir. Ayırmak aynı bilgiyi 5 dosyaya bölüp
> okumayı zorlaştırırdı.

## 1. Ne İşe Yarar

`appsettings.json` içindeki `"AI"` bölümünü, .NET Options pattern'iyle (`IOptions<AiOptions>`)
strongly-typed C# nesnelerine bağlayan POCO (plain data) sınıflarıdır. Hiçbir davranış/iş
mantığı içermezler — sadece veri taşırlar.

## 2. Hangi Amaçla Kullanılır

- Aktif yapay zeka sağlayıcısını seçmek (`AiProvider`: `OpenAI` veya `AzureOpenAI`) —
  bu seçime göre [`AiClientFactory`](../Chat/AiClientFactory.md) hangi `IChatClient`'ın
  kurulacağına karar verir.
- OpenAI/Azure OpenAI kimlik bilgilerini ve model adlarını taşımak.
- Realtime (sesli) oturum ayarlarını taşımak — model, ses tonu, VAD süresi,
  transkripsiyon modeli/dili/prompt'u.

## 3. Sorumlulukları

- **Yapar:** appsettings değerlerini tipli property'lere bağlar, varsayılan değer sağlar.
- **Yapmaz:** Doğrulama yapmaz (ör. `ApiKey` boşsa burada patlamaz — kullanan adaptör
  patlar), sağlayıcı seçimine göre nesne oluşturmaz (bu iş `AiClientFactory`'de).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `Program.cs`/DI kurulumunda `services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName))` ile bağlanır.
- [`AiClientFactory`](../Chat/AiClientFactory.md) — `AiOptions.Provider`'a bakarak OpenAI ya da Azure client'ı kurar.
- [`OpenAiRealtimeClientAdapter`](../Realtime/OpenAiRealtimeClientAdapter.md) — `RealtimeOptions`'ı okur (model, ses, VAD, transkripsiyon).
- [`OpenAiEmbeddingAdapter`](../OpenAi/OpenAiEmbeddingAdapter.md), [`ReasoningChatClient`](../Chat/ReasoningChatClient.md) — `OpenAiOptions`/`AzureOpenAiOptions`'taki model/reasoning-effort alanlarını okur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Options pattern kullanılmasının nedeni, yapılandırmanın Domain katmanına hiç sızmaması
— bu sınıflar bilerek `Adapters.AI` katmanında, Domain'in bağımlı olmadığı bir yerdedir.
Sağlayıcı bazlı ayrım (`OpenAiOptions` vs `AzureOpenAiOptions`) tek bir "yapay zeka
sağlayıcısı" soyutlamasının arkasında iki farklı kimlik doğrulama/adresleme modelini
(API key vs Endpoint+Deployment) barındırabilmek için ayrı sınıflara bölünmüştür.

## 6. Tipler ve Üyeler

### `AiProvider` (enum)
| Değer | Anlamı |
|---|---|
| `OpenAI` | Genel OpenAI API'si (api.openai.com) |
| `AzureOpenAI` | Kurumsal Azure OpenAI Service dağıtımı |

### `AiOptions` (kök sınıf, section: `"AI"`)
| Üye | Tip | Açıklama |
|---|---|---|
| `SectionName` | `const string` | `"AI"` — appsettings.json'daki bölüm adı |
| `Provider` | `AiProvider` | Aktif sağlayıcı |
| `OpenAI` | `OpenAiOptions` | OpenAI ayarları |
| `AzureOpenAI` | `AzureOpenAiOptions` | Azure OpenAI ayarları |
| `Realtime` | `RealtimeOptions` | Gerçek zamanlı ses ayarları |

### `RealtimeOptions`
| Üye | Tip | Varsayılan | Açıklama |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Sesli özellik açık mı |
| `Model` | `string` | `"gpt-realtime-2"` | `wss://api.openai.com/v1/realtime?model={Model}` için model adı |
| `ApiKey` | `string?` | `null` | Boşsa `OpenAiOptions.ApiKey`'e düşer (fallback) |
| `Voice` | `string` | — (zorunlu) | Konuşma sesi (ör. `alloy`) |
| `VadSilenceMs` | `int` | `600` | VAD'in "konuşma bitti" sayması için gereken sessizlik süresi |
| `ReasoningEffort` | `string` | `"low"` | Realtime modelinin muhakeme eforu |
| `MaxResponseTokens` | `int` | `4096` | Model yanıtı için token tavanı |
| `TranscriptionModel` | `string` | `"gpt-4o-transcribe"` | ASR (konuşma→metin) modeli |
| `TranscriptionLanguage` | `string?` | `"tr"` | ASR dil ipucu |
| `TranscriptionPrompt` | `string` | aşağıya bakınız | ASR'ye verilen bağlam ipucu |

> 🐞 **`TranscriptionPrompt` neden somut örnek numara İÇERMİYOR:** Eskiden prompt
> "müşteri numarası (1008, 1027 gibi...)" gibi somut örnekler içeriyordu. ASR modeli
> sessizlik/gürültüde boş dönmek yerine prompt'un sözlüğünden bir cümle **uyduruyordu**;
> örnekler somut olduğunda bu halüsinasyon domain'e gerçek gibi görünen bir metne
> dönüşüyordu. Canlı bir oturumda kullanıcı hiçbir şey söylemeden sohbete *"Merhaba,
> müşteri numaram 1025."* düştü (prompt'taki 1008/1027'nin komşusu bir sayı) ve bu sahte
> metin gerçek bir agent turu başlattı. Düzeltme: alan/dil ipucu bırakıldı, somut
> tohumlayıcı örnekler prompt'tan tamamen çıkarıldı.

### `OpenAiOptions`
| Üye | Tip | Açıklama |
|---|---|---|
| `ApiKey` | `string?` | OpenAI API anahtarı |
| `Model` | `string?` | Standart sohbet modeli |
| `ReasoningModel` | `string?` | Muhakeme (reasoning) modeli — [`ReasoningChatClient`](../Chat/ReasoningChatClient.md) tarafından kullanılır |
| `ReasoningEffort` | `string?` | Muhakeme eforu (ör. `low`/`medium`/`high`) |

### `AzureOpenAiOptions`
| Üye | Tip | Açıklama |
|---|---|---|
| `Endpoint` | `string?` | Azure kaynak endpoint URL'i |
| `ApiKey` | `string?` | Azure API anahtarı |
| `Deployment` | `string?` | Standart sohbet deployment adı |
| `ReasoningDeployment` | `string?` | Muhakeme deployment adı |
| `ReasoningEffort` | `string?` | Muhakeme eforu |

## 7. Bağımlılıklar

Yok — bu dosya saf veri taşıyıcılarından oluşur, hiçbir servisi inject etmez.

## Bağlantılar

- [../Chat/AiClientFactory.md](../Chat/AiClientFactory.md) — `Provider`'a göre client kurar
- [../Realtime/OpenAiRealtimeClientAdapter.md](../Realtime/OpenAiRealtimeClientAdapter.md) — `RealtimeOptions` tüketicisi
- [../DependencyInjection/AiAdapterServiceCollectionExtensions.md](../DependencyInjection/AiAdapterServiceCollectionExtensions.md) — DI kaydı
