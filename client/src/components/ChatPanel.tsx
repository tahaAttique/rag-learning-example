import { useState } from "react";
import type { ChatTurn } from "../types";
import { askQuestion } from "../api/client";

export function ChatPanel() {
  const [question, setQuestion] = useState("");
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleAsk(e: React.FormEvent) {
    e.preventDefault();
    const trimmed = question.trim();
    if (!trimmed || loading) return;

    setLoading(true);
    setError(null);
    try {
      const response = await askQuestion(trimmed);
      setTurns((prev) => [...prev, { question: trimmed, answer: response.answer, sources: response.sources }]);
      setQuestion("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Something went wrong.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="panel chat-panel">
      <h2>Ask your documents</h2>

      <div className="chat-history">
        {turns.length === 0 && <p className="empty">Upload a PDF, then ask a question about it.</p>}
        {turns.map((turn, i) => (
          <div key={i} className="chat-turn">
            <p className="question">{turn.question}</p>
            <p className="answer">{turn.answer}</p>

            {turn.sources.length > 0 && (
              <details className="sources">
                <summary>{turn.sources.length} source chunk(s)</summary>
                {turn.sources.map((s, j) => (
                  <div key={j} className="source">
                    <div className="source-meta">
                      {s.documentName} - chunk {s.chunkIndex} - similarity {s.score.toFixed(3)}
                    </div>
                    <div className="source-text">{s.text}</div>
                  </div>
                ))}
              </details>
            )}
          </div>
        ))}
      </div>

      {error && <p className="error">{error}</p>}

      <form onSubmit={handleAsk} className="chat-form">
        <input
          type="text"
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Ask a question about your uploaded PDFs..."
          disabled={loading}
        />
        <button type="submit" disabled={loading || !question.trim()}>
          {loading ? "Thinking..." : "Ask"}
        </button>
      </form>
    </div>
  );
}
