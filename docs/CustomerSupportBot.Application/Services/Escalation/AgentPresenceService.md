# AgentPresenceService

- **Kaynak:** `Services/Escalation/AgentPresenceService.cs`
- **Port:** `IAgentPresencePort` (inbound)
- **Tasarım:** [2026-10-05-agent-presence-design.md](../../../superpowers/specs/2026-10-05-agent-presence-design.md)

## Ne İşe Yarar

Temsilcinin çevrimiçi/uzakta durumunu yönetir. Durum `HumanAgent` üzerinde saklanır
(`Presence`, `PresenceChangedAt`, `LastSeenAt`); **geçerli durum** okuma anında hesaplanır:
seçilen durum çevrimdışı değilse ve son kalp atışı `Routing:PresenceTimeoutSeconds`'tan yeniyse seçilen
durum, aksi hâlde çevrimdışı (`HumanAgent.EffectivePresence`). [SkillsBasedRouter](../Routing/SkillsBasedRouter.md)
aynı kuralı ve aynı süreyi kullanır.

## İşlemler

| Üye | Davranış |
|---|---|
| `Get(agentId)` | Geçerli + seçilen durum, durumun başladığı an, son kalp atışı, yük. |
| `Set(agentId, presence)` | Temsilcinin seçimi. `Since` yalnızca durum değişince güncellenir. |
| `Connect(agentId)` | Panel açılışı: seçim çevrimdışıysa çevrimiçi yapılır; çevrimiçi/uzakta ise korunur (molaya "uzakta" çıkan temsilci sayfayı yenileyince çevrimiçine dönmez). |
| `Heartbeat(agentId)` | Yalnızca `LastSeenAt`; açık "çevrimdışı" seçimini geri almaz. |
| `GetAll()` | Aktif temsilciler: önce çevrimiçi, sonra uzakta, sonra çevrimdışı; ada göre (tr-TR). |

Bilinmeyen temsilci için tüm işlemler `null` döner (uçta 404).

## Pod'lar arası

`PostgresHumanAgentRegistry.SetPresence/TouchPresence` yalnızca durum sütunlarını günceller ve yalnızca
durum alanlarını `csbot:humanagent:presence` kanalına yayınlar. Tam kayıt yayınlansaydı her kalp atışı,
yayınlayan pod'un (mesaj kaçırmışsa eski) yük sayacını tüm pod'lara yayardı. Tam kayıt yayını
(`upserted`) alınırken daha yeni durum bilgisi korunur; yönetici güncellemesi durum sütunlarına dokunmaz.
