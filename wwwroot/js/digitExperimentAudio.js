// Web Audio metronome clicks — keep isolated from TTS in digitExperimentSpeech.js
(function () {
    const PITCH = 1000; // short tick pitch (not the "tempo" Hz from settings)

    const api = (window.digitExperimentAudio = {});

    var ctx = null;

    function getContext() {
        if (ctx) {
            return ctx;
        }
        const C = window.AudioContext || window.webkitAudioContext;
        if (!C) {
            return null;
        }
        ctx = new C();
        return ctx;
    }

    /// Resume AudioContext in the same user-gesture path as the first Start (required on many browsers).
    api.prepare = async function () {
        const c = getContext();
        if (c && c.state === "suspended") {
            await c.resume();
        }
    };

    /// One metronome tick at the start of a beat.
    api.playMetronomeClick = function () {
        const c = getContext();
        if (!c) {
            return;
        }
        const t = c.currentTime;
        const osc = c.createOscillator();
        const g = c.createGain();
        osc.type = "sine";
        osc.frequency.setValueAtTime(PITCH, t);
        g.gain.setValueAtTime(0.2, t);
        g.gain.exponentialRampToValueAtTime(0.0001, t + 0.06);
        osc.connect(g);
        g.connect(c.destination);
        osc.start(t);
        osc.stop(t + 0.08);
    };

    /// Clear downbeat, then a sustained low tone for the full duration (end-of-round "long beat").
    api.playLongBeat = function (durationMs) {
        const c = getContext();
        if (!c) {
            return;
        }
        const d = Math.min(10, Math.max(0.4, (durationMs || 2000) / 1000));
        const t0 = c.currentTime;
        // Short tick on the 1
        (function () {
            const o = c.createOscillator();
            const g = c.createGain();
            o.type = "sine";
            o.frequency.setValueAtTime(PITCH, t0);
            g.gain.setValueAtTime(0.18, t0);
            g.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.05);
            o.connect(g);
            g.connect(c.destination);
            o.start(t0);
            o.stop(t0 + 0.07);
        })();
        // Long body (hear the whole rest)
        const osc = c.createOscillator();
        const g = c.createGain();
        osc.type = "sine";
        osc.frequency.setValueAtTime(175, t0 + 0.02);
        g.gain.setValueAtTime(0, t0);
        g.gain.linearRampToValueAtTime(0.11, t0 + 0.04);
        g.gain.exponentialRampToValueAtTime(0.0001, t0 + d);
        osc.connect(g);
        g.connect(c.destination);
        osc.start(t0);
        osc.stop(t0 + d);
    };
})();
