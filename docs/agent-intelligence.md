# Ajan zekâsı geliştirmeleri

Sohbet ajan sisteminin kalitesini ve güvenilirliğini artıran bölümler. Her bölüm bağımsız açılıp kapatılabilir.

## Düşük güvende insana devir

`ReasoningResult.ConfidenceScore` (planlama güveni) `0.5`'in **altındaysa** tur "düşük güvenli" sayılır
(`WellKnown.ConfidenceThresholds.Low`). Sayaç `SessionState.LowConfidenceTurns` içinde ardışık turları tutar ve —
duygu sayacı gibi — yalnızca `SessionStateExtractor` tarafından yazılır.

| Ardışık düşük tur | Davranış |
|---|---|
| 1 | Yanıtın altındaki hızlı yanıt çiplerinin başına "Temsilciyle görüşmek istiyorum" eklenir |
| ≥ `ConsecutiveTurnsForEscalation` (2) | Eskalasyon açılır (`AgentName=LowConfidence`, sebep: "Bot art arda N turdur isteği anlayamadı"); müşteri `handoff_pending`'i, temsilci Agent Assist özetini görür |

Atlanır: temsilci modu, oturumda açık eskalasyon, açık "temsilci istiyorum" niyeti (kendi yolu var), `Enabled=false`.
Zaman aşımı/hata yer tutucusu (`IsFallback`) ve parser varsayılanı (`0.5`) güven sinyali sayılmaz — botun anlayamadığı
anlamına gelmezler. Ayar: `LowConfidenceHandoff:Enabled`, `LowConfidenceHandoff:ConsecutiveTurnsForEscalation`.

**Sınır:** Güven, *planlama* aşamasının isteği anlama güvenidir; son yanıtın doğruluğunu ölçmez. Yanıt metnindeki "bilmiyorum"
gibi ifadeleri yakalamaz.

## Politika kod olarak: iade/iptal kural motoru

İptal ve iade kuralları `Domain/Model/OrderPolicy.cs` içinde **tek yerde** durur. Önceden kural üç yerde (depo, araç mesajı,
prompt) ayrı ayrı yaşıyor, süre dolduğunda araç "süresi dolmuş **olabilir**" diye tahmin yürütüyordu.

| Karar | Kural |
|---|---|
| İptal | Yalnızca `İşleniyor` / `Kargolandı`; teslim edilmişse "iade talebi oluşturulabilir" yönlendirmesi |
| İade | Yalnızca `Teslim Edildi` **ve teslimden itibaren 14 gün** (teslim tarihi yoksa sipariş tarihi — en tutucu); son gün dahil |
| Zaten iptal / iade var | Ayrı kodlarla reddedilir |

Her karar `PolicyDecision` döner: izin, kod, müşteriye aynen söylenebilecek gerekçe, son tarih ve kalan gün
("Teslimden itibaren 14 günlük iade süresi 18.10.2026 tarihinde doldu.").

**Yeni araç `order_eligibility_tool`** (salt-okunur, onay gerektirmez): müşteri "iade edebilir miyim?", "süre doldu mu?" diye
sorduğunda ajan talep **açmadan** kural motorundan kesin cevap alır. Sahiplik önce doğrulanır; başka müşterinin siparişi
sızdırılmaz. Depo (`RequestReturn`) ve `ReturnRequestTool` hata mesajı aynı kuralı kullanır, yani tool ile depo ayrışamaz.

**Sınır:** Sesli görüşme (Realtime) araç listesine eklenmedi; orada iade kuralı hâlâ yürütme anında uygulanır.
