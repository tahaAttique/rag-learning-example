# RAG Example (.NET + React)

A small, from-scratch Retrieval-Augmented Generation (RAG) app, built to learn how the
pieces fit together rather than to hide them behind a framework.

## What RAG actually is

A plain LLM only knows what was in its training data. RAG lets it answer questions about
*your* documents by doing two things at request time:

1. **Retrieval** — find the pieces of your documents most relevant to the question.
2. **Generation** — hand those pieces to an LLM as context and ask it to answer using them.

To make retrieval possible, every piece of text (a "chunk") is converted into an
**embedding**: a vector of numbers that captures its meaning, positioned so that
similar-meaning text ends up close together in that vector space. A question gets embedded
the same way, and retrieval is just "find the chunks whose vectors are closest to the
question's vector" — measured here with **cosine similarity**.

## Pipeline in this repo

**Ingestion** (`POST /api/documents`):
```
PDF -> extract text (PdfPig) -> split into overlapping chunks (TextChunker)
     -> embed each chunk (Ollama) -> store text + vector in SQLite (VectorStore)
```

**Query** (`POST /api/chat`) — this is an *agentic* loop, not a fixed pipeline. The model is
handed a set of tools and decides for itself whether and when to use them:
```
Question -> model sees available tools (search_documents, calculator)
         -> model requests a tool call, e.g. search_documents{query: "..."}
         -> C# executes it: embed the query, cosine-similarity search, return chunks
         -> result appended to the conversation, model asked again
         -> repeat until the model answers with text instead of a tool call (max 5 rounds)
```

The two tools are intentionally different in character: `search_documents` is fuzzy,
non-deterministic, and needs a model plus a database; `calculator` is pure, instant, and
exact. The model must choose between them from their **descriptions alone** — those
description strings in `AgentTools.Definitions` are prompt engineering, and rewording them
changes the model's behaviour.

An LLM cannot call your C# methods. All it can do is emit a structured request naming a
function and its arguments; your code executes it and feeds the result back. That round trip
is the entirety of "tool calling" / "function calling" / "agents" — see
`api/Services/RagChatService.cs` for the loop and `api/Services/AgentTools.cs` for the
tools themselves (plain C#, no AI in them).

Two details in there are worth copying into real projects:

- **Tool errors are returned, not thrown.** A bad expression comes back to the model as a
  normal tool result describing the problem, so it can correct itself and retry. The wording
  is part of your prompt: small models often try to pass placeholders like `2026 - x`, and an
  error message that says *"substitute the actual number"* recovers where a bare parser error
  doesn't.
- **The calculator cannot execute code.** It's a hand-written recursive-descent parser over
  `+ - * / % ^` and parentheses — no `eval`, no scripting engine. Any tool you expose is
  something a model can be talked into calling with hostile arguments, so a "calculator" built
  on `eval` is a remote code execution hole.

This is a deliberate step up from "static RAG", where retrieval is hardcoded to run exactly
once before the model sees anything. Letting the model drive means it can skip retrieval for
questions that don't need it, rephrase your question into a better search query, or search
multiple times for a multi-part question.

The vector store is deliberately simple: embeddings are stored as BLOBs in a normal
SQLite table, and similarity search is a brute-force loop in C# (`VectorStore.CosineSimilarity`).
No native vector extension, nothing hidden — you can read the whole retrieval mechanism in
one file (`api/Services/VectorStore.cs`). It's fine up to tens of thousands of chunks; a real
vector database (pgvector, Qdrant, etc.) is a drop-in upgrade once you outgrow it.

Both embeddings and the answering model run locally through [Ollama](https://ollama.com) —
no API key, no cost, works offline. `OllamaChatService` is a thin, swappable wrapper; pointing
it at Claude or another hosted model instead is a small, contained change once you want better
answer quality than a small local model gives.

## Project layout

```
api/      ASP.NET Core Web API (.NET 9) — ingestion + retrieval + local LLM calls
client/   React + TypeScript (Vite) — upload UI + chat UI
```

## API

| Endpoint | Purpose |
|---|---|
| `GET /api/documents` | List ingested documents with chunk counts |
| `POST /api/documents` | Upload a PDF (multipart `file`); runs the full ingestion pipeline |
| `DELETE /api/documents/{id}` | Remove a document and its chunks |
| `POST /api/chat` | Ask a question; runs the agent loop |

`POST /api/chat` takes `{ "question": "...", "conversationId": null }`. The response carries a
`conversationId` — send it back on the next question to continue the thread, or omit it to start
fresh. History lives server-side in `ConversationStore`, so the client never has to replay tool
traffic. Sample requests are in `api/RagExample.Api.http`.

## Setup

### 1. Install Ollama and pull the two models

Download from [ollama.com](https://ollama.com) (or `winget install Ollama.Ollama`), then:

```bash
ollama pull nomic-embed-text   # embeddings
ollama pull qwen2.5:7b         # generation
```

These are the two models `api/appsettings.json` is configured for (`Ollama:EmbeddingModel`
and `Ollama:ChatModel`). A smaller generation model works if 7B is too slow on your
hardware — see *Model size dominates accuracy* below for what you give up.

Ollama runs as a background service on `http://localhost:11434` once installed.

### 2. Run the API

```bash
cd api
dotnet run
```

Runs on `http://localhost:5263`. First run creates `rag.db` (SQLite) next to the project.

### 3. Run the frontend

```bash
cd client
npm install
npm run dev
```

Runs on `http://localhost:5173`.

### 4. Try it

Open the app, upload a PDF, then ask a question about its contents. Each answer shows:

- the **tool calls** the model made, with the exact arguments it chose — watch it rewrite your
  question into a search query, or pick `calculator` over `search_documents`;
- the **source chunks** retrieved, with similarity scores.

Those two views are the fastest way to build intuition for why RAG answers are sometimes wrong
(bad retrieval, or the model never searching at all) vs. right.

## Talking to it

You can ask by voice instead of typing (🎤), and answers are read back aloud — automatically
while *Auto-play answers* is ticked, or on demand via 🔊 on any answer.

Both directions run **entirely in the browser**, through the Web Speech API:

| Direction | API | Where it runs |
|---|---|---|
| Speech → text | `SpeechRecognition` (`client/src/hooks/useVoiceRecorder.ts`) | Browser |
| Text → speech | `SpeechSynthesis` (`ChatPanel.playAnswer`) | Browser |

There is no speech endpoint on the API and no third-party voice service — no key, no cost,
nothing added to the server. That is the same trade the rest of the app makes with Ollama:
lower quality than a hosted service, but local and free.

The catch is browser support. `SpeechRecognition` is **Chrome and Edge only** — Firefox and
Safari don't implement it, so the mic button is disabled there (`useVoiceRecorder` reports
`isSupported`, rather than failing once you click). `SpeechSynthesis` is supported
everywhere. Note that Chrome's implementation sends audio to a Google service for
recognition, so it needs a network connection and isn't as local as the rest of the stack.

Swapping in a server-side speech service (Whisper via Ollama, ElevenLabs, etc.) means adding
a controller that takes an audio blob and returns text, and having `useVoiceRecorder` record
with `MediaRecorder` and POST to it instead — the rest of the UI doesn't change.

## Known gaps (deliberate — good exercises)

- **No similarity threshold — on purpose.** `Retrieval:TopK` returns the N closest chunks even
  when all N are irrelevant. The obvious fix is a minimum-score cutoff, but measured on this
  setup the scores overlap: a *relevant* chunk scored 0.448 on one question while an
  *irrelevant* one scored 0.467 on another. Cosine scores are only comparable **within** a
  single query, not as an absolute relevance bar, so a fixed cutoff silently drops good results.
  What works better is already in place: the search tool reports each score to the model and
  lets it judge (it correctly refuses to answer off-topic questions this way). Worth trying
  anyway to see the failure yourself.
- **Conversation memory is in-process.** `ConversationStore` is a dictionary in memory, so
  history dies with the process and isn't shared across instances. Redis or a table would fix it.
- **Re-ingest after changing extraction, chunking, or the embedding model.** Chunks and
  embeddings are computed at upload time, so changing `PdfTextExtractor`, `TextChunker`, or
  `Ollama:EmbeddingModel` does nothing to documents already in the database.
  `DELETE /api/documents/{id}`, then re-upload. Changing the embedding model is the harsh
  case: the new vectors usually have a different length from the stored ones, and vectors of
  different lengths can't be compared at all. `VectorStore.CosineSimilarity` scores that
  mismatch as 0 so one stale chunk can't crash every search — but those chunks are then
  invisible to retrieval until re-ingested.
- **Model size dominates accuracy.** Small models (3B) chain tools badly and misread
  layout-sensitive documents — e.g. on a resume where the employer is on the line *above* the
  job title, a 3B model pairs each title with the wrong company. 7B+ handles it. On CPU-only
  hardware this is a direct speed/accuracy trade; set `Ollama:ChatModel` in `appsettings.json`.
- **Flattening a PDF loses layout.** Two-column layouts, tables, and forms encode meaning in
  *position*, which becomes ambiguous once flattened to lines. `ContentOrderTextExtractor` does
  layout analysis to recover reading order, but it can't fully recover a table's structure.

## Things worth experimenting with

- **Chunk size/overlap** (`TextChunker.Chunk`) — smaller chunks retrieve more precisely but
  lose surrounding context; larger chunks do the opposite.
- **Top-K** (`Retrieval:TopK` in `appsettings.json`) — how many chunks each search returns.
- **Tool descriptions** (`AgentTools.Definitions`) — the text describing each tool is a
  prompt. Reword it and watch the model's tool choices change.
- **Add a third tool.** The dispatch is one `switch` in `AgentTools.ExecuteAsync` plus one entry
  in `Definitions` — e.g. a `get_today` tool, which the model needs since it has no clock.
- **Embedding model** — swap `nomic-embed-text` for another Ollama embedding model and compare
  retrieval quality.
- **Chat model** — swap `Ollama:ChatModel` for a smaller model (`llama3.2`) or a larger one
  (`qwen2.5:14b`) and watch how much tool-chaining accuracy moves with size, or point
  `OllamaChatService` at a hosted model like Claude for comparison.
- **Vector store** — replace `VectorStore`'s brute-force search with `sqlite-vec`, pgvector, or
  Qdrant once you want to see how a real vector index scales.
