# AdminTabs

**Dosya:** `Helpers/AdminTabs.cs`

## Ne İşe Yarar

Admin panelinin adresli bölümlerini (`/admin?tab=…`) ve role göre erişim kuralını tek yerde tutar.

## Hangi Amaçla Kullanılır

[Admin](../Pages/Admin.md) sayfası açılışta ve adres değişince bölümü buradan çözer; [`StaffSidebar`](../Layout/StaffSidebar.md)
etkin bağlantıyı aynı kuralla hesaplar.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Eskiden sekme yalnızca bellekteydi: sayfa yenilenince ilk sekmeye dönülüyor, bir bölümün linki paylaşılamıyor,
geri tuşu sekmeler arasında çalışmıyordu. Temsilci adresi elle yazsa bile yönetici bölümünü açamaz —
`Normalize` onu rolün varsayılanına çevirir.

## Metotlar / Üyeler

| Üye | Açıklama |
|-----|----------|
| `All` | Geçerli bölümler: approvals, escalations, chats, history, analytics, orders, conversations, replies, improvements. |
| `Default(isAgent)` | Yönetici için `approvals`, temsilci için `escalations`. |
| `FromQuery(query)` | Sorgu dizesinden `tab` değerini okur (büyük/küçük harf duyarsız); yoksa `null`. |
| `Normalize(tab, isAgent)` | Geçersiz ya da role kapalı bölümü rolün varsayılanına çevirir. Temsilciye açık olanlar: escalations, chats. |

Testler: `tests/CustomerSupportBot.Web.Tests/AdminTabsTests.cs`.
