import { useCallback, useEffect, useRef, useState } from "react";

// Voice input via the browser's built-in SpeechRecognition API: no server round trip,
// no API key, no cost. Chrome and Edge only - Firefox and Safari don't implement it,
// which is why `isSupported` is exposed rather than only failing on click.
//
// Recognition is non-continuous, so the browser ends it on its own after detecting a
// pause, not just when stop() is called. onend is therefore the single place a result
// is reported; stop() only requests an early end.
//
// The hook owns every failure path, so onResult is called only with a usable, non-empty
// transcript - callers never have to distinguish "empty because of an error" from
// "empty because the mic heard nothing".

// Resolved lazily rather than at module scope: touching `window` while the module is
// being imported breaks any non-browser consumer (tests, SSR, prerender).
function getSpeechRecognition(): { new (): SpeechRecognition } | undefined {
  if (typeof window === "undefined") return undefined;
  return window.SpeechRecognition ?? window.webkitSpeechRecognition;
}

// SpeechRecognitionErrorEvent.error codes, mapped to something a user can act on.
// Anything unlisted falls through to a generic message.
const ERROR_MESSAGES: Record<string, string> = {
  "no-speech": "Didn't catch that - try again.",
  "audio-capture": "No microphone found. Check that one is connected, then try again.",
  "not-allowed": "Microphone access is blocked. Allow it in your browser's site settings, then try again.",
  "service-not-allowed": "Your browser blocked its speech recognition service.",
  network: "Speech recognition needs a network connection and couldn't reach the service.",
};

export function useVoiceRecorder() {
  const [isRecording, setIsRecording] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const recognitionRef = useRef<SpeechRecognition | null>(null);
  const transcriptRef = useRef("");
  const failedRef = useRef(false);

  const isSupported = getSpeechRecognition() !== undefined;

  // If the component unmounts mid-recording, stop the browser listening and make sure
  // onend can't fire a state update (or onResult) into a component that no longer exists.
  // Handlers are detached before abort() precisely because abort() triggers onend.
  useEffect(() => {
    return () => {
      const recognition = recognitionRef.current;
      if (!recognition) return;
      recognition.onresult = null;
      recognition.onerror = null;
      recognition.onend = null;
      recognition.abort();
      recognitionRef.current = null;
    };
  }, []);

  const start = useCallback((onResult: (text: string) => void) => {
    setError(null);

    const SpeechRecognitionCtor = getSpeechRecognition();
    if (!SpeechRecognitionCtor) {
      setError("Voice input isn't supported in this browser. Try Chrome or Edge.");
      return;
    }
    if (recognitionRef.current) return;

    const recognition = new SpeechRecognitionCtor();
    recognition.lang = "en-US";
    recognition.interimResults = false;
    recognition.maxAlternatives = 1;
    transcriptRef.current = "";
    failedRef.current = false;

    recognition.onresult = (e) => {
      transcriptRef.current = e.results[0]?.[0]?.transcript ?? "";
    };

    recognition.onerror = (e) => {
      // onend still fires after this, so record that the attempt failed to stop it
      // reporting an empty transcript over the top of this more specific message.
      failedRef.current = true;
      setError(ERROR_MESSAGES[e.error] ?? "Voice input failed - try again.");
    };

    recognition.onend = () => {
      setIsRecording(false);
      recognitionRef.current = null;

      if (failedRef.current) return;

      const transcript = transcriptRef.current.trim();
      if (transcript) onResult(transcript);
      else setError("Didn't catch that - try again.");
    };

    try {
      recognition.start();
    } catch {
      // Chrome throws InvalidStateError if recognition is somehow already running.
      setError("Voice input is already running - wait a moment and try again.");
      return;
    }

    recognitionRef.current = recognition;
    setIsRecording(true);
  }, []);

  const stop = useCallback(() => {
    recognitionRef.current?.stop();
  }, []);

  return { isSupported, isRecording, start, stop, error };
}
