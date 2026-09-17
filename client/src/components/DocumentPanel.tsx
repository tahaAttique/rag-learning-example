import { useRef, useState } from "react";
import type { DocumentSummary } from "../types";
import { uploadDocument } from "../api/client";

interface Props {
  documents: DocumentSummary[];
  onUploaded: (doc: DocumentSummary) => void;
}

export function DocumentPanel({ documents, onUploaded }: Props) {
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  async function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;

    setUploading(true);
    setError(null);
    try {
      const doc = await uploadDocument(file);
      onUploaded(doc);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed.");
    } finally {
      setUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = "";
    }
  }

  return (
    <div className="panel">
      <h2>Documents</h2>

      <label className="upload-button">
        {uploading ? "Ingesting..." : "Upload PDF"}
        <input
          ref={fileInputRef}
          type="file"
          accept="application/pdf"
          onChange={handleFileChange}
          disabled={uploading}
          hidden
        />
      </label>

      {error && <p className="error">{error}</p>}

      <ul className="document-list">
        {documents.length === 0 && <li className="empty">No documents yet.</li>}
        {documents.map((doc) => (
          <li key={doc.id}>
            <span className="doc-name">{doc.name}</span>
            <span className="doc-meta">{doc.chunkCount} chunks</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
