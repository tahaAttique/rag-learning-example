import { useEffect, useState } from "react";
import "./App.css";
import { DocumentPanel } from "./components/DocumentPanel";
import { ChatPanel } from "./components/ChatPanel";
import { listDocuments } from "./api/client";
import type { DocumentSummary } from "./types";

function App() {
  const [documents, setDocuments] = useState<DocumentSummary[]>([]);

  useEffect(() => {
    listDocuments()
      .then(setDocuments)
      .catch(() => setDocuments([]));
  }, []);

  return (
    <div className="app">
      <header>
        <h1>RAG Example</h1>
        <p>Upload PDFs, then ask questions grounded in their content.</p>
      </header>

      <main>
        <DocumentPanel documents={documents} onUploaded={(doc) => setDocuments((prev) => [doc, ...prev])} />
        <ChatPanel />
      </main>
    </div>
  );
}

export default App;
