
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly SigningCredentials _signingCredentials;

        public AuthController(IConfiguration config, SigningCredentials signingCredentials)
        {
            _config = config;
            _signingCredentials = signingCredentials;
        }

        [AllowAnonymous]
        [HttpPost("token")]
        public async Task<IActionResult> GetToken([FromBody] ClientAuthModel model)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var start = DateTime.UtcNow;

            if (model is null ||
                string.IsNullOrWhiteSpace(model.ClientId) ||
                string.IsNullOrWhiteSpace(model.ClientSecret))
            {
                return Unauthorized(new
                {
                    message = "Client ID and client secret are required."
                });
            }

            try
            {
                var isValid = await ValidateClientAsync(
                    model.ClientId,
                    model.ClientSecret);

                if (!isValid)
                {
                    Log.Warning(
                        "Invalid client credentials. ClientId: {ClientId}; CorrelationId: {CorrelationId}",
                        model.ClientId,
                        correlationId);

                    return Unauthorized(new
                    {
                        message = "Invalid client credentials."
                    });
                }

                var issuer =
                    _config["AppSettings:Issuer"]
                    ?? throw new InvalidOperationException(
                        "AppSettings:Issuer is missing.");

                var audience =
                    _config["AppSettings:Audience"]
                    ?? throw new InvalidOperationException(
                        "AppSettings:Audience is missing.");

                var timeoutHours =
                    _config.GetValue<double>(
                        "AppSettings:HoursForTimeout",
                        1.0);

                var issuedAt = DateTime.UtcNow;
                var expiresAt = issuedAt.AddHours(timeoutHours);

                var claims = new List<Claim>
                {
                    new(
                        JwtRegisteredClaimNames.Sub,
                        model.ClientId),

                    new(
                        JwtRegisteredClaimNames.Jti,
                        Guid.NewGuid().ToString("N")),

                    new(
                        JwtRegisteredClaimNames.Iat,
                        new DateTimeOffset(issuedAt)
                            .ToUnixTimeSeconds()
                            .ToString(),
                        ClaimValueTypes.Integer64),

                    new(
                        "client_id",
                        model.ClientId),

                    new(
                        ClaimTypes.Name,
                        model.ClientId),

                    new(
                        ClaimTypes.Role,
                        "Customer")
                };

                /*
                 * This uses the exact SigningCredentials registered
                 * in Program.cs, which uses DevAPITokenKey.
                 */
                var jwt = new JwtSecurityToken(
                    issuer: issuer,
                    audience: audience,
                    claims: claims,
                    notBefore: issuedAt,
                    expires: expiresAt,
                    signingCredentials: _signingCredentials);

                var accessToken =
                    new JwtSecurityTokenHandler()
                        .WriteToken(jwt);

                var expiresInSeconds =
                    Convert.ToInt32(
                        (expiresAt - issuedAt).TotalSeconds);

                Log.Information(
                    "JWT issued. ClientId: {ClientId}; CorrelationId: {CorrelationId}; ExpiresUtc: {ExpiresUtc}",
                    model.ClientId,
                    correlationId,
                    expiresAt);

                return Ok(new
                {
                    access_token = accessToken,
                    token_type = "Bearer",
                    expires_in = expiresInSeconds,
                    expires_utc = expiresAt
                });
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Token generation failed. ClientId: {ClientId}; CorrelationId: {CorrelationId}; StartUtc: {StartUtc}; EndUtc: {EndUtc}",
                    model.ClientId,
                    correlationId,
                    start,
                    DateTime.UtcNow);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message =
                            "An error occurred while generating the token.",
                        correlationId
                    });
            }
        }

        private async Task<bool> ValidateClientAsync(string clientId, string clientSecret)
        {
            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret))
            {
                return false;
            }

            var storedSecretHash =
                await Domain.AzureHelper.GetSecretAsync(clientId);

            if (string.IsNullOrWhiteSpace(storedSecretHash))
            {
                return false;
            }

            try
            {
                return BCrypt.Net.BCrypt.Verify(
                    clientSecret,
                    storedSecretHash);
            }
            catch (BCrypt.Net.SaltParseException ex)
            {
                Log.Error(
                    ex,
                    "The stored secret for ClientId {ClientId} is not a valid BCrypt hash.",
                    clientId);

                return false;
            }
        }
    }

    public sealed class ClientAuthModel
    {
        public required string ClientId { get; set; }

        public required string ClientSecret { get; set; }
    }
}

