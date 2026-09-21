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

export interface ToolCallTrace {
  toolName: string;
  arguments: string;
  result: string;
}

export interface ChatResponse {
  answer: string;
  sources: SourceChunk[];
  toolCalls: ToolCallTrace[];
  conversationId: string;
}

export interface ChatTurn {
  // Client-side only: a stable key for React lists and for tracking which answer is
  // being spoken. Array index would shift if turns were ever removed or reordered.
  id: string;
  question: string;
  answer: string;
  sources: SourceChunk[];
  toolCalls: ToolCallTrace[];
}
