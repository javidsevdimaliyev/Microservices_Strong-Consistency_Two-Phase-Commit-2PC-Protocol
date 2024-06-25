using Coordinator.Models.Contexts;
using Coordinator.Services;
using Coordinator.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<TwoPhaseCommitContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("MSSQL")));

builder.Services.AddHttpClient("OrderAPI", client => client.BaseAddress = new("https://localhost:7268/"));
builder.Services.AddHttpClient("StockAPI", client => client.BaseAddress = new("https://localhost:7066/"));
builder.Services.AddHttpClient("PaymentAPI", client => client.BaseAddress = new("https://localhost:7226/"));

builder.Services.AddTransient<ITransactionService, TransactionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.MapGet("/create-order-transaction", async (ITransactionService transactionService) =>
{
    ///Phase 1 - Prepare
    var transactionId = await transactionService.CreateTransactionAsync();
    await transactionService.PrepareServicesAsync(transactionId);
    bool allServicesIsReady = await transactionService.CheckReadyServicesAsync(transactionId);
    bool transactionsState = default;
    if (allServicesIsReady)
    {
        ///Phase 2 - Commit
        await transactionService.CommitAsync(transactionId);
        transactionsState = await transactionService.CheckTransactionStateServicesAsync(transactionId);
    }

    if (!transactionsState)
        await transactionService.RollbackAsync(transactionId);
});


app.Run();
