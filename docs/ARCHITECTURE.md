# Architecture & Code Walkthrough

This document explains how the RAG Example works, file by file and request by request. The
[README](../README.md) covers the *why* and how to run it; this covers the *how*: what every
piece of code does, how data moves between pieces, and where the sharp edges are.

**Contents**

1. [What the app does](#1-what-the-app-does)
2. [Big picture](#2-big-picture)
3. [Concepts you need](#3-concepts-you-need)
4. [Repository layout](#4-repository-layout)
5. [Backend startup and configuration](#5-backend-startup-and-configuration)
6. [Flow A: ingesting a PDF](#6-flow-a-ingesting-a-pdf)
7. [Flow B: answering a question (the agent loop)](#7-flow-b-answering-a-question-the-agent-loop)
8. [Storage](#8-storage)
9. [The frontend](#9-the-frontend)
10. [Voice input and output](#10-voice-input-and-output)
11. [Design decisions](#11-design-decisions)
12. [Limitations and gotchas](#12-limitations-and-gotchas)
13. [Extending it](#13-extending-it)
14. [Troubleshooting](#14-troubleshooting)

---

## 1. What the app does

You upload PDFs, then ask questions about them in a chat box (typed or spoken). An LLM answers
using passages retrieved from your PDFs, and can also do exact arithmetic. The chat and
embedding models are Google's, reached over Gemini's OpenAI-compatible HTTP API; the data lives
in a local SQLite file and speech uses the browser's built-in APIs. You need a Gemini API key
(see the README) — and note that both uploaded document text and your questions are sent to
Google.

The UI shows its work: every answer can be expanded to reveal the **tool calls** the model made
(with the exact arguments it chose) and the **source chunks** it retrieved (with similarity
scores). That transparency is the point: it's a learning project.

## 2. Big picture

```
 Browser (React, :5173)                 ASP.NET Core API (:5263)              Gemini (HTTPS)
┌──────────────────────┐   HTTP/JSON   ┌───────────────────────────┐   HTTP     ┌─────────────────────┐
│ DocumentPanel        │──────────────▶│ DocumentsController       │───────────▶│ gemini-embedding-001│
│ ChatPanel            │               │ ChatController            │            │  (embeddings)       │
│  ├ useVoiceRecorder  │◀──────────────│   │                       │◀───────────│                     │
│  └ SpeechSynthesis   │               │   ▼                       │            │ gemini-3.5-flash-…  │
└──────────────────────┘               │ IngestionService          │───────────▶│  (chat + tools)     │
   Web Speech API                      │ RagChatService (agent)    │            └─────────────────────┘
   (runs in the browser)               │  ├ AgentTools             │             /v1beta/openai/
                                       │  ├ OpenAiCompatibleChat…  │            ┌─────────────────────┐
                                       │  └ ConversationStore(mem) │───────────▶│ rag.db (SQLite)     │
                                       │ VectorStore               │◀───────────│ Documents,Chunks    │
                                       └───────────────────────────┘            └─────────────────────┘
```

| Layer | Technology | Role |
|---|---|---|
| Frontend | React 19, TypeScript, Vite | Upload UI, chat UI, voice in/out |
| Backend | ASP.NET Core Web API, .NET 9 | Ingestion, retrieval, agent loop |
| PDF parsing | PdfPig (`UglyToad.PdfPig`) | Text extraction with layout analysis |
| Vector storage | SQLite via `Microsoft.Data.Sqlite` | Chunks + embeddings as BLOBs |
| Models | Gemini `gemini-embedding-001`, `gemini-3.5-flash-lite` | Embeddings; chat with tool calling |
| Speech | Web Speech API | Speech-to-text and text-to-speech in the browser |

The two model calls go to Gemini's **OpenAI-compatible** endpoint rather than its native API, so
nothing in the code is Google-specific beyond the default URL and model names — pointing it at
Groq, Cerebras or OpenRouter is a configuration change.

There are exactly two flows through the backend, and they share only the database:

- **Flow A (write path):** PDF in → chunks + embeddings stored.
- **Flow B (read path):** question in → agent loop → answer out.

## 3. Concepts you need

| Term | Meaning in this project |
|---|---|
| **Chunk** | A ~200-word slice of a PDF. The unit that gets embedded, stored, and retrieved. LLMs and embedding models work on limited text, and retrieving a small relevant slice beats sending a whole document. |
| **Embedding** | A `float[]` (3,072 numbers for `gemini-embedding-001`) produced by an embedding model. Texts with similar meaning get vectors pointing in similar directions. |
| **Cosine similarity** | The score for "how similar are two vectors": the cosine of the angle between them. 1 = same direction, 0 = unrelated. Implemented in `VectorStore.CosineSimilarity`. |
| **Retrieval** | Embed the question, score it against every stored chunk, keep the top K. |
| **RAG** | Retrieval-Augmented Generation: put the retrieved chunks in front of the LLM so it answers from your documents instead of from memory. |
| **Tool / function calling** | You describe functions to the LLM as JSON Schema. Instead of answering, it may reply "call `search_documents` with `{query: ...}`". *Your code* runs it and sends the result back. The LLM never executes anything itself. |
| **Agent loop** | Repeat "ask model → run any tools it requested → feed results back" until the model answers in plain text. |
| **Static RAG vs agentic RAG** | Static: retrieval always runs once, before the model sees the question. Agentic (this app): retrieval is a tool the model *chooses* to call, zero or more times. |

## 4. Repository layout

```
rag-learning-example/
├── RagExample.sln
├── README.md                       Conceptual overview + setup
├── docs/ARCHITECTURE.md            This file
├── api/                            ASP.NET Core Web API
│   ├── Program.cs                  Composition root: DI, CORS, HTTP clients
│   ├── appsettings.json            Model names, TopK, DB path
│   ├── RagExample.Api.http         Sample requests (REST Client / Rider)
│   ├── Controllers/
│   │   ├── DocumentsController.cs  GET/POST/DELETE /api/documents
│   │   └── ChatController.cs       POST /api/chat
│   ├── Models/Dtos.cs              Request/response records
│   └── Services/
│       ├── PdfTextExtractor.cs     PDF → text
│       ├── TextChunker.cs          text → overlapping chunks
│       ├── IChatService.cs         Chat contract + shared message/tool types
│       ├── OpenAiCompatibleEmbeddingService.cs  text → vector (batched)
│       ├── VectorStore.cs          SQLite storage + cosine search
│       ├── IngestionService.cs     Orchestrates Flow A
│       ├── OpenAiCompatibleChatService.cs  One chat round-trip with tools
│       ├── AgentTools.cs           Tool definitions + dispatch
│       ├── Calculator.cs           Safe arithmetic parser
│       ├── ConversationStore.cs    In-memory dialogue history
│       └── RagChatService.cs       The agent loop (Flow B)
└── client/                         React + Vite
    └── src/
        ├── App.tsx                 Layout; owns the document list
        ├── types.ts                TS mirrors of the API DTOs
        ├── api/client.ts           fetch wrappers
        ├── components/
        │   ├── DocumentPanel.tsx   Upload / list / delete
        │   └── ChatPanel.tsx       Chat, tool-call/source display, TTS
        ├── hooks/useVoiceRecorder.ts  SpeechRecognition wrapper
        └── speech.d.ts             Type declarations for the Web Speech API
```

## 5. Backend startup and configuration

### [Program.cs](../api/Program.cs)

Builds the DI container and HTTP pipeline:

- **Controllers + OpenAPI** (OpenAPI mapped in Development only).
- **CORS** policy `AllowReactDev`: only `http://localhost:5173` may call the API from a browser.
  Deploying the frontend elsewhere means changing this origin.
- **Two typed `HttpClient`s**, both pointing at `Chat:BaseUrl` with a 2-minute timeout. Each
  service attaches the `Authorization: Bearer` header in its own constructor, so the key is read
  from configuration in exactly one place per client.
- **Service lifetimes:**

| Service | Lifetime | Why |
|---|---|---|
| `VectorStore` | Singleton | Holds only a connection string; creates the schema once at startup. Opens a fresh SQLite connection per operation. |
| `ConversationStore` | Singleton | Must outlive individual requests, since it *is* the conversation memory. |
| `IngestionService`, `AgentTools`, `RagChatService` | Scoped | Stateless per-request orchestrators. |

### [appsettings.json](../api/appsettings.json)

| Key | Default | Used by | Effect |
|---|---|---|---|
| `Chat:BaseUrl` | `https://generativelanguage.googleapis.com/v1beta/openai/` | `Program.cs` | Provider endpoint (must end in `/`) |
| `Chat:Model` | `gemini-3.5-flash-lite` | `OpenAiCompatibleChatService` | Model that answers and calls tools |
| `Chat:EmbeddingModel` | `gemini-embedding-001` | `OpenAiCompatibleEmbeddingService` | Model that produces vectors |
| `Chat:ApiKey` | *(none)* | both services | **Secret.** Set via `dotnet user-secrets`, never in this file |
| `Retrieval:TopK` | `4` | `AgentTools` | Chunks returned per search |
| `Storage:SqlitePath` | `rag.db` | `VectorStore` | SQLite file (relative to the working dir) |

`Chat:ApiKey` is the only secret and is deliberately absent from `appsettings.json`, which is
committed. Both services throw a message naming the `dotnet user-secrets` command if it is
missing, rather than failing later with an opaque 401.

### HTTP API ([Controllers/](../api/Controllers))

| Endpoint | Behaviour |
|---|---|
| `GET /api/documents` | Returns `DocumentSummary[]`, newest first, each with its chunk count. |
| `POST /api/documents` | Multipart field `file`. 400 if empty or not `.pdf` (checked by file *name*, not content). Max 50 MB. Runs the whole ingestion synchronously and returns the summary. |
| `DELETE /api/documents/{id}` | 204 if deleted, 404 if the id didn't exist. |
| `POST /api/chat` | Body `{ question, conversationId? }`. 400 if the question is blank. Returns `ChatResponse`. |

Controllers are deliberately thin: validate, delegate, wrap in an HTTP status.

### DTOs ([Models/Dtos.cs](../api/Models/Dtos.cs))

```csharp
DocumentSummary(Id, Name, ChunkCount, UploadedAt)
ChatRequest(Question, ConversationId?)
SourceChunk(DocumentName, ChunkIndex, Text, Score)      // one retrieved chunk
ToolCallTrace(ToolName, Arguments, Result)              // one tool call, for the UI
ChatResponse(Answer, Sources, ToolCalls, ConversationId)
```

`client/src/types.ts` mirrors these by hand, so change both together.

## 6. Flow A: ingesting a PDF

```
POST /api/documents (multipart PDF)
  │
  ▼  DocumentsController.Upload         validate: non-empty, ends with .pdf
  ▼  IngestionService.IngestPdfAsync
  │    1. PdfTextExtractor.ExtractText   PDF stream → one big string
  │    2. TextChunker.Chunk              string → List<string> (~200 words each)
  │    3. VectorStore.AddDocumentAsync   INSERT Documents row → new GUID
  │    4. EmbeddingService.EmbedAsync(chunks)  all chunks → List<float[3072]>
  │                                            (batched, ≤100 inputs per HTTP request)
  │    5. for each chunk: VectorStore.AddChunkAsync   INSERT Chunks row
  │       on any failure: DeleteDocumentAsync (rollback), rethrow
  ▼
200 { id, name, chunkCount, uploadedAt }
```

### Step 1: [PdfTextExtractor](../api/Services/PdfTextExtractor.cs)

Opens the PDF with PdfPig and, per page, calls `ContentOrderTextExtractor.GetText(page, true)`.
The choice matters: the simpler `page.Text` concatenates glyphs in raw content-stream order with
no line breaks, turning a resume or invoice into one run-on line where you can't tell which date
belongs to which job. `ContentOrderTextExtractor` runs layout analysis and keeps lines intact.

It can't read **scanned** PDFs (images of text); those yield no text and no OCR is attempted.

### Step 2: [TextChunker](../api/Services/TextChunker.cs)

Splits text into chunks of about **200 words with 40 words of overlap** (defaults of
`Chunk(text, chunkSizeWords, overlapWords)`). It works on **whole lines**, not raw words:

1. Split on `\n`, trim, drop empty lines, count words per line.
2. Starting at line `start`, keep adding lines while the running word count stays ≤ 200. The
   first line is always taken, so one oversized line still becomes its own chunk.
3. Emit the chunk. If that reached the last line, stop.
4. Otherwise choose the next start by walking *backwards* from the chunk's end over whole lines
   until ~40 words are covered (never further back than `start + 1`, which guarantees progress).
5. Repeat.

Why line-based: it preserves the structure the extractor worked for (a title stays with its
dates), and line breaks are a cheap proxy for meaning boundaries, so chunks tend to begin at a
heading or bullet. Why overlap: a fact straddling a boundary appears complete in at least one
chunk.

Example with a 200/40 setting: chunk 0 = lines 1-12 (198 words); the last ~40 words (say lines
10-12) are repeated at the top of chunk 1 = lines 10-22, and so on.

### Step 3-5: [IngestionService](../api/Services/IngestionService.cs) and [OpenAiCompatibleEmbeddingService](../api/Services/OpenAiCompatibleEmbeddingService.cs)

`EmbedAsync` POSTs `{ "model": "gemini-embedding-001", "input": [ ...chunks... ] }` to
`embeddings` and reads back `data[]`. The endpoint accepts an **array** of inputs, so a whole
document is embedded in one request (batched at 100 inputs, since one request per book would
exceed the payload limit). Against a hosted API that matters twice over: it is one round trip
instead of one per chunk, and a 40-chunk PDF costs one request against a free tier's
per-minute quota instead of forty.

Results are re-sorted by the `index` field each item carries, because the endpoint is not
obliged to return them in request order. Getting this wrong would be quiet and nasty: every
chunk would be stored with another chunk's vector, so retrieval would still return results,
just consistently the wrong ones. The service also rejects a response whose item count doesn't
match the request.

**Rollback:** the document row is inserted *before* embedding starts. If any embedding call fails
or the request is cancelled, the `catch` deletes the document and its chunks and rethrows. A
half-ingested document would be listed as present while part of its text is silently
unsearchable, which is worse than a clean failure.

## 7. Flow B: answering a question (the agent loop)

```
POST /api/chat { question, conversationId? }
  │
  ▼ ChatController.Ask                      400 if blank
  ▼ RagChatService.AskAsync
       id       = conversationId or new GUID
       messages = [system prompt] + ConversationStore.GetHistory(id) + [user question]

       repeat up to 5 rounds:
         reply = IChatService.ChatAsync(messages, tool definitions)
         ├─ no tool calls  →  save (question, answer) to history; return answer
         └─ tool calls     →  append the assistant message to `messages`
                              for each call:
                                AgentTools.ExecuteAsync(name, args)
                                record a ToolCallTrace, collect any SourceChunks
                                append a "tool" message holding the result
                              loop (model now sees the results)

       5 rounds exhausted → return a canned "try rephrasing" message
```

### [RagChatService](../api/Services/RagChatService.cs)

This is the agent. Key points:

- **System prompt** ([RagChatService.cs:18](../api/Services/RagChatService.cs#L18)) tells the
  model it has no built-in knowledge of the documents, must search before answering
  document-specific questions, must say "I don't know" if nothing relevant comes back, must use
  the calculator for *all* arithmetic with literal numbers only, and should resolve references
  like "it" against earlier turns. Each sentence exists to correct a failure seen with small
  models, so treat it as code, not decoration.
- **Round cap** (`MaxToolRounds = 5`) prevents a model that keeps calling tools from looping
  forever. One "round" is one model call, and a single reply may contain several tool calls.
- **Traces and sources** accumulate across all rounds. The UI gets every tool call and every
  chunk retrieved, even if the model searched several times.
- **History is compacted:** on completion only the user question and final assistant text are
  stored. Tool-call and tool-result messages are discarded (see below).

### [IChatService](../api/Services/IChatService.cs) and [OpenAiCompatibleChatService](../api/Services/OpenAiCompatibleChatService.cs)

`IChatService` is a one-method contract — messages + tool definitions in, one `ChatMessage` out —
and the file also holds the shared `ChatMessage`, `ToolCall` and `ToolDefinition` types. The
agent loop depends only on this interface, which is what lets the provider change without the
loop noticing.

`OpenAiCompatibleChatService` is the single implementation: one request/response turn against
`chat/completions`. It has no loop or policy; it only translates between the app's types and the
provider's JSON.

Request shape it sends:

```jsonc
{
  "model": "gemini-3.5-flash-lite",
  "messages": [
    { "role": "system", "content": "You answer questions about..." },
    { "role": "user",   "content": "What is 15% of the total on the invoice?" }
  ],
  "tools": [
    { "type": "function",
      "function": { "name": "search_documents", "description": "...", "parameters": { /* JSON Schema */ } } },
    { "type": "function", "function": { "name": "calculator", ... } }
  ]
}
```

If the model wants a tool, the reply carries `tool_calls` instead of final text — note it sits
under `choices[0].message`, and that `arguments` is a **JSON string**, not an object:

```jsonc
{ "choices": [ { "message": { "role": "assistant", "content": null,
    "tool_calls": [ { "id": "call_1", "type": "function",
                      "function": { "name": "search_documents",
                                    "arguments": "{\"query\":\"invoice total\"}" },
                      "extra_content": { "google": { "thought_signature": "..." } } } ] } } ] }
```

Three details the parsing has to get right:

- **`arguments` is a string.** It is parsed with `JsonDocument.Parse` and the root element
  `Clone()`d, since the owning document is disposed when the method returns.
- **`content` is `null`, not absent,** on a tool-only turn, so the code checks `ValueKind` rather
  than just presence.
- **`extra_content` must be echoed back.** Gemini attaches a *thought signature* to each tool
  call and rejects the following request with a 400 unless it comes back byte-identical. The app
  never interprets it: it is captured into `ToolCall.ProviderExtra` as an opaque `JsonElement`
  and re-emitted verbatim when the assistant message is replayed. Providers that don't use it
  simply never set it. This only matters inside one agent loop — `ConversationStore` drops tool
  messages, so a *follow-up* question never replays a tool call.

A tool result is sent back as `{ "role": "tool", "tool_call_id": "call_1", "content": "..." }`;
the id is how the model pairs a result with the request it made when it asked for several tools
at once.

### [AgentTools](../api/Services/AgentTools.cs)

Two parts: the **definitions** the model sees, and the **dispatch** that runs them.

| Tool | Argument | What runs | Character |
|---|---|---|---|
| `search_documents` | `query: string` | Embed the query → `VectorStore.SearchAsync(embedding, TopK)` → format chunks as `[doc, chunk N, similarity 0.62]\n text`, joined by `---` | Fuzzy, needs a network call + DB |
| `calculator` | `expression: string` | `Calculator.Evaluate(expression)` → `"expr = value"` | Exact, instant, pure C# |

The `description` strings in `Definitions` are the *only* thing telling the model when to use
each tool. That is prompt engineering, so rewording them changes the model's behaviour.

`ExecuteAsync` returns `(ResultText, Sources)`. `ResultText` is what the model reads; `Sources`
are the structured chunks the UI displays. Failure handling is intentionally soft:

- Empty query/expression → a plain-language message as the result.
- No chunks in the DB → `"No documents have been uploaded yet."`
- Calculator `FormatException` → an explanatory message that tells the model to *substitute
  the actual number* rather than pass a placeholder such as `2026 - x`. Errors are returned as
  results, never thrown, so the model can correct itself and retry within the round cap.
- Unknown tool name → `"Unknown tool: ..."` (models occasionally hallucinate tool names).

### [Calculator](../api/Services/Calculator.cs)

A hand-written **recursive-descent parser**, not `eval`. Grammar (lowest to highest precedence):

```
expression := term   (('+' | '-') term)*
term       := factor (('*' | '/' | '%') factor)*
factor     := ('-' | '+') factor | primary ('^' factor)?
primary    := '(' expression ')' | number
```

Each rule is one method that calls the next-tighter rule, which is how precedence falls out of the
code shape. `^` is right-associative (`2^3^2 = 2^9`), and because unary minus is parsed in
`factor` before `^`, `-2^2` evaluates as `-(2^2) = -4`. Numbers are digits and `.` only (no
scientific notation, no variables, no functions). Because it can only ever produce a number, a
prompt-injected or hostile argument cannot execute code, which is the security property that
matters for any model-facing tool.

### [ConversationStore](../api/Services/ConversationStore.cs)

A `ConcurrentDictionary<conversationId, List<ChatMessage>>`.

- Keeps only user/assistant messages, capped at **20 messages (10 exchanges)**; older ones fall
  off the front.
- Why drop tool messages: a single search result is hundreds of words; keeping them would fill
  the model's context window in a few questions while rarely being useful later.
- `GetHistory` returns a copy under a lock, and `Append` mutates under a lock, so concurrent
  requests on one conversation don't corrupt the list.
- Memory only: lost on restart, not shared between server instances.

### Worked example

> Documents: `q3-report.pdf`. Question: *"What was Q3 revenue, and what is 8% of it?"*

| Round | Model does | App does |
|---|---|---|
| 1 | Replies with `tool_calls: [search_documents {query: "Q3 revenue"}]` | Embeds the query, cosine-scores all chunks, returns the top 4 as the tool result; records a trace + sources |
| 2 | Reads the chunks, finds "$1,250,000", replies with `tool_calls: [calculator {expression: "1250000 * 0.08"}]` | Evaluates → `1250000 * 0.08 = 100000` |
| 3 | Replies with plain text: "Q3 revenue was $1.25M; 8% of it is $100,000." | No tool calls, so the loop ends. Stores the question + this answer in history; returns answer, 1 source set, 2 traces, conversation id |

## 8. Storage

### Schema ([VectorStore.cs](../api/Services/VectorStore.cs))

Created with `CREATE TABLE IF NOT EXISTS` at startup:

```sql
Documents(Id TEXT PRIMARY KEY, Name TEXT NOT NULL, UploadedAt TEXT NOT NULL)
Chunks   (Id INTEGER PRIMARY KEY AUTOINCREMENT,
          DocumentId TEXT NOT NULL REFERENCES Documents(Id),
          ChunkIndex INTEGER NOT NULL,
          Text       TEXT NOT NULL,
          Embedding  BLOB NOT NULL)
```

There's no migration system; schema changes mean editing `Initialize()` and deleting `rag.db`.

### Embedding serialisation

`float[]` ↔ `byte[]` by raw memory copy (`Buffer.BlockCopy`): 3,072 floats × 4 bytes = 12,288
bytes per chunk with `gemini-embedding-001`. Compact and fast, but it assumes the same machine
endianness that wrote it.

That dimension is worth noticing: it is 4× the 768 a small local embedding model produces, so
both the database and the per-query cosine loop are 4× larger. Brute-force search cost is
O(chunks × dimensions), and this quadruples the second factor.

### Search

`SearchAsync(queryEmbedding, topK)`:

1. **Load every chunk** (name, index, text, embedding) with a `JOIN` on Documents.
2. Compute `CosineSimilarity(query, chunk)` for each.
3. Sort descending, take `topK`.

`CosineSimilarity` = `dot(a,b) / (‖a‖·‖b‖)`, accumulated in `double`. Two guards return `0`
instead of throwing: mismatched vector lengths (chunks stored under a *different embedding
model*) and zero-magnitude vectors.

This is O(N) in the number of chunks *per query*, including reading all embeddings out of SQLite.
Fine for a learning project, and a vector index is the fix when it stops being fine.

### Deletion

`DeleteDocumentAsync` deletes the chunks and the document row in one transaction, so the store
never contains a document without chunks or chunks without a document. It returns `true` if any
row was affected.

### Connections

Every method opens and disposes its own `SqliteConnection`. SQLite handles this fine for a
single-process app and it avoids sharing connection state across threads.

## 9. The frontend

### Component tree and state

```
App                       state: documents[]
├── DocumentPanel         props: documents, onUploaded, onDeleted
│                         state: uploading, deletingId, error
└── ChatPanel             state: turns[], question, loading, autoPlay,
                                 playingId, error, conversationId
    └── useVoiceRecorder  state: isRecording, error (+ refs to the recognizer)
```

`App` owns the document list (loaded once on mount, and updated locally on upload/delete without
refetching). `ChatPanel` owns the whole conversation and is independent of the document list;
it doesn't need to know what's uploaded because the server-side model decides what to search.

### [api/client.ts](../client/src/api/client.ts)

Four `fetch` wrappers (`listDocuments`, `uploadDocument`, `askQuestion`, `deleteDocument`). The
base URL is `VITE_API_BASE_URL` or `http://localhost:5263`. The shared `handle<T>` turns any
non-2xx response into a thrown `Error` carrying the response body, which is how server messages
like "Only PDF files are supported." reach the UI.

### [DocumentPanel](../client/src/components/DocumentPanel.tsx)

A hidden `<input type="file" accept="application/pdf">` behind a styled label. On selection it
uploads, calls `onUploaded`, and **always** clears the input's value in `finally`. Otherwise
re-selecting the same file wouldn't fire `onChange`. Delete has per-row pending state.

### [ChatPanel](../client/src/components/ChatPanel.tsx)

`ask(text)`:

1. Ignores blank input or if a request is in flight.
2. Calls `askQuestion(text, conversationId)`.
3. Stores the returned `conversationId` (so the next question continues the thread).
4. Appends a `ChatTurn { id, question, answer, sources, toolCalls }`, where `id` is a client-side
   UUID used as the React key and to track which answer is being read aloud.
5. If *Auto-play answers* is on, speaks the answer.

Each turn renders the question, a collapsible **tool calls** section (name, arguments, result),
the answer with a 🔊 button, and a collapsible **sources** section (document · chunk · score).
"New conversation" clears the turns, resets `conversationId` to `null` (so the server starts a
fresh history), and cancels any speech.

## 10. Voice input and output

Both directions use the browser's Web Speech API, so the server has no speech code at all.

### Speech to text: [useVoiceRecorder](../client/src/hooks/useVoiceRecorder.ts)

Wraps `SpeechRecognition` (`webkitSpeechRecognition` in Chrome/Edge). Behaviours worth knowing:

- **Non-continuous, one utterance.** The browser ends recognition itself after a pause, so
  `onend` is the *single place* a result is delivered; `stop()` only asks for an early end.
- **Hook owns all failure paths.** `onerror` sets a friendly message (mapped from codes like
  `not-allowed`, `no-speech`, `network`) and flags the attempt as failed so `onend` doesn't also
  report an "empty transcript" over it. `onResult` is only ever called with non-empty text, so the
  caller never has to distinguish "error" from "heard nothing".
- **`isSupported`** is exposed so the mic button is disabled up front on Firefox/Safari.
- **Lazy lookup** of `window.SpeechRecognition` avoids touching `window` at import time (breaks
  tests/SSR otherwise).
- **Cleanup on unmount:** handlers are detached *before* `abort()`, because `abort()` fires
  `onend`, which would otherwise update state on an unmounted component.
- Language is fixed to `en-US`.
- In `ChatPanel`, the transcript goes straight into `ask(...)`: speaking a question submits it.

[speech.d.ts](../client/src/speech.d.ts) supplies TypeScript types because these APIs aren't in
the standard DOM typings.

### Text to speech: `ChatPanel.playAnswer`

Creates a `SpeechSynthesisUtterance`, cancels anything already speaking, and speaks. The
`onerror` handler ignores `interrupted`/`canceled`, which are what the previous utterance reports
when *we* cancelled it. Speech synthesis is a global queue, so it's cancelled on unmount and on
"New conversation".

Chrome's recognizer sends audio to a Google service, so voice *input* needs a network connection
even though the rest of the stack is local.

## 11. Design decisions

| Decision | Reasoning |
|---|---|
| Agentic loop instead of always retrieving first | The model can skip retrieval, rewrite the question into a better query, or search several times. Costs extra LLM round-trips. |
| Line-based chunking | Preserves layout structure; boundaries land at natural breaks. |
| Chunk overlap | Facts on a boundary survive intact in one chunk. |
| SQLite + brute-force cosine | The entire retrieval mechanism is readable in one file. No native extension. |
| Hosted Gemini for chat *and* embeddings | Fast and far more accurate than a small local model, at the cost of an API key and sending document text off the machine. |
| Talking to the OpenAI-compatible endpoint, not Gemini's native API | One wire format covers Groq, Cerebras, OpenRouter and others, so the provider is a config change rather than a rewrite. |
| Opaque `ProviderExtra` passthrough on tool calls | Carries Gemini's thought signature without the agent loop knowing what it is, so no provider quirk leaks into shared code. |
| Tool errors returned as results | The model can read the error and retry. Exceptions would abort the whole request. |
| Hand-written calculator | No code-execution surface. |
| Tool traces + sources returned to the UI | Makes wrong answers diagnosable: bad retrieval vs. model never searched vs. model misread. |
| Drop tool messages from history | Context-window budget. |
| Rollback on failed ingestion | No partially searchable documents. |
| Server-side conversation memory | The client sends only `conversationId`, not the transcript. |
| Similarity scores shown to the model, no fixed cutoff | Scores are only comparable *within* one query, so a threshold discards good hits (see README). The model judges relevance instead. |
| Web Speech API for voice | Zero server code, key, or cost. |

## 12. Limitations and gotchas

**Documented in the README** (see *Known gaps* there): no similarity threshold, in-memory
conversations, re-ingest after changing extraction/chunking/embedding model, small models
chaining tools badly, layout loss on tables and columns.

**Additional things noticed while reading the code:**

- **Scanned/image-only PDFs ingest "successfully" with 0 chunks.** No text → no chunks → a
  document appears in the list that can never be retrieved. There is no OCR and no warning.
- **`.pdf` check is by filename only.** A non-PDF renamed `x.pdf` reaches PdfPig and fails there
  with a 500 instead of a clean 400.
- **Upload is one long synchronous request.** Embedding is batched, so a typical PDF is one or
  two HTTP calls rather than one per chunk, but the browser still waits with no progress
  indication. (The 2-minute timeout covers the whole call, including any retries.)
- **Free-tier quotas are small, and every question spends several requests.** One question is at
  least one embedding call plus two chat calls (ask → search → answer), and one more chat call per
  extra tool round. A free model capped at 20 requests/day — `gemini-3.6-flash` was — therefore
  answers about 7 questions a day. Quotas are per model, which is why the default is a lite
  model; check yours in [AI Studio](https://ai.dev/rate-limit), since Google no longer publishes
  free-tier numbers.
- **Retries cover blips, not outages.** `RetryTransientHandler` retries 429/502/503/504 up to 5
  times (1s, 2s, 4s, 8s, honouring `Retry-After`), so an overload spike is absorbed invisibly.
  A *daily* quota is deliberately not retried — it stays exhausted until tomorrow — and a provider
  that stays down for the full ~15s still fails the request (and rolls back an upload).
- **Every question costs two network round trips minimum**, so latency is dominated by the
  provider, not by local compute.
- **Search reads the entire `Chunks` table on every query.** Latency and memory grow linearly
  with the corpus.
- **Retrieval spans all documents.** There's no way to scope a question to one document, and the
  model's `search_documents` tool has no document filter.
- **`Sources` can contain duplicates** if the model runs overlapping searches in one turn; the
  UI lists them as returned.
- **A reply with both text and tool calls** has its text kept in the running message list but
  never shown, since only the final no-tool reply becomes the answer.
- **Calculator edge cases:** division by zero yields `Infinity` and modulo by zero `NaN`, both
  reported as normal results. Extremely deep parentheses nesting recurses per level; a
  pathological input could exhaust the stack (a crash .NET can't catch). The parser is fine
  for realistic model output, but cap input length before exposing it to untrusted callers.
- **No authentication, no rate limiting.** The API trusts anything that passes CORS (which only
  restricts *browsers*, not curl). Fine on localhost, not on a network.
- **Two documents with the same file name** are allowed and indistinguishable to the model,
  which sees only `[name, chunk N]`.
- **Model memory of "today":** the model has no clock; date-relative questions need a tool
  (see the `get_today` suggestion below).

## 13. Extending it

### Add a tool (e.g. `get_today`)

1. Add a `ToolDefinition` to `AgentTools.Definitions` with a precise description and a JSON Schema
   for its parameters (an empty `properties` object if it takes none).
2. Add a `case "get_today":` to the `switch` in `AgentTools.ExecuteAsync` returning
   `(DateTime.Now.ToString("yyyy-MM-dd"), [])`.
3. Mention it in the system prompt if the model needs nudging.

Nothing else changes: the loop, wire format, and UI trace display are generic.

### Swap the chat model or provider

Change `Chat:Model`. The model **must support tool calling**, or the agent loop degrades to a
plain chatbot that can never search. For a different provider, change `Chat:BaseUrl` and
`Chat:ApiKey` too — anything speaking the OpenAI `chat/completions` format works without code
changes. For a provider that doesn't (Anthropic's native API, say), write a second
`IChatService` and register it in `Program.cs`; nothing else in the app depends on the wire
format.

### Swap the embedding model

Change `Chat:EmbeddingModel`, then **delete and re-upload every document**. Old vectors have the
old length and score 0 against new queries, so they go silently invisible rather than erroring.

### Tune retrieval

`Retrieval:TopK` (more context vs. more noise), `TextChunker.Chunk` size/overlap (precision vs.
context; requires re-ingest).

### Scale the vector store

Replace `SearchAsync` with `sqlite-vec`, pgvector, or Qdrant. The rest of the app only sees
`VectorStore`'s public methods.

### Persist conversations

Implement the same two methods (`GetHistory`, `Append`) over Redis or a table.

## 14. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Any call 500s with "Chat:ApiKey is not configured" | The key was never set, or was set for a different project | `cd api && dotnet user-secrets set "Chat:ApiKey" "<key>"` |
| 500 wrapping a 401/403 from the provider | Key rejected, revoked, or wrong provider's key | Re-issue the key in Google AI Studio |
| 500 wrapping a 404 "model … is not available" | Model name retired or renamed | Update `Chat:Model`; the provider's error names the replacement |
| 503 "rate-limiting requests" | Per-minute limit still tripped after 5 retries | Wait a minute and retry |
| 503 "daily quota … used up" | Per-day free-tier quota exhausted for this model (returned immediately, not retried) | Wait for the daily reset, set `Chat:Model` to another model (quotas are per model), or enable billing |
| 503 "overloaded … even after several retries" | Provider outage or sustained demand spike | Try again in a minute; the API log shows each retry |
| 400 "missing a thought_signature" | A tool call was replayed without its `extra_content` | `ToolCall.ProviderExtra` must round-trip verbatim — see §7 |
| Browser console: CORS error | Frontend not on `localhost:5173` | Run `npm run dev`, or update the origin in `Program.cs` |
| Frontend can't reach API | API isn't on `:5263` | Check `launchSettings.json`, or set `VITE_API_BASE_URL` |
| Answer says it doesn't know, but the PDF has it | Bad retrieval (check the *sources* scores and the tool-call *query*), or the doc has 0 chunks | Expand the trace; rephrase; check the chunk count in the document list |
| Model never calls a tool | Model doesn't support tool calling | Use a tool-capable `Chat:Model` |
| Model makes up numbers | It skipped the calculator | Strengthen the calculator description/system prompt; use a larger model |
| Documents "disappeared" from search after changing models | Embedding dimension mismatch (score 0) | Delete and re-upload |
| Mic button disabled | Not Chrome/Edge | Use Chrome or Edge |
| Mic says access blocked | Browser permission denied | Allow the microphone in site settings |
| Chat very slow | Network latency, or the model is chaining several tool rounds | Expand the tool-call trace to count the rounds; try a faster `Chat:Model` |
| Follow-up loses context after restart | `ConversationStore` is in memory | Expected; start a new conversation |

Fastest debugging loop: open a turn's **tool calls** and **sources** panels. They show whether the
failure was retrieval, model behaviour, or the model misreading good context.
