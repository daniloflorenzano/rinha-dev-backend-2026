using Antifraud;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
    "Username=postgres;Password=senha;Host=localhost;Port=5431;Database=postgres;Pooling=true;MaxPoolSize=15;Connection Lifetime=0;";

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
await using var dataSource = dataSourceBuilder.Build();

await DbWarmup.PrepareDatabase(dataSource);

app.MapGet("/ready", () =>
{
    return Results.Ok();
});

app.MapPost("/fraud-score", ([FromBody]Request req) =>
{
    const int MaxAmount = 10000;
    const int MaxInsallments = 12;
    const int AmountVsAvgRatio = 10;
    const int MaxMinutes = 1440;
    const int MaxKm = 1000;
    const int MaxTxCount24h = 20;
    const int MaxMerchantAvgAmount = 10000;

    double[] vector = new double[14];
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

    var res = new Response(true, 0.8);
    return Results.Ok(res);
});

app.Run();

static double Limitar(double amount, int max)
{
    var v = amount / max;
    if (v < 0.0) return 0.0;
    if (v > 1.0) return 1.0;

    return Math.Round(v, 4);
}

static int DayOfWeekAsInt(DayOfWeek dayOfWeek)
{
    var intFromEnum = (int)dayOfWeek;

    if (intFromEnum == 0) return 6;

    return intFromEnum - 1;
}

static double MccRisk(string mcc)
{
    Dictionary<string, double> dict = new()
    {
        {"5411", 0.15},
        {"5812", 0.30},
        {"5912", 0.20},
        {"5944", 0.45},
        {"7801", 0.80},
        {"7802", 0.75},
        {"7995", 0.85},
        {"4511", 0.35},
        {"5311", 0.25},
        {"5999", 0.50}
    };

    if (dict.TryGetValue(mcc, out var risk))
        return risk;

    return 0.5;
}

record Response(bool approved, double fraud_score);

record Request(
    string id,
    Transaction transaction,
    Customer customer,
    Merchant merchant,
    Terminal terminal,
    LastTransaction? last_transaction
);

record Transaction(double amount, int installments, string requested_at);
record Customer(double avg_amount, int tx_count_24h, string[] known_merchants);
record Merchant(string id, string mcc, double avg_amount);
record Terminal(bool is_online, bool card_present, double km_from_home);
record LastTransaction(string timestamp, double km_from_current);
