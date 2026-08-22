# CustomerIdentityHintBuilder

- **Kaynak:** `Services/Providers/CustomerIdentityHintBuilder.cs`
- **Tür:** `public sealed class` (`IContextProvider` implemente **etmez** — bkz. madde 4)
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## 1. Ne İşe Yarar

Giriş yapmış müşterinin adı + bugünün tarihinden oluşan tek bir Türkçe sistem mesajı üretir.
İsim `SessionState.AuthenticatedCustomerId` (JWT'den doğrulanmış) üzerinden `ICustomerRepository`
ile çözülür.

## 2. Hangi Amaçla Kullanılır

Aynı cümle **iki farklı kanalda** birebir gerekir: yazılı workflow (`WorkflowMessageBuilder`,
mesaj listesinin başına system mesajı olarak) ve sesli native mod (`RealtimeNativeService` →
`ConfigureNativeSessionAsync` instructions'ına eklenerek). Bu sınıf o metni **tek bir yerde**
üretir ki iki kanal birbirinden sapmasın.

## 3. Sorumlulukları

**Üstlendiği:** Kimliği doğrulanmış müşterinin tam adını çözmek, bugünün tarihini `tr-TR`
kültüründe biçimlendirmek, ikisini tek bir sistem mesajına birleştirmek.

**Üstlenmediği:** `SessionState.CustomerId`'yi (LLM'in serbest metinden çıkardığı, güvenilmeyen
alan) **hiç kullanmaz** — bkz. madde 5, güvenlik gerekçesi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ICustomerRepository.GetFullNameAsync(customerId, ct)` — isim çözümü.
- `TimeProvider` — test edilebilirlik için enjekte edilebilir saat kaynağı (varsayılan `TimeProvider.System`).
- Tüketicileri: `WorkflowMessageBuilder` (yazılı akış) ve `RealtimeNativeService` (sesli native mod).
- **`IContextProvider` implemente etmez** — `ContextPipeline`'ın parçası değildir, doğrudan
  yazılı/sesli mesaj kurucuları tarafından çağrılır. Bu, [`IContextProvider.md`](IContextProvider.md)'de
  listelenen diğer sağlayıcılardan yapısal farkıdır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Neden ortak bir servis:** Metin iki kanalda ayrı ayrı yazılsaydı, biri güncellenip
> diğeri unutulabilirdi — sesli kanalın kimliği/tarihi hiç görmemesi geçmişte tam olarak
> böyle bir sapmadan kaynaklanmıştı.

**Güvenlik kısıtı:** İsim `AuthenticatedCustomerId` (JWT'den) üzerinden çözülür,
`SessionState.CustomerId` (LLM'in metinden çıkardığı, kullanıcının serbestçe değiştirebildiği
alan) KULLANILMAZ — aksi halde kullanıcı "ben 1008'im" diyerek ajanı başka birinin adıyla
hitap etmeye ikna edebilirdi.

İsim çözülemezse (misafir/anonim oturum veya repository'de kayıt yoksa) çıktı asla boş
dönmez — yalnızca tarih döner, çünkü tarih her zaman faydalıdır: "yarın", "bu ay" gibi göreli
ifadeler bunun üzerinden yorumlanır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `BuildAsync(session, ct)` | Tarihi `"d MMMM yyyy, dddd"` formatında (tr-TR) üretir, `ResolveFullNameAsync` ile ismi çözer. İsim varsa `"Şu an sizinle görüşen, kimliği doğrulanmış müşteri: {isim}. Bugünün tarihi: {tarih}. Uygun olduğunda müşteriye adıyla hitap edebilir…"`; yoksa yalnızca `"Bugünün tarihi: {tarih}."` döner. |
| `ResolveFullNameAsync(session, ct)` *(private)* | `session?.State.AuthenticatedCustomerId` yoksa veya `long`'a parse edilemiyorsa `null` döner; aksi halde `ICustomerRepository.GetFullNameAsync` çağrılır. |

## 7. Bağımlılıklar

Constructor injection ile: `ICustomerRepository` (zorunlu), `TimeProvider?` (opsiyonel,
verilmezse `TimeProvider.System`).

## Bağlantılar

- [IContextProvider.md](IContextProvider.md) — yapısal fark için karşılaştırma
