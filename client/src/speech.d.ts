// TypeScript's DOM lib ships the Web Speech API's event and result types
// (SpeechRecognitionEvent, SpeechRecognitionErrorEvent) but not the SpeechRecognition
// interface itself or its still-vendor-prefixed window constructors. Only the members
// this app actually uses are declared.
interface SpeechRecognition extends EventTarget {
  lang: string;
  interimResults: boolean;
  maxAlternatives: number;
  onresult: ((this: SpeechRecognition, ev: SpeechRecognitionEvent) => void) | null;
  onerror: ((this: SpeechRecognition, ev: SpeechRecognitionErrorEvent) => void) | null;
  onend: ((this: SpeechRecognition, ev: Event) => void) | null;
  start(): void;
  /** Requests a graceful end; a pending result is still delivered via onresult. */
  stop(): void;
  /** Ends immediately and discards any pending result. */
  abort(): void;
}

interface Window {
  SpeechRecognition?: { new (): SpeechRecognition };
  webkitSpeechRecognition?: { new (): SpeechRecognition };
}
