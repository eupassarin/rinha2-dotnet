#pragma warning disable
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

var builder = WebApplication.CreateSlimBuilder(args);
var pgHost = Environment.GetEnvironmentVariable("PG_HOST") ?? "localhost";
var maxPoolSize = Environment.GetEnvironmentVariable("MAX_POOL_SIZE") ?? "256";
var connStr = @$"Host={pgHost};Database=rinha;Username=root;Password=998877;Minimum Pool Size=170;Maximum Pool Size={maxPoolSize};";

builder.Services.AddSingleton<NpgsqlDataSource>(new NpgsqlDataSourceBuilder(connStr).Build());
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
builder.Logging.ClearProviders();
var app = builder.Build();
app.UseUnprocessableEntityMiddleware();

app.MapPost("/clientes/{id}/transacoes", 
    async ([FromServices] NpgsqlDataSource dataSource, [FromRoute] short id, [FromBody] Transacao t) =>
{
    if (id > 5) return Results.NotFound();

    if (string.IsNullOrEmpty(t.descricao) || t.descricao.Length < 1 || t.descricao.Length > 10) 
        return Results.UnprocessableEntity();

    var limite = new[] { 1000_00, 800_00, 10_000_00, 100_000_00, 5000_00 }[id - 1];
    
    switch (t.tipo)
    {
        case "d":
        {
            var conn = await dataSource.OpenConnectionAsync();
            var res = new NpgsqlCommand(@$"SELECT D({id}::SMALLINT, {t.valor}, '{t.descricao}', {limite})", conn).ExecuteScalar();
            conn.CloseAsync();
            if (res is DBNull) return Results.UnprocessableEntity();
            return Results.Ok(new SaldoLimite((int)res, limite));
        }
        case "c":
        {
            var conn = await dataSource.OpenConnectionAsync();
            var saldo = new NpgsqlCommand(@$"SELECT C({id}::SMALLINT, {t.valor}, '{t.descricao}')", conn).ExecuteScalar();
            conn.CloseAsync();
            return Results.Ok(new SaldoLimite((int) saldo, limite));
        }
        default:
            return Results.UnprocessableEntity();
    }
     
});
app.MapGet("/clientes/{id}/extrato", async (NpgsqlDataSource dataSource, [FromRoute] short id) =>
{
    if (id > 5) return Results.NotFound(); 

    var limite = new[] { 1000_00, 800_00, 10_000_00, 100_000_00, 5000_00 }[id - 1];
    
    var conn_e = await dataSource.OpenConnectionAsync();
    var saldo = await new NpgsqlCommand(@$"SELECT saldo FROM cliente WHERE id = {id}", conn_e).ExecuteScalarAsync();
    conn_e.CloseAsync();

    var conn_c = await dataSource.OpenConnectionAsync();
    var res = await new NpgsqlCommand(@$"
            SELECT valor, tipo, descricao, realizada_em FROM transacao WHERE cliente_id = {id}
            ORDER BY realizada_em DESC LIMIT 10", conn_c).ExecuteReaderAsync();
    var transacoes = Enumerable.Range(0, 10).Select(_ => res.ReadAsync().Result).TakeWhile(hasNext => hasNext)
            .Select(_ => new Transacao(valor: res.GetInt32(0),tipo: res.GetString(1),
                descricao: res.GetString(2),realizada_em: res.GetInt64(3))).ToArray();
    conn_c.CloseAsync();

    return Results.Ok(new Extrato(new Saldo((int)saldo , DateTime.Now, limite), transacoes));
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