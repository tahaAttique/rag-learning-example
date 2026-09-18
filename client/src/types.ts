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
  question: string;
  answer: string;
  sources: SourceChunk[];
  toolCalls: ToolCallTrace[];
}
