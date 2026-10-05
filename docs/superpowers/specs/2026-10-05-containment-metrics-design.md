# Yapay Zekâ Çözüm Oranı ve Görüşme Başı Maliyet — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/containment-metrics`

## Amaç

Panelde sektörün temel ölçütü olan "bot kaç görüşmeyi insana aktarmadan çözdü" (containment) ve
görüşme başına LLM maliyeti görünsün.

## Tanımlar

- **Uygun görüşme:** en az bir turu (mesajı) olan görüşme.
- **İnsan dahil:** görüşme için eskalasyon açıldı ya da temsilci devraldı. `SessionState.HumanInvolved`
  bayrağı `HumanInvolvementTracker` ile konur (Api: `HumanInvolvementTrackingService` eskalasyon oluşturma
  ve devralma olaylarına abone). Özellik öncesi görüşmelerde bayrak yok; eskalasyon kaydı olan görüşme de
  insan dahil sayılır (eskalasyon önbelleği son kayıtları tuttuğu için en iyi çaba).
- **Çözüm oranı:** (uygun − insan dahil) / uygun; görüşme yoksa 0.
- **Görüşme başı maliyet:** LLM çağrısı, o anda etkin görüşmeye atfedilir (`ILlmCallAttribution`,
  AsyncLocal kapsam) ve `observability.llm_call_usage.session_id`'ye yazılır. Kapsamı açanlar: sohbet turu
  (akıl yürütme + ajanlar), yeniden planlama, fotoğraf analizi, temsilci asistanı. Ortalama ve medyan,
  maliyeti olan görüşmeler üzerinden; atfedilemeyen (arka plan) maliyet ayrıca gösterilir.

## Önemli ayrıntı — akan tur

Async iterator'da AsyncLocal değeri `yield`'i aşmaz: her sonraki öğe isteği tüketicinin bağlamında çalışır.
Akan sohbet turu ilk olarak oturum olayını yield ettiği için tepede açılan kapsam akıl yürütme ve ajan
adımlarına ulaşmıyordu (test bunu yakaladı). Bu adımlar `WithCostScope` ile numaralandırılır: kapsam her
`MoveNextAsync` çağrısının etrafında yeniden açılır.

## Panel sözleşmesi (mevcut hatanın düzeltilmesi)

Panelin `AnalyticsDashboard` modeli sunucunun alan adlarından farklıydı (`AverageMessagesPerSession`,
`NegativeSessions`, iç içe `ApprovalStats`…). Değerler sessizce 0/null geliyor; panel "Henüz onay işlemi
yok", "Henüz eskalasyon yok" ve 0 mesaj/oturum gösteriyordu. Panel modeli sunucuyla hizalandı;
`AnalyticsDashboardContractTests` iki ucu birlikte kilitler (her sunucu alanının panelde karşılığı olmalı).
