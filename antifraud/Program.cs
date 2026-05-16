using System.Net;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/ready", () =>
{
    return Results.Ok();
});

app.MapPost("/fraud-score", ([FromBody]Request req) =>
{
    Console.WriteLine(JsonSerializer.Serialize(req));

    var res = new Response(true, 0.8);
    return Results.Ok(res);
});

app.Run();

record Response(bool approved, double fraud_score);

record Request(
    string id,
    Transaction transaction,
    Customer customer,
    Merchant merchant,
    Terminal terminal,
    LastTransaction? last_transaction
);

record Transaction(decimal amount, int installments, string requested_at);
record Customer(decimal avg_amount, int tx_count_24h, string[] known_merchants);
record Merchant(string id, string mcc, decimal avg_amount);
record Terminal(bool is_online, bool card_present, decimal km_from_home);
record LastTransaction(string timestamp, decimal km_from_current);
