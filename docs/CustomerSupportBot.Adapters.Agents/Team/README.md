# CustomerSupportBot.Adapters.Agents.Team

Bu klasör, çoklu ajan sistemindeki 6 uzman ajanı (`PlanningAgent`, `ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`, `ResponseAgent`), temel sınıf olan `SupportAgentBase`'i ve yapılandırılmış akıl yürütme şemalarını (`SpecialistReasoningSchema`) barındırır.

## Dosyalar

- [SupportAgentBase](SupportAgentBase.md) — Tüm MAF ajanlarının türediği, hem sync hem async streaming yollarını intercept eden ve debug hook'ları (`OnBeforeRun`, `OnAfterRun`) sunan `DelegatingAIAgent` temel sınıfı.
- [PlanningAgent](PlanningAgent.md) — Kullanıcı talebini analiz ederek `PlanningResult` JSON şeması üreten ve akışı uygun uzmana yönlendiren planlama ajanı.
- [ProductAgent](ProductAgent.md) — Ürün arama, stok ve fiyat sorgulama araçlarına (`product_inquiry_tool`, `product_list_tool`) sahip ürün uzmanı.
- [OrderAgent](OrderAgent.md) — Sipariş durumu, sepet ve HITL onaylı sipariş oluşturma/iptal araçlarına sahip sipariş uzmanı.
- [ComplaintAgent](ComplaintAgent.md) — Şikayet sorgulama ve HITL onaylı şikayet kaydı araçlarına sahip şikayet uzmanı.
- [HumanHandoffAgent](HumanHandoffAgent.md) — Canlı müşteri temsilcisine eskalasyon aktarım koşullarını değerlendiren ajan.
- [ResponseAgent](ResponseAgent.md) — Uzmanların ReAct çıktılarını sentezleyip müşteriye yönelik samimi, temiz Türkçe yanıt metni üreten nihai yanıt ajanı.
- [SpecialistReasoningSchema](SpecialistReasoningSchema.md) — Uzman ajanların `ChatResponseFormat.ForJsonSchema` ile zorlandığı ReAct JSON şeması (`preToolCheck`, `postToolReflection`).
