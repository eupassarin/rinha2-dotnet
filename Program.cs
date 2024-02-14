#pragma warning disable
using System.Data.SqlClient;
using System.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Npgsql;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;


var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddNpgsqlDataSource(@$"
    User ID=root;Password=998877;
    Host={Environment.GetEnvironmentVariable("PG_HOST") ?? "localhost"};
    Port=5432;Database=rinha;Pooling=true;Minimum Pool Size=50;Maximum Pool Size=256;");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Logging.ClearProviders();
var app = builder.Build();
app.UseUnprocessableEntityMiddleware();

app.MapPost("/clientes/{id}/transacoes", async (NpgsqlDataSource dataSource, [FromRoute] short id, [FromBody] Transacao t) =>
{
    if (id > 5) return Results.NotFound();

    if (string.IsNullOrEmpty(t.descricao) || t.descricao.Length < 1 || t.descricao.Length > 10) 
        return Results.UnprocessableEntity();

    var limite = new[] { 1000_00, 800_00, 10_000_00, 100_000_00, 5000_00 }[id - 1];

    switch (t.tipo)
    {
        case "d":
            {
                var res = await dataSource.CreateCommand(@$"
                    SELECT D({id}::SMALLINT, {t.valor}, '{t.descricao}', {limite})")
                    .ExecuteScalarAsync();
                if (res is DBNull) 
                    return Results.UnprocessableEntity();
                return Results.Ok(new SaldoLimite((int)res, limite));
            }

        case "c":
                return Results.Ok(new SaldoLimite((int)await dataSource.CreateCommand(@$"
                    SELECT C({id}::SMALLINT, {t.valor}, '{t.descricao}')")
                    .ExecuteScalarAsync(), limite));
        default:
            return Results.UnprocessableEntity();
    }
     
});

app.MapGet("/clientes/{id}/extrato", async (NpgsqlDataSource dataSource, [FromRoute] short id) =>
{
    if (id > 5) return Results.NotFound(); 

    var limite = new[] { 1000_00, 800_00, 10_000_00, 100_000_00, 5000_00 }[id - 1];

    var res = await dataSource.CreateCommand(@$"
            SELECT valor, tipo, descricao, realizada_em 
            FROM transacao 
            WHERE cliente_id = {id}
            ORDER BY realizada_em DESC LIMIT 10").ExecuteReaderAsync();

    return Results.Ok(new Extrato(
        saldo: new Saldo(
            (int) await dataSource
            .CreateCommand(@$"SELECT saldo FROM cliente WHERE id = {id}")
            .ExecuteScalarAsync(), DateTime.Now, limite), 
        ultimas_transacoes: Enumerable.Range(0, 10)
            .Select(_ => res.ReadAsync().Result)
            .TakeWhile(hasNext => hasNext)
            .Select(_ => new Transacao(
                valor: res.GetInt32(0),
                tipo: res.GetString(1),
                descricao: res.GetString(2),
                realizada_em: res.GetInt64(3)))
            .ToArray()));
});

app.Run($"http://0.0.0.0:{Environment.GetEnvironmentVariable("HTTP_PORT") ?? "80"}");

record TransacaoPayload (int valor, char tipo, string descricao);
record SaldoLimite(int saldo, int limite);
record Extrato(Saldo saldo, Transacao[] ultimas_transacoes);
record Transacao(int valor, string tipo, string descricao, long realizada_em);
record Saldo(int total, DateTime data_extrato, int limite);

[JsonSerializable(typeof(TransacaoPayload))]
[JsonSerializable(typeof(SaldoLimite))]
[JsonSerializable(typeof(Extrato))]
[JsonSerializable(typeof(Transacao))]
[JsonSerializable(typeof(Saldo))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

internal static class MiddlewareExtensions
{
    public static void UseUnprocessableEntityMiddleware(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            await next();
            if (context.Response.StatusCode == StatusCodes.Status400BadRequest)
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        });
    }
}