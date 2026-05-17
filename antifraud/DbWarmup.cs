using System.IO.Compression;
using System.Text.Json;
using Npgsql;
using Pgvector;

namespace Antifraud;

public static class DbWarmup
{
    private static readonly string DestinationFile = Environment.GetEnvironmentVariable("REFERENCES_JSON_DESTINATION_PATH") ??
            "/home/df/Projects/rinha-2026/antifraud/references.json";

    public static async Task PrepareDatabase(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        await using var createExtensionCommand = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", connection);
        await createExtensionCommand.ExecuteNonQueryAsync();

        await using var dropExistingTableCommand = new NpgsqlCommand("DROP TABLE IF EXISTS items", connection);
        await dropExistingTableCommand.ExecuteNonQueryAsync();

        await using var createTableCommand = new NpgsqlCommand("CREATE TABLE items (id bigserial PRIMARY KEY, vector vector(14), label char(5))", connection);
        await createTableCommand.ExecuteNonQueryAsync();

        DecompressReferencesJson();

        var jsonFile = await File.ReadAllTextAsync(DestinationFile);
        var items = JsonSerializer.Deserialize<List<Item>>(jsonFile) ?? throw new Exception("Erro ao desserializar itens");

        Console.Write("Populando banco de dados");
        using var writer = await connection.BeginBinaryImportAsync("COPY items (vector, label) FROM STDIN (FORMAT BINARY)");

        var i = 0;
        foreach (var item in items)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(new Vector(item.vector));
            await writer.WriteAsync(item.label);

            if (i++ % 10000 == 0)
                Console.Write(".");
        }

        await writer.CompleteAsync();

        DeleteReferencesJson();
    }

    private static void DecompressReferencesJson()
    {
        var fileExists = File.Exists(DestinationFile);
        if (fileExists) return;

        var sourceFile = Environment.GetEnvironmentVariable("REFERENCES_JSON_GZ_SOURCE_PATH") ??
            "/home/df/Projects/rinha-2026/antifraud/references.json.gz";

        using var sourceStream = File.OpenRead(sourceFile);
        using var destinationStream = File.Create(DestinationFile);
        using var decompressionStream = new GZipStream(sourceStream, CompressionMode.Decompress);

        decompressionStream.CopyTo(destinationStream);
    }

    private static void DeleteReferencesJson()
    {
        var fileExists = File.Exists(DestinationFile);
        if (!fileExists) return;

        File.Delete(DestinationFile);
    }

    public record Item(float[] vector, string label);
}