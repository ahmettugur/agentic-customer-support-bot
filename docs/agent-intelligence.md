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
