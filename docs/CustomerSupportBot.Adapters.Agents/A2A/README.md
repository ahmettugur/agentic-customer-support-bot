# CustomerSupportBot.Adapters.Agents.A2A

Bu klasör, dış sistemlerle Agent-to-Agent (A2A) protokolü üzerinden haberleşen, salt-okunur ve girdi boyutu korumalı ajan kataloğunu barındırır.

## Dosyalar

- [A2AAgentCatalog](A2AAgentCatalog.md) — Dış sistemlere açık `ProductInfoAgent`, `OrderInfoAgent` ve `ComplaintInfoAgent` salt-okunur ajanlarını kuran katalog.
- [InputLimitedAgent](InputLimitedAgent.md) — Dış kanaldan gelen isteklerde mesaj boyutu (`MaxMessageChars`) ve parça sayısını (`MaxParts`) denetleyen `DelegatingAIAgent` sarmalayıcısı.
