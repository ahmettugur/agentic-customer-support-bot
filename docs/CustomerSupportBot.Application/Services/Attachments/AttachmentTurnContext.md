# AttachmentTurnContext

- **Kaynak:** `CustomerSupportBot.Application/Services/Attachments/AttachmentTurnContext.cs`
- **Tür:** `public static class`

## Ne işe yarar?

Ajanlar görüntüyü değil görsel modelin açıklamasını görür. Bu sınıf mesajla gelen fotoğrafları
doğrular ve açıklamalarını kullanıcının mesajına ekler:

```
Ürün kırık geldi

[Müşterinin eklediği fotoğraf — otomatik analiz]: Kulpu kırık beyaz kupa.
```

- `ResolveAsync(store, ids, sessionId, customerId, maxPerMessage)` — yalnızca BU oturuma ve BU
  müşteriye ait kimlikler, tekrarsız, ilk `maxPerMessage` tanesi. Diğerleri sessizce yok sayılır
  (`ChatPortService` sayıyı loglar).
- `Compose(query, attachments)` — birden fazla fotoğrafta "fotoğraf 1", "fotoğraf 2"; açıklama yoksa
  "otomatik analiz yapılamadı; temsilci fotoğrafı görebilir".

Metin geçmişe de bu hâliyle yazılır: fotoğraf bir turda, sipariş numarası sonraki turda gelse de
ajan fotoğrafı bilir; temsilci panelinde de görünür. Etiket, şikayet ve sipariş ajanlarının
prompt'larında adıyla anılır — `PromptContractTests.PhotoNoteLabel_IsTheSameInCodeAndAgentPrompts`
iki ucu birlikte kilitler.
