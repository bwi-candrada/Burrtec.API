using Data.BulkOrder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

//
// Serilog bootstrap logger
//
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();


try
{
    //
    // Azure Key Vault
    //
    var keyVaultUrl = builder.Configuration["AzureKeyVault:VaultUrl"]
        ?? throw new InvalidOperationException(
            "AzureKeyVault:VaultUrl is missing.");

    var managedIdentityClientId = builder.Configuration[
        "AzureKeyVault:ManagedIdentityClientId"];

    Domain.AzureHelper.Initialize(keyVaultUrl,builder.Environment.IsDevelopment(),managedIdentityClientId);

    //
    // Retrieve secrets
    //
    string APIDB = string.Empty;
    if (builder.Environment.IsDevelopment())
        APIDB = "LocalDevDBConnStr";
    else
        APIDB = "DeployedAPIDBConnStr";

    var connectionString =
            await Domain.AzureHelper.GetSecretAsync(APIDB)
            ?? throw new InvalidOperationException(
                "The APIDB connection string was not found in Azure Key Vault.");

    //var connectionString = builder.Configuration["AppSettings:ConnStr"]?.ToString()
    //        ?? throw new InvalidOperationException(
    //            "The APIDB connection string was not found in Azure Key Vault.");

    var tokenKey = await Domain.AzureHelper.GetSecretAsync("DevAPITokenKey")
        ?? throw new InvalidOperationException(
            "The DevAPITokenKey secret was not found in Azure Key Vault.");

    if (string.IsNullOrWhiteSpace(tokenKey))
    {
        throw new InvalidOperationException(
            "The DevAPITokenKey secret is empty.");
    }

    var tokenKeyBytes = Encoding.UTF8.GetBytes(tokenKey);

    if (tokenKeyBytes.Length < 32)
    {
        throw new InvalidOperationException("DevAPITokenKey must contain at least 32 UTF-8 bytes for HS256.");
    }

    //
    // JWT configuration
    //
    var issuer = builder.Configuration["AppSettings:Issuer"]
        ?? throw new InvalidOperationException(
            "AppSettings:Issuer is missing.");

    var audience = builder.Configuration["AppSettings:Audience"]
        ?? throw new InvalidOperationException(
            "AppSettings:Audience is missing.");

    /*
     * Do not assign KeyId unless you intentionally use key rotation
     * and both token creation and token validation use the same kid.
     */
    var signingKey = new SymmetricSecurityKey(tokenKeyBytes);

    var signingCredentials = new SigningCredentials(signingKey,SecurityAlgorithms.HmacSha256);

    var _sentryDSN = await Domain.AzureHelper.GetSecretAsync("SentryDSN")
        ?? throw new InvalidOperationException(
            "Missing Sentry DSN.");

    builder.Host.UseSerilog();
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = _sentryDSN;
        options.SendDefaultPii = true;
        options.AttachStacktrace = true;
        options.MinimumBreadcrumbLevel = LogLevel.Information;
        options.MinimumEventLevel = LogLevel.Error;

        options.Environment =
            builder.Environment.EnvironmentName;

        options.Release =
            typeof(Program).Assembly
                .GetName()
                .Version?
                .ToString();
    });

    /*
     * Register this so the token controller can use the exact same
     * signing key and algorithm that validation uses.
     */
    builder.Services.AddSingleton(signingCredentials);

    //
    // Controllers
    //
    builder.Services
        .AddControllers()
        .AddNewtonsoftJson();

    //
    // Dapper SQL connection
    //
    builder.Services.AddScoped<IDbConnection>(_ =>
        new SqlConnection(connectionString));

    //
    // Repository registrations
    //
    builder.Services.AddScoped<
        IBulkOrderRepository,
        BulkOrderRepository>();

    //
    // Authentication
    //
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;

            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

            options.SaveToken = true;

            options.IncludeErrorDetails = builder.Environment.IsDevelopment();

            options.MapInboundClaims = false;

            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    //
                    // Signature
                    //
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    RequireSignedTokens = true,

                    /*
                     * The API accepts only HS256 tokens.
                     */
                    ValidAlgorithms =
                    [
                        SecurityAlgorithms.HmacSha256
                    ],

                    //
                    // Issuer
                    //
                    ValidateIssuer = true,
                    ValidIssuer = issuer,

                    //
                    // Audience
                    //
                    ValidateAudience = true,
                    ValidAudience = audience,

                    //
                    // Lifetime
                    //
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),

                    //
                    // Claims
                    //
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var authorizationHeader =
                        context.Request.Headers.Authorization
                            .ToString();

                    if (string.IsNullOrWhiteSpace(
                            authorizationHeader))
                    {
                        Log.Warning(
                            "Authorization header was not provided. Path: {Path}",
                            context.Request.Path);
                    }

                    return Task.CompletedTask;
                },

                OnAuthenticationFailed = context =>
                {
                    var authorizationHeader = context.Request.Headers.Authorization.ToString();

                    var token = authorizationHeader;

                    if (token.StartsWith(
                            "Bearer ",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        token = token["Bearer ".Length..].Trim();
                    }

                    string? algorithm = null;
                    string? keyId = null;
                    string? tokenIssuer = null;
                    string? tokenAudience = null;

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            var handler =
                                new JsonWebTokenHandler();

                            if (handler.CanReadToken(token))
                            {
                                var jwt =
                                    handler.ReadJsonWebToken(token);

                                algorithm = jwt.Alg;
                                keyId = jwt.Kid;
                                tokenIssuer = jwt.Issuer;
                                tokenAudience =
                                    string.Join(
                                        ",",
                                        jwt.Audiences);
                            }
                        }
                    }
                    catch (Exception inspectionException)
                    {
                        Log.Warning(
                            inspectionException,
                            "The failed JWT could not be inspected.");
                    }

                    Log.Error(
                        context.Exception,
                        """
                        JWT authentication failed.
                        Path: {Path}
                        ExceptionType: {ExceptionType}
                        Message: {Message}
                        Algorithm: {Algorithm}
                        TokenKeyId: {TokenKeyId}
                        TokenIssuer: {TokenIssuer}
                        ExpectedIssuer: {ExpectedIssuer}
                        TokenAudience: {TokenAudience}
                        ExpectedAudience: {ExpectedAudience}
                        """,
                        context.Request.Path,
                        context.Exception.GetType().Name,
                        context.Exception.Message,
                        algorithm ?? "(missing)",
                        keyId ?? "(missing)",
                        tokenIssuer ?? "(missing)",
                        issuer,
                        tokenAudience ?? "(missing)",
                        audience);

                    return Task.CompletedTask;
                },

                OnTokenValidated = context =>
                {
                    Log.Information(
                        "JWT validated for subject {Subject}.",
                        context.Principal?
                            .FindFirst("sub")?
                            .Value
                        ?? context.Principal?
                            .Identity?
                            .Name
                        ?? "unknown");

                    return Task.CompletedTask;
                },

                OnChallenge = context =>
                {
                    Log.Warning(
                        "JWT challenge. Path: {Path}; Error: {Error}; Description: {Description}",
                        context.Request.Path,
                        context.Error,
                        context.ErrorDescription);

                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();

    //
    // Rate limiting
    //
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(
            "ApiRateLimit",
            httpContext =>
            {
                var clientIdentifier =
                    httpContext.User
                        .FindFirst("client_id")?
                        .Value
                    ?? httpContext.User
                        .FindFirst("sub")?
                        .Value
                    ?? httpContext.Connection
                        .RemoteIpAddress?
                        .ToString()
                    ?? "unknown-client";

                return RateLimitPartition
                    .GetFixedWindowLimiter(
                        partitionKey:
                            clientIdentifier,
                        factory: _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 100,
                                Window =
                                    TimeSpan.FromMinutes(1),
                                QueueLimit = 0,
                                QueueProcessingOrder =
                                    QueueProcessingOrder
                                        .OldestFirst,
                                AutoReplenishment = true
                            });
            });

        options.OnRejected = async (
            context,
            cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode =
                StatusCodes.Status429TooManyRequests;

            context.HttpContext.Response.ContentType =
                "application/json";

            if (context.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var retryAfter))
            {
                context.HttpContext.Response
                    .Headers
                    .RetryAfter =
                    Math.Ceiling(
                            retryAfter.TotalSeconds)
                        .ToString();
            }

            await context.HttpContext.Response
                .WriteAsJsonAsync(
                    new
                    {
                        statusCode =
                            StatusCodes
                                .Status429TooManyRequests,
                        message =
                            "Too many requests. Please try again later."
                    },
                    cancellationToken);
        };
    });

    //
    // Swagger
    //
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc(
            "v1",
            new OpenApiInfo
            {
                Title = "Burrtec Public API",
                Version = "v1"
            });

        options.AddSecurityDefinition(
            "Bearer",
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description =
                    "Enter the JWT only. Do not include the word Bearer."
            });

        options.AddSecurityRequirement(document =>
            new OpenApiSecurityRequirement
            {
                [
                    new OpenApiSecuritySchemeReference(
                        "Bearer",
                        document)
                ] = []
            });
    });

    var app = builder.Build();

    //
    // HTTP pipeline
    //
    //if (app.Environment.IsDevelopment())
    //{
    //    app.UseSwagger();

    //    app.UseSwaggerUI(options =>
    //    {
    //        options.SwaggerEndpoint(
    //            "/swagger/v1/swagger.json",
    //            "Burrtec Public API v1");
    //    });
    //}

    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Burrtec Public API v1");
    });

    app.UseHttpsRedirection();

    /*
     * Authentication must run before rate limiting if the rate-limit
     * partition uses the authenticated client or subject.
     */
    app.UseAuthentication();

    app.UseRateLimiter();

    app.UseAuthorization();

    app.MapControllers()
        .RequireRateLimiting("ApiRateLimit");

    Log.Information(
        "Starting Burrtec Public API. Issuer: {Issuer}; Audience: {Audience}",
        issuer,
        audience);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Burrtec Public API terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

