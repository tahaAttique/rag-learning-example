namespace RagExample.Api.Services;

// Splits text into overlapping word-based chunks. Overlap keeps a sentence that
// spans a chunk boundary retrievable from either chunk, instead of losing context.
public static class TextChunker
{
    public static List<string> Chunk(string text, int chunkSizeWords = 200, int overlapWords = 40)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        if (words.Length == 0) return chunks;

        var start = 0;
        while (start < words.Length)
        {
            var length = Math.Min(chunkSizeWords, words.Length - start);
            chunks.Add(string.Join(' ', words.Skip(start).Take(length)));

            if (start + length >= words.Length) break;
            start += chunkSizeWords - overlapWords;
        }

        return chunks;
    }
}
