using Data;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sentry;
using Sentry.Extensions.Logging;
using Serilog;
using System.Text.Json;

namespace API.Controllers
{
    [Authorize()]
    [Route("api/[controller]")]
    [ApiController]
    public class BaseController : ControllerBase
    {
        protected IConfiguration Config =>
            HttpContext.RequestServices
            .GetRequiredService<IConfiguration>();

        protected T GetService<T>() where T : notnull
        {
            return HttpContext.RequestServices.GetRequiredService<T>();
        }

        [NonAction]
        protected async Task LogExceptionDataAsync(Domain.Logging log)
        {
            var rawJson = JsonSerializer.Serialize(log);
            var prettyJson = await PrettyJsonString(rawJson);

            Log.Logger.Write(log.Type, prettyJson + "\r\n\r\n");

            if (Config.GetValue<bool>("Sentry:IsLogToSentry"))
                SentrySdk.CaptureMessage(prettyJson, MapToSentryLevel(log.Type));
        }

        [NonAction]
        protected async Task CaptureException(Exception ex)
        {
            // Capture full exception in Sentry
            SentrySdk.CaptureException(ex);

            // Build a JSON object for logging
            var logObj = new
            {
                Exception = ex.Message,
                StackTrace = ex.StackTrace,
                Type = ex.GetType().FullName
            };

            var rawJson = JsonSerializer.Serialize(logObj);
            var prettyJson = await PrettyJsonString(rawJson);

            Log.Error(prettyJson + "\r\n\r\n");
        }

        [NonAction]
        protected async Task InformationalLogging(Logging log)
        {
            var rawJson = JsonSerializer.Serialize(log);

            var prettyJson = await PrettyJsonString(rawJson);

            Log.Logger.Write(log.Type, prettyJson + "\r\n\r\n");

            if (Config.GetValue<bool>("Sentry:IsLogToSentry"))
            {
                var sentryLevel = MapToSentryLevel(log.Type);
                SentrySdk.CaptureMessage(prettyJson, sentryLevel);
            }
        }

        [NonAction]
        public async Task<string> PrettyJsonString(string jsonStr)
        {
            if (string.IsNullOrWhiteSpace(jsonStr))
                return string.Empty;

            jsonStr = jsonStr
                .Replace("\r\n", "")
                .Replace("\n", "")
                .Replace("\r", "")
                .Replace("\u0022", "\"")   // convert escaped quotes back to real quotes
                .Trim();

            return await Task.Run(() =>
            {
                try
                {
                    int idx = jsonStr.IndexOf('{');
                    if (idx > 0)
                        jsonStr = jsonStr.Substring(idx);

                    var element = JsonSerializer.Deserialize<JsonElement>(jsonStr);

                    return JsonSerializer.Serialize(
                        element,
                        new JsonSerializerOptions { WriteIndented = true }
                    );
                }
                catch
                {
                    return jsonStr;
                }
            });
        }

        [NonAction]
        private static SentryLevel MapToSentryLevel(Serilog.Events.LogEventLevel level)
        {
            return level switch
            {
                Serilog.Events.LogEventLevel.Verbose => SentryLevel.Debug,
                Serilog.Events.LogEventLevel.Debug => SentryLevel.Debug,
                Serilog.Events.LogEventLevel.Information => SentryLevel.Info,
                Serilog.Events.LogEventLevel.Warning => SentryLevel.Warning,
                Serilog.Events.LogEventLevel.Error => SentryLevel.Error,
                Serilog.Events.LogEventLevel.Fatal => SentryLevel.Fatal,
                _ => SentryLevel.Info
            };
        }
        [NonAction]
        protected static string GetAPIClient(ControllerBase controller)
        {
            var clientId =
                controller.User.FindFirst("client_id")?.Value
                ?? controller.User.FindFirst("clientid")?.Value
                ?? controller.User.FindFirst("azp")?.Value
                ?? controller.User.FindFirst("appid")?.Value
                ?? string.Empty;

            return clientId;
        }
    }
}
