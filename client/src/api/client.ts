import type { ChatResponse, DocumentSummary } from "../types";

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5263";

// The API reports failures as RFC 7807 problem details ({ title, detail, status }), but a few
// endpoints still return a bare string (e.g. "Only PDF files are supported."). Prefer the
// human-readable `detail` when there is one, and fall back to the raw body otherwise.
async function errorMessage(res: Response): Promise<string> {
  const text = await res.text();
  try {
    const body = JSON.parse(text);
    if (body && typeof body === "object" && typeof body.detail === "string") return body.detail;
  } catch {
    // not JSON - use the text as-is
  }
  return text || `Request failed with status ${res.status}`;
}

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error(await errorMessage(res));
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

export function askQuestion(question: string, conversationId: string | null): Promise<ChatResponse> {
  return fetch(`${API_BASE}/api/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question, conversationId }),
  }).then((r) => handle(r));
}

export async function deleteDocument(id: string): Promise<void> {
  const res = await fetch(`${API_BASE}/api/documents/${id}`, { method: "DELETE" });
  if (!res.ok) throw new Error(`Failed to delete document (status ${res.status})`);
}
