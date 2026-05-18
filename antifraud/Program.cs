using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Pgvector;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                       "Username=postgres;Password=senha;Host=localhost;Port=5431;Database=postgres;Pooling=true;MaxPoolSize=50;Connection Lifetime=0;";

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();

var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);

builder.Services.AddHealthChecks()
    .AddCheck("DbWarmup", () => IsDbReady(dataSource) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());

var app = builder.Build();

app.MapHealthChecks("/ready");

app.MapPost("/fraud-score", async (Request req, [FromServices] NpgsqlDataSource source) =>
{
    const int timeLimitMs = 1_950;
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeLimitMs));
    try
    {
        var res = await Fetch(req, source, cts.Token);
        return Results.Ok(res);
    }
    catch (OperationCanceledException)
    {
        return Results.Ok(new Response(true, 0.0f));
    }
});

app.Run();
return;

static bool IsDbReady(NpgsqlDataSource source)
{
    try
    {
        using var connection = source.OpenConnection();
        using var cmd = new NpgsqlCommand("SELECT 1 FROM items WHERE id = 3000000", connection);
        return cmd.ExecuteScalar() != null;
    }
    catch
    {
        return false;
    }
}

static async Task<Response> Fetch(Request req, NpgsqlDataSource source, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();

    const int MaxAmount = 10000;
    const int MaxInsallments = 12;
    const int AmountVsAvgRatio = 10;
    const int MaxMinutes = 1440;
    const int MaxKm = 1000;
    const int MaxTxCount24h = 20;
    const int MaxMerchantAvgAmount = 10000;

    float[] vector = new float[14];
    var requestedAt = DateTime.Parse(req.Transaction.RequestedAt).ToUniversalTime();

    vector[0] = Limitar(req.Transaction.Amount, MaxAmount);
    vector[1] = Limitar(req.Transaction.Installments, MaxInsallments);
    vector[2] = Limitar(req.Transaction.Amount / req.Customer.AvgAmount, AmountVsAvgRatio);
    vector[3] = Limitar(requestedAt.Hour, 23);
    vector[4] = Limitar(DayOfWeekAsInt(requestedAt.DayOfWeek), 6);
    vector[5] = req.LastTransaction is null ? -1 : Limitar(requestedAt.Minute, MaxMinutes);
    vector[6] = req.LastTransaction is null ? -1 : Limitar(req.LastTransaction.KmFromCurrent, MaxKm);
    vector[7] = Limitar(req.Terminal.KmFromHome, MaxKm);
    vector[8] = Limitar(req.Customer.TxCount24h, MaxTxCount24h);
    vector[9] = req.Terminal.IsOnline ? 1 : 0;
    vector[10] = req.Terminal.CardPresent ? 1 : 0;
    vector[11] = req.Customer.KnownMerchants.Contains(req.Merchant.Id) ? 0 : 1;
    vector[12] = MccRisk(req.Merchant.Mcc);
    vector[13] = Limitar(req.Merchant.AvgAmount, MaxMerchantAvgAmount);

    await using var connection = await source.OpenConnectionAsync(ct);
    await using var cmd = new NpgsqlCommand("SELECT label FROM items ORDER BY vector <-> $1 LIMIT 5", connection)
    {
        Parameters = { new() { Value = new Vector(vector) } }
    };

    if (!cmd.IsPrepared)
        await cmd.PrepareAsync(ct);

    var fraudNeighbors = 0f;
    await using var reader = await cmd.ExecuteReaderAsync(ct);

    while (await reader.ReadAsync(ct))
    {
        if (reader.GetString(0).Trim() == "fraud")
            fraudNeighbors++;
    }

    var score = fraudNeighbors / 5;
    return new Response(score < 0.6, score);
}

static float Limitar(float amount, int max)
{
    var v = amount / max;
    if (v < 0.0) return 0.0f;
    if (v > 1.0) return 1.0f;
    return (float)Math.Round(v, 4);
}

static int DayOfWeekAsInt(DayOfWeek dayOfWeek) => dayOfWeek == 0 ? 6 : (int)dayOfWeek - 1;

static float MccRisk(string mcc) => mcc switch
{
    "5411" => 0.15f,
    "5812" => 0.30f,
    "5912" => 0.20f,
    "5944" => 0.45f,
    "7801" => 0.80f,
    "7802" => 0.75f,
    "7995" => 0.85f,
    "4511" => 0.35f,
    "5311" => 0.25f,
    "5999" => 0.50f,
    _ => 0.5f
};

public record Response(
    bool Approved,
    [property: JsonPropertyName("fraud_score")]
    float FraudScore);

public record Request(
    string Id,
    Transaction Transaction,
    Customer Customer,
    Merchant Merchant,
    Terminal Terminal,
    [property: JsonPropertyName("last_transaction")]
    LastTransaction? LastTransaction
);

public record Transaction(
    float Amount,
    int Installments,
    [property: JsonPropertyName("requested_at")]
    string RequestedAt);

public record Customer(
    [property: JsonPropertyName("avg_amount")]
    float AvgAmount,
    [property: JsonPropertyName("tx_count_24h")]
    int TxCount24h,
    [property: JsonPropertyName("known_merchants")]
    string[] KnownMerchants);

public record Merchant(
    string Id,
    string Mcc,
    [property: JsonPropertyName("avg_amount")]
    float AvgAmount);

public record Terminal(
    [property: JsonPropertyName("is_online")]
    bool IsOnline,
    [property: JsonPropertyName("card_present")]
    bool CardPresent,
    [property: JsonPropertyName("km_from_home")]
    float KmFromHome);

public record LastTransaction(
    string Timestamp,
    [property: JsonPropertyName("km_from_current")]
    float KmFromCurrent);

[JsonSerializable(typeof(Request))]
[JsonSerializable(typeof(Response))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
