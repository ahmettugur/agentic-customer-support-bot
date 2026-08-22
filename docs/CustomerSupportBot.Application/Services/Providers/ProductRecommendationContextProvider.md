# ProductRecommendationContextProvider

- **Kaynak:** `Services/Providers/ProductRecommendationContextProvider.cs`
- **Tür:** `public sealed class : IContextProvider`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## 1. Ne İşe Yarar

[`IRecommendationService.Recommend(session)`](../Personalization/RecommendationService.md)'in
ürettiği (varsa) ürün önerilerini `"## 💡 Olası İlgi Alanı"` başlığıyla bağlama ekleyen
sağlayıcıdır. Öneri motorunun kendisi burada değil `RecommendationService`'te yaşar — bu sınıf
sadece sonucu metne çevirir.

## 2. Hangi Amaçla Kullanılır

Ajanın, uygun olduğunda (zorlanmadan) müşteriye ilgilenebileceği başka bir ürün önerebilmesi
için bir **ipucu** sağlamak. Bu blok bir talimat değildir — ajanı hiçbir tool'u tetiklemeye
veya otonom bir eylem başlatmaya zorlamaz; şikayet/sipariş akışlarındaki ajanlar bu bloğu
görmezden gelmekte tamamen serbesttir.

## 3. Sorumlulukları

**Üstlendiği:** `_recommendations.Recommend(session)` sonucunu (varsa) markdown listesine
çevirmek; sonuç boşsa `null` dönmek.

**Üstlenmediği:** Önerinin ne zaman uygun olduğuna karar vermek — susma kuralları (negatif
sentiment, aktif şikayet/iade/iptal niyeti, veri yetersizliği) tamamen
[`RecommendationService`](../Personalization/RecommendationService.md)'te toplanmıştır; bu
provider sonucu sorgusuz kabul eder.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IRecommendationService` — tek veri kaynağı.
- [`ContextPipeline`](../Chat/ContextPipeline.md) — bu provider'ı `Order=8` ile çalıştıran tüketici.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`Order=8` — pipeline'daki tüm provider'lar arasında en yüksek (en düşük öncelikli) `Order`
(`NoopContextProvider`'ın `int.MaxValue`'su hariç). Bilinçli sıralama: ürün önerisi, sipariş/
şikayet gibi kritik olgulardan daha düşük öncelikli bir "iyileştirme"dir — karakter bütçesi
dolduğunda `ContextPipeline` önce bunu düşürür, kritik bilgiyi değil.

Metin bilinçli olarak "**isteğe bağlı, YALNIZCA uygun bağlamda bahset — zorlama**" ifadesiyle
başlar; bu, öneri motorunun kendi susma kurallarına ek bir güvenlik katmanı — LLM'in agresif
bir satış tonuna kaymasını önlemeyi hedefler.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Name` (`string`) | Sabit `"ProductRecommendation"`. |
| `Order` (`int`) | `8`. |
| `GetContextAsync(session, currentQuery, ct)` | `_recommendations.Recommend(session)` çağırır (senkron, `ct` kullanılmaz — LLM/ağ çağrısı yok); sonuç boşsa `null`, doluysa her öneriyi `"- {ProductName} — {Reason}"` satırı olarak listeler. |

## 7. Bağımlılıklar

Constructor injection ile: `IRecommendationService`, `ILogger<ProductRecommendationContextProvider>`.

## Bağlantılar

- [IContextProvider.md](IContextProvider.md)
- [../Personalization/RecommendationService.md](../Personalization/RecommendationService.md) — susma kurallarının gerçek kaynağı
