# Hibrit arama (dense + sözcüksel)

Bilgi tabanı, ders ve episodik bellek aramaları iki sinyali birleştirir:

- **Dense (anlamsal):** `text-embedding-3-large` vektörünün kosinüs benzerliği. Eş anlamlıları yakalar, ama ürün/iade
  kodu (`XK-9921`), sipariş no gibi **tam eşleşme** gereken sorgularda zayıftır.
- **Sözcüksel (BM25 benzeri):** sözcük eşleşmesi. Nadir sözcükler yüksek, yaygın sözcükler düşük ağırlık alır.

İki sıralama **Reciprocal Rank Fusion** ile birleştirilir: `skor = Σ 1 / (k + sıra)`. Skorlar farklı ölçekte olduğundan
(kosinüs vs. BM25) ham skorlar toplanmaz, yalnızca sıra kullanılır.

## Ayar

```json
"SemanticMemory": { "Retrieval": { "Hybrid": { "Enabled": true, "Candidates": 20, "RrfK": 60 } } }
```

Sınıf varsayılanı kapalıdır; `appsettings.json` açar. Kapatmak eski davranışa döner (yalnızca dense).

- `MinScore` yalnızca dense sinyale uygulanır. Hibritte dönen `Score` RRF'nin **0–1'e ölçeklenmiş** halidir (her iki
  sinyalde 1. sıra = 1.0); kosinüs benzerliği değildir. Bağlam metnindeki `score=` değeri bu yüzden hibritte farklı
  okunmalıdır.
- Hibrit yalnızca **sorgu metni** verilen çağrılarda çalışır (`SearchAsync(kind, metin)` ve bağlam sağlayıcısı).

## Nasıl çalışır

Qdrant, var olan bir koleksiyona sonradan sparse vektör eklemeye izin vermez. Bu yüzden her `X` koleksiyonunun yanında
yalnızca sparse vektör taşıyan bir **`X__lex`** koleksiyonu tutulur (aynı nokta kimlikleri, aynı payload/etiketler,
`Modifier.Idf` ile). Mevcut koleksiyonlara dokunulmaz.

- **Yazma:** `UpsertAsync` iki koleksiyona da yazar. Sözcüksel yazım en iyi gayrettir (hata loglanır).
- **Migrasyon:** Başlangıçta `X__lex` yoksa kurulur ve `X`'teki mevcut belgelerle doldurulur. Elle işlem gerekmez.
- **Silme:** Her silme (`DeleteAsync`, oturum, müşteri/etiket, damga) `X__lex`'e de uygulanır. Sözcüksel koleksiyon belge
  metnini kopyaladığı için bu **KVKK açısından zorunludur**; yoksa silinen veri aranabilir kalırdı.
- **Yeniden kurulum:** Embedding boyutu değişip koleksiyon yeniden kurulursa `X__lex` de sıfırlanır.
- **Hata davranışı:** Sözcüksel taraf kurulamaz ya da sorgulanamazsa arama dense'e düşer; uygulama başlangıcı durmaz.

## Türkçe metin işleme (`SparseTextEncoder`)

`İ/I/ı` doğru indirgenir, aksanlar düşer (`iade süreci` = `iade sureci`), harf-yalnız sözcükler ilk 5 karaktere kırpılır
(`kargo/kargom/kargoyu` aynı kökte buluşur), sayı içeren sözcükler (sipariş no, kod) kırpılmaz, kısa bir etkisiz sözcük
listesi atılır. Sözcük indeksi FNV-1a ile hesaplanır; süreçler ve sürümler arasında kararlıdır.

> Kök bulma kaba bir sezgisel yöntemdir (dilbilgisel değil). Farklı köklerin ilk 5 harfi çakışabilir; IDF bunu
> dengeler ama sıfırlamaz.
