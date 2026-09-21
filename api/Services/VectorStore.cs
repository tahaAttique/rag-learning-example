using System.Globalization;
using Microsoft.Data.Sqlite;
using RagExample.Api.Models;

namespace RagExample.Api.Services;

// A minimal vector store built on plain SQLite: embeddings are stored as BLOBs
// and similarity search is done in-process with cosine similarity. No native
// vector extension required - this is the whole retrieval mechanism laid bare,
// which is the point of building it while learning rather than importing it.
public class VectorStore
{
    private readonly string _connectionString;

    public VectorStore(IConfiguration config)
    {
        var dbPath = config["Storage:SqlitePath"] ?? "rag.db";
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                UploadedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Chunks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DocumentId TEXT NOT NULL REFERENCES Documents(Id),
                ChunkIndex INTEGER NOT NULL,
                Text TEXT NOT NULL,
                Embedding BLOB NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<string> AddDocumentAsync(string name)
    {
        var id = Guid.NewGuid().ToString("N");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Documents (Id, Name, UploadedAt) VALUES ($id, $name, $uploadedAt)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$uploadedAt", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();

        return id;
    }

    public async Task AddChunkAsync(string documentId, int chunkIndex, string text, float[] embedding)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Chunks (DocumentId, ChunkIndex, Text, Embedding)
            VALUES ($docId, $idx, $text, $embedding)
            """;
        cmd.Parameters.AddWithValue("$docId", documentId);
        cmd.Parameters.AddWithValue("$idx", chunkIndex);
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$embedding", ToBytes(embedding));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteDocumentAsync(string documentId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Both deletes in one transaction: a document row surviving without its chunks
        // (or the reverse) would leave the store describing documents it can't search.
        await using var tx = await conn.BeginTransactionAsync();

        var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            DELETE FROM Chunks WHERE DocumentId = $id;
            DELETE FROM Documents WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", documentId);

        var affected = await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();

        return affected > 0;
    }

    public async Task<List<DocumentSummary>> ListDocumentsAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.Id, d.Name, d.UploadedAt, COUNT(c.Id)
            FROM Documents d
            LEFT JOIN Chunks c ON c.DocumentId = d.Id
            GROUP BY d.Id
            ORDER BY d.UploadedAt DESC
            """;

        var results = new List<DocumentSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new DocumentSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(3),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return results;
    }

    public async Task<List<SourceChunk>> SearchAsync(float[] queryEmbedding, int topK = 4)
    {
        var all = await GetAllChunksAsync();

        return all
            .Select(c => new SourceChunk(c.DocumentName, c.ChunkIndex, c.Text, CosineSimilarity(queryEmbedding, c.Embedding)))
            .OrderByDescending(c => c.Score)
            .Take(topK)
            .ToList();
    }

    private async Task<List<(string DocumentName, int ChunkIndex, string Text, float[] Embedding)>> GetAllChunksAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.Name, c.ChunkIndex, c.Text, c.Embedding
            FROM Chunks c
            JOIN Documents d ON d.Id = c.DocumentId
            """;

        var results = new List<(string, int, string, float[])>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                FromBytes((byte[])reader["Embedding"])));
        }

        return results;
    }

    public static double CosineSimilarity(float[] a, float[] b)
    {
        // Different embedding models produce different vector lengths, and chunks are
        // embedded at upload time - so switching Ollama:EmbeddingModel leaves older chunks
        // stored at the old dimension. Scoring those as 0 makes them simply never match,
        // instead of throwing and taking every search down with them. Re-ingest to fix.
        if (a.Length != b.Length) return 0;

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static byte[] ToBytes(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}
