# CustomerVoiceCallCard

- **Kaynak:** `CustomerSupportBot.Web/Components/CustomerVoiceCallCard.razor`
- **Kullanım:** Müşteri sohbet sayfası (`Chat.razor`), `voice_signal` `ring` geldiğinde

## Ne işe yarar?

Gelen aramayı müşteriye gösterir: "… sizi arıyor", kayıt ve döküm için **rıza metni**, **Reddet** /
**Kabul et**. Kabul etmeden görüşme kurulmaz.

- Kabul: mikrofon izni istenir; izin yoksa arama `no_microphone` sebebiyle reddedilir ve temsilciye
  "Müşterinin mikrofonu yok" bildirilir.
- Görüşmede: temsilci adı, süre, sessize al, **Bitir**.
- Müşteri sesli AI modundaysa gelen arama önce o oturumu kapatır.
- Döküm satırları müşteriye gösterilmez.
