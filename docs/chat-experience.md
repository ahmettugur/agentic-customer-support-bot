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

## Hızlı yanıt çipleri

Tur bittiğinde, **niyete göre sabit ve kural tabanlı** (ek LLM çağrısı yok) en fazla 3 öneri yanıtın altında çip olarak çıkar; tıklanınca metin
müşterinin sonraki mesajı olarak aynen gönderilir. Örn. sipariş sorgusundan sonra: *Siparişimi iptal etmek istiyorum · İade talebi oluşturmak
istiyorum · Siparişimle ilgili şikayetim var*.

Kurallar (`SuggestedRepliesPolicy`): yalnızca `completed` sonlanan, tek niyetli ve **müşteriden bilgi istemeyen** (son ~140 karakterde `?` yok)
turlar öneri alır; hata, eskalasyon, "bulunamadı", bileşik sorgu, bilinmeyen/`genel`/temsilci niyeti ve boş yanıtta çıkmaz. Olay: `ui_hint`
`{ kind: "suggested_replies", suggestions: [...] }` — yanıt ve kayıttan sonra gelir. Çipler yalnızca **son** mesajda ve akış bittiyse görünür;
geçmişten yüklenen konuşmalarda yoktur (kalıcı değil, tur anına özgüdür).

## Sipariş ve şikayet kartları

Sipariş sorgusu (`order_status_tool`, `get_last_order_tool`, `get_all_orders_tool`) ve şikayet sorgusu (`complaint_status_tool`,
`get_all_complaints_tool`) başarılı olunca araç, yanıt metninin yanında **yapılandırılmış bir `ui_hint`** de yayınlar
(`order_cards` / `complaint_cards`). Kart **veritabanındaki gerçek veriden** üretilir (modelin yazdığı metinden değil), bu yüzden
model yanlış özetlese bile kart doğrudur.

- **Sipariş kartı:** numara, durum rozeti, ürün satırları, tarihli zaman çizelgesi (Sipariş alındı → Kargolandı → Teslim edildi;
  iptalde Alındı → İptal edildi; iadede ek adım) ve kargo firması / takip numarası. Liste sorgusunda en yeni 5 kart + toplam sayı.
- **Şikayet kartı:** numara, durum, ilgili sipariş, şikayet metni (en çok 200 karakter), kapanmışsa sonuç açıklaması.
- **Gizlilik:** kartlar yalnızca araç başarılı olunca (sahiplik doğrulanmış) yayınlanır; başkasının siparişi/şikayeti kart olarak da çıkmaz.
  İç not, atanan temsilci ve onay kaydı kartta **yoktur**; çözüm açıklaması yalnızca kapanmış şikayette görünür.
- **Kanal:** yalnızca canlı sohbet akışında çizilir. Sesli görüşme, A2A ve MCP'de akış olmadığı için ipucu düşer, araç sonucu değişmez.
- Kartlar tur anına özgüdür; geçmişten yüklenen (yenilemeden sonraki) konuşmalarda kart yoktur, metin vardır.

## İlgili makaleler (bilgi tabanı)

Bot yanıtını verirken bilgi tabanından makaleler bağlama girdiyse (RAG), yanıtın altında **"İlgili makaleler: …"** olarak başlıkları görünür
(en çok 3; aynı başlık tek). Olay: `ui_hint` `{ kind: "sources", sources: [{ title }] }`, `SemanticMemoryContextProvider` yayınlar.

**Etiket bilerek "kaynak" değil "ilgili":** model bu makalelere dayandığını söylemez, yalnızca onları görmüştür; "kaynak" demek olmayan bir
atıf iddiası olurdu. Gösterilen, bu tur bağlama **gerçekten enjekte edilen** makalelerdir — bağlam bütçesine sığmayanlar gösterilmez;
dersler ve episodik bellek hiç gösterilmez. Bileşik sorguda her alt görevin ipucu birleştirilir. Makale müşteriye açık bir sayfa olmadığı
için başlık düz metindir (bağlantı yok).

## Başarısız tur: yanıtı kurtarma ve tekrar deneme

İstemci bağlantıyı keserse sunucu turu **iptal eder** (aksi halde LLM çağrısı boşuna sürerdi); yani kopan bir akışın yanıtı genellikle
sonradan gelmez. İki durum ayrılır:

1. **Tur sunucuda yine de tamamlandı** (sahte kopma, proxy zaman aşımı): sayfa oturum geçmişine bakar; gönderilen soruyu ve hemen
   ardından bir bot yanıtını **yeni mesajlar olarak** bulursa yanıtı gösterir (`StreamRecovery`). Eşleşme sıkıdır ve yeniliği,
   tur gönderilmeden önceki mesaj sayısına göre doğrular — müşteri aynı soruyu daha önce de sormuş olabilir, eski bir yanıt yeni sanılmaz.
   Sayaç bilinmiyorsa (temsilci devraldı / sesli görüşme kullanıldı) kurtarma denenmez.
2. **Tamamlanmadı:** hata görünür ve **↻ Tekrar dene** düğmesi çıkar; aynı metin yeniden gönderilir (kullanıcı ve hata balonu yeniden
   çizilir, ikilenmez). Sunucunun bildirdiği hata olaylarında (ör. zaman aşımı) da aynı düğme vardır. Fotoğraflı mesajda düğme yoktur
   (ek kimlikleri tüketildi).

Bilinen sınır: sunucu süreci ölürse (çökme/deploy) ve `WorkflowCheckpointing` açıksa tur başka bir pod'da bitirilir ve yanıt geçmişe yazılır;
bu ~90 sn sürebildiği için sayfa beklemez — yanıt, konuşma geri yüklenince (yenileme) ya da **Konuşmalarım**'dan açılınca görünür.

## Sohbet içi bildirimler

Sohbet açıkken siparişiniz kargoya verilir/teslim edilirse ya da şikayetinizin durumu değişirse, müşteri bunu
e-postayı beklemeden **anında** görür: sohbete bir sistem notu düşer ve kısa bir toast çıkar.

| Olay | Tür (`kind`) | Tetikleyen |
|---|---|---|
| Sipariş kargoya verildi | `order_shipped` | `OrderFulfillmentService.MarkShippedAsync` (gerçek geçiş) |
| Sipariş teslim edildi | `order_delivered` | `OrderFulfillmentService.MarkDeliveredAsync` (gerçek geçiş) |
| Şikayet incelemeye alındı | `complaint_in_review` | `ComplaintManagementService` (üstlenme / yeniden açma / ilk atama) |
| Şikayet sonuçlandı | `complaint_closed` | `ComplaintManagementService` (çözüm / ret) |

**Nasıl akar.** Yayın, e-postanın gittiği noktalarla aynı yerde yapılır; ama e-postadan **bağımsızdır** (e-posta kapalı olsa
da açık sohbet bilgilenir). `CustomerNotificationHub` bildirimi `IMessageBusPort` üzerinden tek kanala (`csbot:customer-notify`)
yayınlar; her pod yalnızca kendi açık bağlantılarına dağıtır — bildirimi veren pod ile müşterinin SSE'sini tutan pod farklı
olabilir. Müşteriye ulaşım, **mevcut kalıcı oturum akışı** (`GET /chat/events/{sessionId}`) üzerinde `notification` olayıyla olur;
yeni bir uç yoktur.

**Güvenlik.** Abonelik, oturum kimliğine değil JWT'deki doğrulanmış müşteri kimliğine (`linked_customer_id`) bağlanır; kimliksiz
akış bildirim almaz. Yazım, akıştaki diğer tüm olaylar gibi her seferinde sahiplik kontrolünden geçer — oturum başka bir müşteriye
geçerse akış kapanır. Metin istemcide Razor ile kaçışlı basılır (kargo firması/takip no yönetici girdisidir).

**Sınırlar (bilerek).** Bildirim *geçicidir*: sohbet açık değilse (ya da bağlantı kopukken) kaybolur ve yeniden gönderilmez —
kalıcı kanal e-postadır. Sesli görüşme / A2A / MCP gibi kalıcı oturum akışı olmayan yüzeylere inmez. Yayın başarısız olursa
durum güncellemesi etkilenmez (hata yutulup loglanır).

## Çıktı guardrail'i

`InputGuard` müşteri mesajını LLM'e gitmeden denetler; `OutputGuardrail` bunun ters yönü: turun **kanonik yanıtı** müşteriye
(`response_complete`) ve konuşma geçmişine yazılmadan önce deterministik olarak taranır. LLM çağrısı yoktur, gecikme ihmal edilebilir.

| Bulgu | Eylem |
|---|---|
| E-posta / telefon — müşterinin kendi kaydı, **kendi yazdığı** metin ya da izinli destek iletişimi **değilse** | Yalnızca o kısım `[gizlendi]` ile maskelenir (`Redacted`) |
| Sır: JWT, `sk-…` anahtarı, `Bearer …`, `Password=`/`ApiKey=` bağlantı dizesi | Yanıt tamamen güvenli bir metinle değiştirilir (`Blocked`) |
| Sistem istemi sızıntısı (`system prompt`, `sistem talimat…`, `talimatlarım`) ve yapılandırılmış yasak ifadeler | `Blocked` |

**Karar kuralları.** Bilinen iletişim = doğrulanmış müşterinin kayıtlı e-postası + müşterinin bu konuşmada *yazdığı* e-posta/telefonlar
+ `Email:FromAddress` + `OutputGuardrail:AllowedContacts`. Botun önceki mesajları bilinen sayılmaz (botun söylediği, müşterinin
verdiği anlamına gelmez). Telefon yalnızca `0…`/`+90`/`90` önekli ya da ayraçlı 10 haneli diziyse sayılır; ayraçsız 10 haneli
kargo takip numaraları, sipariş numaraları ve tutarlar maskelenmez.

**Yapılandırma** (`OutputGuardrail`): `Enabled` (varsayılan açık), `AllowedContacts` (ör. `["0850 123 45 67"]` — bilgi tabanı
makaleleri şirket telefonu/e-postası içeriyorsa buraya ekleyin, yoksa maskelenir), `BlockedPhrases` (kuruma özel iç ifadeler).

**Gözlem.** Müdahale `chat.output_guardrail{verdict,finding}` sayacına yazılır ve uyarı olarak loglanır (içerik loglanmaz).

**Sınırlar (bilerek).** (1) Ham `response_delta` akışı canlı yazıldığı için maskeleme *tam turda* yapılır: müşteri tarayıcısında ham metin
bir an görünebilir, `response_complete` balonu ezer; geçmişe ve sonraki tura **hiç** ham metin yazılmaz. Gerçek "hiç görünmesin"
için delta tamponlamak gerekir (akıcılık kaybı) — şimdilik yapılmadı. (2) Tarama kural tabanlıdır; paraphrase edilmiş sızıntıyı ya da
telefon biçimi dışındaki kişisel verileri (ad, adres) yakalamaz. (3) Sesli görüşme / A2A / MCP yüzeyleri bu kapıdan geçmez.
