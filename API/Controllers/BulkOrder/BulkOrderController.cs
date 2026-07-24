using Data.BulkOrder;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.BulkOrder
{
    [Authorize]
    [Route("api/[controller]")]
    public class BulkOrderController : BaseController 
    {
        private IBulkOrderRepository BulkOrderRepository =>
            GetService<IBulkOrderRepository>();

        public BulkOrderController()
        {
        }

        [HttpGet("GetBulkByOrderID/{bulkOrderId:int}")]
        public async Task<ActionResult<Domain.Entities.BulkOrder>> GetBulkByOrderID(int bulkOrderId, string correlationID = "")
        {
            DateTime start = DateTime.Now;

            if (string.IsNullOrEmpty(correlationID))
                correlationID = Guid.NewGuid().ToString("N");
            try
            {
                var bulkOrder =
                    await BulkOrderRepository.GetBulkOrderByIdAsync(bulkOrderId);

                if (bulkOrder is null)
                {
                    return NotFound(new
                    {
                        message = $"Bulk order {bulkOrderId} was not found."
                    });
                }

                return Ok(bulkOrder);
            }
            catch (Exception ex)
            {
                await LogExceptionDataAsync(new Logging
                {
                    LogMessage = ex.Message,
                    CorrelationID = correlationID,
                    CallerID = GetAPIClient(this),
                    ExceptionStackTrace = ex.StackTrace ?? string.Empty,
                    EndpointName = "BulkOrderController.GetBulkByOrderID",
                    LogStartDateTime = start,
                    LogFinishDateTime = DateTime.Now,
                    Type = Serilog.Events.LogEventLevel.Error
                });
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message = "An error occurred while retrieving the bulk order."
                    });
            }
        }

        [HttpGet("GetBulkByOrderConfirmationID/{confirmationId}")]
        public async Task<ActionResult<Domain.Entities.BulkOrder>> GetBulkByOrderConfirmationID(string confirmationId, string correlationID = "")
        {
            DateTime start = DateTime.Now;

            if (string.IsNullOrEmpty(correlationID))
                correlationID = Guid.NewGuid().ToString("N");
            try
            {
                var bulkOrder =
                    await BulkOrderRepository
                        .GetBulkOrderByConfirmationIDAsync(confirmationId);

                if (bulkOrder is null)
                {
                    return NotFound(new
                    {
                        message =
                            $"Bulk order confirmation ID {confirmationId} was not found."
                    });
                }

                return Ok(bulkOrder);
            }
            catch (Exception ex)
            {
                await LogExceptionDataAsync(new Logging
                {
                    LogMessage = ex.Message,
                    CorrelationID = correlationID,
                    CallerID = GetAPIClient(this),
                    ExceptionStackTrace = ex.StackTrace ?? string.Empty,
                    EndpointName = "BulkOrderController.GetBulkByOrderID",
                    LogStartDateTime = start,
                    LogFinishDateTime = DateTime.Now,
                    Type = Serilog.Events.LogEventLevel.Error
                });
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message = "An error occurred while retrieving the bulk order."
                    });
            }
        }
    }
}