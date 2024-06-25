using Coordinator.Models;
using Coordinator.Models.Contexts;
using Coordinator.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Coordinator.Services
{
    public class TransactionService(IHttpClientFactory _httpClientFactory, TwoPhaseCommitContext _context) : ITransactionService
    {
        //TwoPhaseCommitContext _context;

        //public TransactionService(TwoPhaseCommitContext context)
        //{
        //    _context = context;
        //}

        HttpClient _orderHttpClient = _httpClientFactory.CreateClient("OrderAPI");
        HttpClient _stockHttpClient = _httpClientFactory.CreateClient("StockAPI");
        HttpClient _paymentHttpClient = _httpClientFactory.CreateClient("PaymentAPI");

        public async Task<Guid> CreateTransactionAsync()
        {
            Guid transactionId = Guid.NewGuid();

            var nodes = await _context.Nodes.ToListAsync();
            nodes.ForEach(node => node.NodeStates = new List<NodeState>()
            {
                new(transactionId)
                {
                    IsReady = Enums.ReadyType.Pending,
                    TransactionState = Enums.TransactionState.Pending
                }
            });

            await _context.SaveChangesAsync();
            return transactionId;
        }
        public async Task PrepareServicesAsync(Guid transactionId)
        {
            var nodeStates = await GetNodeStatesAsync(transactionId);

            foreach (var nodeState in nodeStates)
            {
                try
                {
                    var response = await (nodeState.Node.Name switch
                    {
                        "Order.API" => _orderHttpClient.GetAsync("ready"),
                        "Stock.API" => _stockHttpClient.GetAsync("ready"),
                        "Payment.API" => _paymentHttpClient.GetAsync("ready"),
                    });

                    var result = bool.Parse(await response.Content.ReadAsStringAsync());
                    nodeState.IsReady = result ? Enums.ReadyType.Ready : Enums.ReadyType.Unready;
                }
                catch (Exception)
                {
                    nodeState.IsReady = Enums.ReadyType.Unready;
                }
            }

            await _context.SaveChangesAsync();
        }
        public async Task<bool> CheckReadyServicesAsync(Guid transactionId)
            => (await _context.NodeStates
                         .Where(ns => ns.TransactionId == transactionId)
                         .ToListAsync()).TrueForAll(ns => ns.IsReady == Enums.ReadyType.Ready);


        public async Task CommitAsync(Guid transactionId)
        {
            var nodeStates = await GetNodeStatesAsync(transactionId);

            foreach (var nodeState in nodeStates)
            {
                try
                {
                    var response = await (nodeState.Node.Name switch
                    {
                        "Order.API" => _orderHttpClient.GetAsync("commit"),
                        "Stock.API" => _stockHttpClient.GetAsync("commit"),
                        "Payment.API" => _paymentHttpClient.GetAsync("commit")
                    });

                    var result = bool.Parse(await response.Content.ReadAsStringAsync());
                    nodeState.TransactionState = result ? Enums.TransactionState.Done : Enums.TransactionState.Abort;
                }
                catch
                {
                    nodeState.TransactionState = Enums.TransactionState.Abort;
                }
            }

            await _context.SaveChangesAsync();
        }
        public async Task<bool> CheckTransactionStateServicesAsync(Guid transactionId)
            => (await _context.NodeStates
            .Where(ns => ns.TransactionId == transactionId)
            .ToListAsync()).TrueForAll(ns => ns.TransactionState == Enums.TransactionState.Done);
        public async Task RollbackAsync(Guid transactionId)
        {
            var nodeStates = await GetNodeStatesAsync(transactionId);

            foreach (var nodeState in nodeStates)
            {
                try
                {
                    if (nodeState.TransactionState == Enums.TransactionState.Done)
                        _ = await (nodeState.Node.Name switch
                        {
                            "Order.API" => _orderHttpClient.GetAsync("rollback"),
                            "Stock.API" => _stockHttpClient.GetAsync("rollback"),
                            "Payment.API" => _paymentHttpClient.GetAsync("rollback"),
                        });

                    nodeState.TransactionState = Enums.TransactionState.Abort;
                }
                catch
                {
                    nodeState.TransactionState = Enums.TransactionState.Abort;
                }
            }

            await _context.SaveChangesAsync();
        }


        #region Helper
        private async Task<List<NodeState>> GetNodeStatesAsync(Guid transactionId) =>
            await _context.NodeStates
                  .Where(ns => ns.TransactionId == transactionId)
                  .Include(ns => ns.Node)
                  .ToListAsync();
        #endregion
    }
}
