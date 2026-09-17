export interface DocumentSummary {
  id: string;
  name: string;
  chunkCount: number;
  uploadedAt: string;
}

export interface SourceChunk {
  documentName: string;
  chunkIndex: number;
  text: string;
  score: number;
}

export interface ChatResponse {
  answer: string;
  sources: SourceChunk[];
}

export interface ChatTurn {
  question: string;
  answer: string;
  sources: SourceChunk[];
}
