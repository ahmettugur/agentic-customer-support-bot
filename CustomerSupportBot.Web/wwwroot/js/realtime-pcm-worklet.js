// realtime-pcm-worklet.js
// AudioWorklet processor — mikrofon Float32 -> PCM16 (Int16) chunks.
// 24kHz mono, ~50ms paketler halinde main thread'e postMessage.

class PcmCaptureProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        // 24000 sample/sn × 0.05 = 1200 örnek = ~50ms
        this._chunkSamples = 1200;
        this._buffer = new Int16Array(this._chunkSamples);
        this._offset = 0;
    }

    process(inputs) {
        const input = inputs[0];
        if (!input || input.length === 0) return true;
        const channel = input[0]; // mono — ilk kanal yeterli
        if (!channel) return true;

        for (let i = 0; i < channel.length; i++) {
            // Float32 [-1,1] -> Int16 [-32768, 32767]
            let s = channel[i];
            if (s > 1) s = 1; else if (s < -1) s = -1;
            this._buffer[this._offset++] = s < 0 ? s * 0x8000 : s * 0x7FFF;

            if (this._offset >= this._chunkSamples) {
                // Kopyalanmış ArrayBuffer transfer et — sıfır-kopya postMessage
                const out = this._buffer.buffer.slice(0);
                this.port.postMessage(out, [out]);
                this._buffer = new Int16Array(this._chunkSamples);
                this._offset = 0;
            }
        }
        return true;
    }
}

registerProcessor('pcm-capture-processor', PcmCaptureProcessor);
