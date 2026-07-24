using Dapper;
using System.Data;

namespace Data.BulkOrder
{
    public interface IBulkOrderRepository
    {
        Task<Domain.Entities.BulkOrder?> GetBulkOrderByIdAsync(int bulkOrderId);
        Task<Domain.Entities.BulkOrder?> GetBulkOrderByConfirmationIDAsync(string confirmationOrderId);
    }
    public class BulkOrderRepository : IBulkOrderRepository
    {
        private readonly IDbConnection _connection;

        public BulkOrderRepository(IDbConnection connection)
        {
            _connection = connection;
        }

        public async Task<Domain.Entities.BulkOrder?> GetBulkOrderByIdAsync(int bulkOrderId)
        {
            const string sql = """
                SELECT *
                FROM dbo.BulkOrder
                WHERE BulkOrderId = @BulkOrderId;
                """;

            var command = new CommandDefinition(
                commandText: sql,
                parameters: new
                {
                    BulkOrderId = bulkOrderId
                });

            return await _connection
                .QuerySingleOrDefaultAsync<Domain.Entities.BulkOrder>(command);
        }
        public async Task<Domain.Entities.BulkOrder?> GetBulkOrderByConfirmationIDAsync(string confirmationOrderId)
        {
            const string sql = """
                SELECT *
                FROM dbo.BulkOrder
                WHERE ConfirmationID = @ConfirmationID;
                """;

            var command = new CommandDefinition(
                commandText: sql,
                parameters: new
                {
                    ConfirmationID = confirmationOrderId
                });

            return await _connection
                .QuerySingleOrDefaultAsync<Domain.Entities.BulkOrder>(command);
        }
    }
}