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

**Query** (`POST /api/chat`):
```
Question -> embed it (Ollama) -> cosine-similarity search over stored vectors (VectorStore)
         -> top matching chunks -> injected into the system prompt (RagChatService)
         -> local model (Ollama) answers using only that context
```

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

## Setup

### 1. Install Ollama and pull the two models

Download from [ollama.com](https://ollama.com) (or `winget install Ollama.Ollama`), then:

```bash
ollama pull nomic-embed-text   # embeddings
ollama pull llama3.2           # generation
```

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

Open the app, upload a PDF, then ask a question about its contents. Expand "source chunks"
under an answer to see exactly which text the model was given — this is the easiest way to
build intuition for why RAG answers are sometimes wrong (bad retrieval) vs. right (good
retrieval + good generation).

## Things worth experimenting with

- **Chunk size/overlap** (`TextChunker.Chunk`) — smaller chunks retrieve more precisely but
  lose surrounding context; larger chunks do the opposite.
- **Top-K** (`RagChatService.AskAsync`, currently 4) — how many chunks get passed to the model.
- **Embedding model** — swap `nomic-embed-text` for another Ollama embedding model and compare
  retrieval quality.
- **Chat model** — try a bigger local model (`ollama pull qwen2.5:7b`, etc.) and see how much
  answer quality improves, or point `OllamaChatService` at a hosted model like Claude for
  comparison.
- **Vector store** — replace `VectorStore`'s brute-force search with `sqlite-vec`, pgvector, or
  Qdrant once you want to see how a real vector index scales.
