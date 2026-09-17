import type { ChatResponse, DocumentSummary } from "../types";

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5263";

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Request failed with status ${res.status}`);
  }
  return res.json() as Promise<T>;
}

export function listDocuments(): Promise<DocumentSummary[]> {
  return fetch(`${API_BASE}/api/documents`).then((r) => handle(r));
}

export function uploadDocument(file: File): Promise<DocumentSummary> {
  const formData = new FormData();
  formData.append("file", file);
  return fetch(`${API_BASE}/api/documents`, {
    method: "POST",
    body: formData,
  }).then((r) => handle(r));
}

export function askQuestion(question: string): Promise<ChatResponse> {
  return fetch(`${API_BASE}/api/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question }),
  }).then((r) => handle(r));
}
