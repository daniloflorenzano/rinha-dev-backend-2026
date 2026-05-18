using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Pgvector;

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
    "Username=postgres;Password=senha;Host=localhost;Port=5431;Database=postgres;Pooling=true;MaxPoolSize=15;Connection Lifetime=0;";

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();

var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);


static bool IsDbReady(NpgsqlDataSource source)
{
    try
    {
        using var connection = source.OpenConnection();
        var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM items", connection);
        using var reader = cmd.ExecuteReader();
    
        Int64 items = 0;
        while (reader.Read())
        {
            items = reader.GetInt64(0);
        }
    
        if (items == 3_000_000) return true;

        Console.WriteLine("items: " + items);
        return false;
    }
    catch (Exception e)
    {
        Console.WriteLine("Banco de dados nao esta pronto: " + e.Message);
        return false;
    }
}

builder.Services.AddHealthChecks()
    .AddCheck("DbWarmup", () => IsDbReady(dataSource) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());

var app = builder.Build();

app.MapHealthChecks("/ready");

app.MapPost("/fraud-score", async ([FromBody]Request req, [FromServices]NpgsqlDataSource source) =>
{
    const int MaxAmount = 10000;
    const int MaxInsallments = 12;
    const int AmountVsAvgRatio = 10;
    const int MaxMinutes = 1440;
    const int MaxKm = 1000;
    const int MaxTxCount24h = 20;
    const int MaxMerchantAvgAmount = 10000;

    float[] vector = new float[14];
    var requestedAt = DateTime.Parse(req.transaction.requested_at).ToUniversalTime();

    vector[0] = Limitar(req.transaction.amount, MaxAmount);
    vector[1] = Limitar(req.transaction.installments, MaxInsallments);
    vector[2] = Limitar(req.transaction.amount / req.customer.avg_amount, AmountVsAvgRatio);
    vector[3] = Limitar(requestedAt.Hour, 23);
    vector[4] = Limitar(DayOfWeekAsInt(requestedAt.DayOfWeek), 6);
    vector[5] = req.last_transaction is null ? -1 : Limitar(requestedAt.Minute, MaxMinutes);
    vector[6] = req.last_transaction is null ? -1 : Limitar(req.last_transaction.km_from_current, MaxKm);
    vector[7] = Limitar(req.terminal.km_from_home, MaxKm);
    vector[8] = Limitar(req.customer.tx_count_24h, MaxTxCount24h);
    vector[9] = req.terminal.is_online ? 1 : 0;
    vector[10] = req.terminal.card_present ? 1 : 0;
    vector[11] = req.customer.known_merchants.Contains(req.merchant.id) ? 0 : 1;
    vector[12] = MccRisk(req.merchant.mcc);
    vector[13] = Limitar(req.merchant.avg_amount, MaxMerchantAvgAmount);

    await using var connection = await dataSource.OpenConnectionAsync();
    var cmd = new NpgsqlCommand("SELECT label FROM items ORDER BY vector <-> $1 LIMIT 5", connection)
    {
        Parameters = { new() { Value = new Vector(vector)}}
    };

    var fraudNeighbors = 0f;
    await using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        var label = reader.GetString(0);
        if (string.Equals(label, "fraud"))
            fraudNeighbors++;
    }

    var score = fraudNeighbors / 5;
    var approved = score < 0.6;

    var res = new Response(approved, score);
    return Results.Ok(res);
});

app.Run();

static float Limitar(float amount, int max)
{
    var v = amount / max;
    if (v < 0.0) return 0.0f;
    if (v > 1.0) return 1.0f;

    return (float)Math.Round(v, 4);
}

static int DayOfWeekAsInt(DayOfWeek dayOfWeek)
{
    var intFromEnum = (int)dayOfWeek;

    if (intFromEnum == 0) return 6;

    return intFromEnum - 1;
}

static float MccRisk(string mcc)
{
    Dictionary<string, float> dict = new()
    {
        {"5411", 0.15f},
        {"5812", 0.30f},
        {"5912", 0.20f},
        {"5944", 0.45f},
        {"7801", 0.80f},
        {"7802", 0.75f},
        {"7995", 0.85f},
        {"4511", 0.35f},
        {"5311", 0.25f},
        {"5999", 0.50f}
    };

    if (dict.TryGetValue(mcc, out var risk))
        return risk;

    return 0.5f;
}

record Response(bool approved, float fraud_score);

record Request(
    string id,
    Transaction transaction,
    Customer customer,
    Merchant merchant,
    Terminal terminal,
    LastTransaction? last_transaction
);

record Transaction(float amount, int installments, string requested_at);
record Customer(float avg_amount, int tx_count_24h, string[] known_merchants);
record Merchant(string id, string mcc, float avg_amount);
record Terminal(bool is_online, bool card_present, float km_from_home);
record LastTransaction(string timestamp, float km_from_current);
