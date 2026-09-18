namespace RagExample.Api.Services;

// Groups whole lines into chunks of roughly chunkSizeWords, with overlap.
//
// Two reasons this is line-based rather than a blind sliding window over words:
// it keeps the line structure that PdfTextExtractor worked to preserve (so a job
// title stays attached to its dates), and line breaks are a free, decent proxy for
// semantic boundaries - a chunk is far more likely to start at a heading or bullet.
public static class TextChunker
{
    public static List<string> Chunk(string text, int chunkSizeWords = 200, int overlapWords = 40)
    {
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => (Text: l, WordCount: CountWords(l)))
            .ToList();

        var chunks = new List<string>();
        if (lines.Count == 0) return chunks;

        var start = 0;
        while (start < lines.Count)
        {
            var words = 0;
            var end = start;

            // Always take at least one line, so a single line longer than the budget
            // still becomes its own chunk instead of stalling the loop.
            while (end < lines.Count && (words == 0 || words + lines[end].WordCount <= chunkSizeWords))
            {
                words += lines[end].WordCount;
                end++;
            }

            chunks.Add(string.Join('\n', lines[start..end].Select(l => l.Text)));

            if (end >= lines.Count) break;

            // Walk back over whole lines until roughly overlapWords are covered.
            // Stopping at start + 1 guarantees the window advances every iteration.
            var overlap = 0;
            var next = end;
            while (next > start + 1 && overlap + lines[next - 1].WordCount <= overlapWords)
            {
                next--;
                overlap += lines[next].WordCount;
            }

            start = next;
        }

        return chunks;
    }

    private static int CountWords(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
