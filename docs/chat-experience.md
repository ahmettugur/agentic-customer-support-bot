# Sohbet deneyimi (müşteri arayüzü)

## Konuşmayı geri yükleme ve geçmiş konuşmalar

- Aktif oturumun kimliği bu tarayıcıda (`localStorage`: `csbot.activeSession`) tutulur. Sayfa yenilenince / sekme kapanıp açılınca
  konuşma sunucudan geri yüklenir. **"Yeni sohbet"** bu kaydı siler: yenilemede eski konuşma istenmeden geri gelmez.
- Geri yükleme müşterinin **kendi** oturum listesine karşı doğrulanır (sunucu sahiplik kontrolü); aynı tarayıcıda başka bir müşteri
  girerse hiçbir şey yüklenmez. Depolama kapalıysa (gizli pencere) özellik sessizce devre dışı kalır, sohbet etkilenmez.
- Başlıktaki **Konuşmalarım** paneli geçmiş konuşmaları listeler (başlık = ilk mesaj, göreli zaman, mesaj sayısı; en fazla 30, mesajı
  olmayanlar gizli) ve birini açar. Zaman etiketi elle biçimlenir (Blazor WASM'de tr-TR ICU verisi yok).
- Çökme sonrası kurtarılan bir turun yanıtı (bkz. `docs/operations.md`) konuşma geçmişine yazıldığından artık müşteri tarafından görülür.

## Yanıt geri bildirimi (👍 / 👎)

Her tamamlanmış bot yanıtının altında 👍/👎 vardır (sesli görüşme, temsilci ve akan mesajlarda yok). Aynı düğmeye tekrar basmak oyu geri çeker.
👎'den sonra sabit bir kümeden **neden** seçilir: Yanlış bilgi · Sorumu yanıtlamadı · Anlaşılmadı · Diğer.

- **Anahtar:** (oturum, mesaj metninin özeti). Konuşma geçmişi mesaj/trace kimliği taşımadığından sunucu özeti kendisi hesaplar ve metnin
  **o oturumun gerçek bir bot mesajı** olduğunu doğrular (kullanıcının kendi mesajına ya da uydurma metne oy verilemez). Yanıtı üreten
  tur, oturumun trace'lerinde `FinalResponse` eşleşmesiyle bulunur (bulunamazsa oy yine kaydedilir).
- **API:** `POST /sessions/{id}/messages/feedback` `{ text, helpful: true|false|null, reason?, comment? }` (yalnızca müşteri, kendi oturumu).
  Oylar `GET /sessions/{id}/messages` yanıtında bot mesajlarına eklenir, yenilemede korunur. Admin: `GET /analytics/message-feedback/recent`
  (bot metni dönmez).
- **Nereye akar:** `analytics.message_feedback`; **LessonMiner** (👎 alan tur ders çıkarma adayıdır ve neden prompt'a girer);
  Langfuse'ta `message_feedback` oturum skoru (BOOLEAN; **serbest metin yorum gitmez**, yalnızca neden kodu); Prometheus
  `chat_message_feedback_total{helpful,reason}` ve Grafana panosunda panel.
- **KVKK:** yorum serbest metin olduğundan dışa aktarmaya girer, oturum silinince silinir; Langfuse skoru önce oradan kaldırılır ve
  başarısızlıkta silme tekrarlanır (veri bir yerde kalmışken "silindi" denmez).
