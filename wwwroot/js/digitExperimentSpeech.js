// Isolated TTS for digit training — replace or extend without touching Blazor code.
window.digitExperimentSpeech = {
    speakDigit: function (digit) {
        if (!("speechSynthesis" in window)) {
            return;
        }
        const d = String(digit);
        try {
            window.speechSynthesis.cancel();
        } catch (e) {
            /* ignore */
        }
        const u = new SpeechSynthesisUtterance(d);
        u.rate = 0.95;
        u.pitch = 1;
        window.speechSynthesis.speak(u);
    }
};
