import { useEffect, useState } from "react";
import type { ChatTurn } from "../types";
import { askQuestion } from "../api/client";
import { useVoiceRecorder } from "../hooks/useVoiceRecorder";

// Text-to-speech runs entirely in the browser via SpeechSynthesis - no backend call, no
// API key, no cost, which keeps the whole voice feature in line with the rest of the app
// running locally. Checked once here because jsdom and a few embedded webviews lack it.
const speechSynthesisSupported = typeof window !== "undefined" && "speechSynthesis" in window;

export function ChatPanel() {
  const [question, setQuestion] = useState("");
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [loading, setLoading] = useState(false);
  const [autoPlay, setAutoPlay] = useState(true);
  const [playingId, setPlayingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [conversationId, setConversationId] = useState<string | null>(null);

  const { isSupported: micSupported, isRecording, start, stop, error: recorderError } = useVoiceRecorder();

  // Speech synthesis is a global, window-level queue: it keeps talking after this
  // component is gone unless it's explicitly cancelled.
  useEffect(() => {
    return () => {
      if (speechSynthesisSupported) window.speechSynthesis.cancel();
    };
  }, []);

  async function ask(text: string) {
    const trimmed = text.trim();
    if (!trimmed || loading) return;

    setLoading(true);
    setError(null);
    try {
      const response = await askQuestion(trimmed, conversationId);
      setConversationId(response.conversationId);

      const turn: ChatTurn = {
        id: crypto.randomUUID(),
        question: trimmed,
        answer: response.answer,
        sources: response.sources,
        toolCalls: response.toolCalls,
      };
      setTurns((prev) => [...prev, turn]);
      setQuestion("");

      if (autoPlay) playAnswer(turn.id, turn.answer);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Something went wrong.");
    } finally {
      setLoading(false);
    }
  }

  function playAnswer(turnId: string, text: string) {
    if (!speechSynthesisSupported) return;

    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.onstart = () => setPlayingId(turnId);
    utterance.onend = () => setPlayingId(null);
    utterance.onerror = (e) => {
      // cancel() above interrupts whatever is already speaking, which surfaces here as an
      // error on the previous utterance - not a real failure, so don't report it.
      if (e.error === "interrupted" || e.error === "canceled") return;
      setPlayingId(null);
      setError("Couldn't play audio.");
    };
    window.speechSynthesis.speak(utterance);
  }

  function handleMicClick() {
    if (isRecording) {
      stop();
      return;
    }
    setError(null);
    // The hook reports its own failures through `recorderError` and only calls back with
    // a usable transcript, so there is no empty-result case to handle here.
    start(ask);
  }

  function startNewConversation() {
    if (speechSynthesisSupported) window.speechSynthesis.cancel();
    setTurns([]);
    setConversationId(null);
    setError(null);
    setPlayingId(null);
  }

  const micTitle = !micSupported
    ? "Voice input needs Chrome or Edge"
    : isRecording
      ? "Stop recording"
      : "Ask by voice";

  return (
    <div className="panel chat-panel">
      <div className="panel-header">
        <h2>Ask your documents</h2>
        <div className="panel-header-actions">
          {speechSynthesisSupported && (
            <label className="autoplay-toggle">
              <input type="checkbox" checked={autoPlay} onChange={(e) => setAutoPlay(e.target.checked)} />
              Auto-play answers
            </label>
          )}
          {turns.length > 0 && (
            <button type="button" className="link-button" onClick={startNewConversation}>
              New conversation
            </button>
          )}
        </div>
      </div>

      <div className="chat-history">
        {turns.length === 0 && <p className="empty">Upload a PDF, then ask a question about it (by typing or by voice).</p>}
        {turns.map((turn) => (
          <div key={turn.id} className="chat-turn">
            <p className="question">{turn.question}</p>

            {turn.toolCalls.length > 0 && (
              <details className="tool-calls">
                <summary>
                  {turn.toolCalls.length} tool call{turn.toolCalls.length === 1 ? "" : "s"}
                </summary>
                {turn.toolCalls.map((call, j) => (
                  <div key={j} className="tool-call">
                    <code className="tool-name">{call.toolName}</code>
                    <pre className="tool-args">{call.arguments}</pre>
                    <pre className="tool-result">{call.result}</pre>
                  </div>
                ))}
              </details>
            )}

            <p className="answer">
              {turn.answer}
              {speechSynthesisSupported && (
                <button
                  type="button"
                  className="speak-button"
                  onClick={() => playAnswer(turn.id, turn.answer)}
                  disabled={playingId === turn.id}
                  title="Play this answer"
                  aria-label={playingId === turn.id ? "Playing this answer" : "Play this answer"}
                >
                  <span aria-hidden="true">{playingId === turn.id ? "▶ Playing..." : "🔊"}</span>
                </button>
              )}
            </p>

            {turn.sources.length > 0 && (
              <details className="sources">
                <summary>
                  {turn.sources.length} source{turn.sources.length === 1 ? "" : "s"}
                </summary>
                {turn.sources.map((s, j) => (
                  <div key={j} className="source">
                    <span className="source-meta">
                      {s.documentName} · chunk {s.chunkIndex} · {s.score.toFixed(3)}
                    </span>
                    <p>{s.text}</p>
                  </div>
                ))}
              </details>
            )}
          </div>
        ))}
      </div>

      {(error || recorderError) && (
        <p className="error" role="alert">
          {error || recorderError}
        </p>
      )}

      <form
        onSubmit={(e) => {
          e.preventDefault();
          ask(question);
        }}
        className="chat-form"
      >
        <button
          type="button"
          className={`mic-button${isRecording ? " recording" : ""}`}
          onClick={handleMicClick}
          disabled={loading || !micSupported}
          title={micTitle}
          aria-label={micTitle}
          aria-pressed={isRecording}
        >
          <span aria-hidden="true">{isRecording ? "■" : "🎤"}</span>
        </button>
        <input
          type="text"
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder={isRecording ? "Listening..." : "Ask a question about your uploaded PDFs..."}
          disabled={loading || isRecording}
        />
        <button type="submit" disabled={loading || !question.trim()}>
          {loading ? "Thinking..." : "Ask"}
        </button>
      </form>
    </div>
  );
}
