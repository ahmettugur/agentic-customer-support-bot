// Js/chat-api-client.js
// API iletişimi ve oturum yönetimi

class ChatApiClient {
    baseUrl;
    sessionId;

    constructor(baseUrl) {
        this.baseUrl = baseUrl.replace(/\/+$/, "");
        this.sessionId = null;
    }

    async sendMessage(text) {
        const response = await fetch(`${this.baseUrl}/chat/`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ query: text, sessionId: this.sessionId })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        const data = await response.json();

        if (data.sessionId) {
            this.sessionId = data.sessionId;
        }

        return {
            text: data.response || "(boş yanıt)",
            reasoning: data.reasoning || null
        };
    }

    /**
     * SSE ile streaming mesaj gönderir.
     * onEvent callback'i her event için çağrılır: { type, data }
     * Örn tipleri: session, reasoning_start, reasoning_delta, reasoning_complete,
     *              agent, response_start, response_delta, response_complete, error, done
     */
    async sendMessageStream(text, onEvent, signal) {
        const response = await fetch(`${this.baseUrl}/chat/stream`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "text/event-stream"
            },
            body: JSON.stringify({ query: text, sessionId: this.sessionId }),
            signal
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder("utf-8");
        let buffer = "";

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            // SSE event'leri "\n\n" ile ayrılır
            let sepIndex;
            while ((sepIndex = buffer.indexOf("\n\n")) !== -1) {
                const rawEvent = buffer.slice(0, sepIndex);
                buffer = buffer.slice(sepIndex + 2);

                const parsed = this._parseSseEvent(rawEvent);
                if (!parsed) continue;

                // Session ID'yi client state'e yaz
                if (parsed.type === "session" && parsed.data?.sessionId) {
                    this.sessionId = parsed.data.sessionId;
                }
                if (parsed.type === "done" && parsed.data?.sessionId) {
                    this.sessionId = parsed.data.sessionId;
                }

                onEvent(parsed);
            }
        }
    }

    _parseSseEvent(raw) {
        let eventType = "message";
        const dataLines = [];

        for (const line of raw.split("\n")) {
            if (line.startsWith("event:")) {
                eventType = line.slice(6).trim();
            } else if (line.startsWith("data:")) {
                dataLines.push(line.slice(5).trim());
            }
        }

        if (dataLines.length === 0) return null;

        const dataStr = dataLines.join("\n");
        let data = null;
        try {
            data = JSON.parse(dataStr);
        } catch {
            data = dataStr;
        }

        return { type: eventType, data };
    }

    async getSessions() {
        const response = await fetch(`${this.baseUrl}/sessions/`);
        if (!response.ok) return [];
        return await response.json();
    }

    async getSessionMessages(sessionId) {
        const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/messages`);
        if (!response.ok) return [];
        return await response.json();
    }

    setSession(sessionId) {
        this.sessionId = sessionId;
    }

    resetSession() {
        this.sessionId = null;
    }

    // ─── Rating API ───

    async submitRating(sessionId, stars, feedback) {
        const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/rating`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ stars, feedback })
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return await response.json();
    }

    async getRating(sessionId) {
        const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/rating`);
        if (!response.ok) return null;
        return await response.json();
    }
}
