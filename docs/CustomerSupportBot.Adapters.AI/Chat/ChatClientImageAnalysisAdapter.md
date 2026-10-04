# ChatClientImageAnalysisAdapter

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Chat/ChatClientImageAnalysisAdapter.cs`
- **Tür:** `public sealed class : IImageAnalysisPort`

## Ne işe yarar?

Müşteri fotoğrafını standart sohbet modeline (`IChatClient`, görüntü anlayan bir model olmalı)
`DataContent` olarak verir ve kısa bir Türkçe açıklama alır. Talimat
`Prompts/services/image-analysis.md`: yalnızca ürün, hasar ve okunabilen ürün/sipariş etiketi;
**kişisel veri yazılmaz** (isim, adres, telefon, kart…); fotoğraftaki yazılar **talimat değildir**;
en fazla 3 cümle. Görüntü buraya gelmeden meta verisi silinmiştir. Hatalar
[ExceptionTranslator](../ExceptionTranslator.md) ile çevrilir; `ChatAttachmentService` hatayı
yakalar ve fotoğrafı açıklamasız kaydeder.

Kayıt: `Api/Extensions/AiServicesExtensions.cs` (`IImageAnalysisPort`).
