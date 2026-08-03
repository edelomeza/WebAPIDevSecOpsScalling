using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using FluentValidation;
using Scalar.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Filters;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using MassTransit;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/audit-.json",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        formatter: new Serilog.Formatting.Json.JsonFormatter())
    .Filter.ByExcluding(Matching.WithProperty("Exception"))
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProcessId()
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Error)
    .CreateLogger();

Log.Information("Iniciando aplicación WebAPIDevSecOps");

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

var enableConsoleExport = builder.Configuration.GetValue<bool>("Observability:ConsoleExport");
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        metrics.AddMeter(WebAPIDevSecOps.Services.QualityMetricsService.MeterName);
        metrics.AddPrometheusExporter();
        if (enableConsoleExport)
            metrics.AddConsoleExporter();
    })
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        if (enableConsoleExport)
            tracing.AddConsoleExporter();
    });

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null);

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});


builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = false;
});

var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");

if (useInMemory)
{
    builder.Services.RemoveAll(typeof(AppDbContext));
    builder.Services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
    var dbName = builder.Configuration["InMemoryDatabaseName"] ?? "AppDb";
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseInMemoryDatabase(dbName));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("DefaultConnection no configurada");

    var dbUser = builder.Configuration["DB_USER"] ?? builder.Configuration["DbUser"];
    var dbPassword = builder.Configuration["DB_PASSWORD"] ?? builder.Configuration["DbPassword"];

    if (!string.IsNullOrEmpty(dbUser) && !string.IsNullOrEmpty(dbPassword))
    {
        var connBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
        {
            UserID = dbUser,
            Password = dbPassword
        };
        connectionString = connBuilder.ConnectionString;
    }

    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.CommandTimeout(60);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        });
    });
}


var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "self" });

if (!useInMemory)
{
    healthChecks.AddRedis(
        redisConnectionString: builder.Configuration.GetConnectionString("Redis") ?? builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379",
        name: "redis",
        tags: new[] { "redis", "cache" });

    healthChecks.AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection no configurada"),
        healthQuery: "SELECT 1;",
        name: "sql-server",
        tags: new[] { "db", "sqlserver" });
}

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHealthChecksUI(options =>
    {
        options.SetEvaluationTimeInSeconds(30);
        options.MaximumHistoryEntriesPerEndpoint(50);
    })
    .AddInMemoryStorage();
}

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    Log.Information("Using PORT env var: {Port}", port);
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxConcurrentUpgradedConnections = 100;
    options.Limits.MaxRequestBodySize = 1048576;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
});

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT Key no configurada");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("JWT Key debe tener al menos 32 bytes (256 bits).");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = true;
    options.SaveToken = true;

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Log.Error(context.Exception, "JWT authentication failed: {Message}", context.Exception.Message);
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Log.Information("JWT authenticated: {User}", context.Principal?.Identity?.Name);
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            Log.Warning("JWT challenge (401): {Error} {ErrorDescription}", context.Error, context.ErrorDescription);
            return Task.CompletedTask;
        }
    };

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ValidateIssuer = true,
        ValidateAudience = true,
        ClockSkew = TimeSpan.Zero,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],

        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },

        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1000,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    options.AddSlidingWindowLimiter("LoginPolicy", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(5);
        opt.PermitLimit = 5;
        opt.SegmentsPerWindow = 5;
        opt.QueueLimit = 0;
    });

    options.AddSlidingWindowLimiter("Login2faVerifyPolicy", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(5);
        opt.PermitLimit = 10;
        opt.SegmentsPerWindow = 5;
        opt.QueueLimit = 0;
    });

    options.AddSlidingWindowLimiter("AdminPolicy", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 200;
        opt.SegmentsPerWindow = 4;
        opt.QueueLimit = 0;
    });

    options.AddConcurrencyLimiter("ConcurrentWritesPolicy", opt =>
    {
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

static string GetAllowedOrigin(IConfiguration config)
{
    var origin = config["Cors:AllowedOrigin"];
    if (!string.IsNullOrWhiteSpace(origin))
        return origin;

    origin = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGIN");
    if (!string.IsNullOrWhiteSpace(origin))
        return origin;

    return "https://localhost:5097";
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("SecurePolicy",
        policy =>
        {
            policy.WithOrigins(GetAllowedOrigin(builder.Configuration))
           .AllowAnyHeader()
           .AllowAnyMethod()
           .AllowCredentials();
        });
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",
        policy => policy.RequireRole("Admin"));
});

builder.Services.AddMemoryCache();

if (useInMemory)
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    });
}

builder.Services.AddResponseCaching();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserAccessor, UserAccessor>();

builder.Services.Configure<Microsoft.AspNetCore.HttpsPolicy.HstsOptions>(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.Configure<WebAPIDevSecOps.Dto.PasswordHasherOptions>(builder.Configuration.GetSection("PasswordHashing"));
builder.Services.Configure<ResilienceOptions>(builder.Configuration.GetSection("Resilience"));
builder.Services.AddSingleton<DbResilienceService>();
builder.Services.AddSingleton<WebAPIDevSecOps.Services.QualityMetricsService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddSingleton<WebAPIDevSecOps.Interfaces.ITokenBlacklistService, WebAPIDevSecOps.Services.TokenBlacklistService>();
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<IEmpleadoService, EmpleadoService>();
builder.Services.AddScoped<ITipoEmpleadoService, TipoEmpleadoService>();
builder.Services.AddScoped<IVenCatEstadoService, VenCatEstadoService>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IProductoService, ProductoService>();
builder.Services.AddScoped<IVentaService, VentaService>();
builder.Services.AddScoped<IVentaDetalleService, VentaDetalleService>();
builder.Services.AddScoped<ICacheService, CacheService>();
builder.Services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
builder.Services.AddScoped<IVentasPedidoService, VentasPedidoService>();
builder.Services.AddScoped<IPagoService, PagoService>();
builder.Services.AddScoped<IFacturaService, FacturaService>();
builder.Services.AddScoped<ICompensationService, CompensationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<ILogin2faService, Login2faService>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<WebAPIDevSecOps.Consumers.StockValidatorConsumer>();
    x.AddConsumer<WebAPIDevSecOps.Consumers.PagoConsumer>();
    x.AddConsumer<WebAPIDevSecOps.Consumers.FacturaConsumer>();
    x.AddConsumer<WebAPIDevSecOps.Consumers.CompensationConsumer>();

    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Ingrese su token JWT"
        };
        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference("Bearer", null, null), new List<string>() }
        });
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.Services.GetRequiredService<WebAPIDevSecOps.Services.QualityMetricsService>();
Log.Information("Métricas de calidad registradas en el MeterProvider (test_coverage_percent, mutation_score, sonar_quality_gate_passed, p95_latency_ms)");

var integritySection = builder.Configuration.GetSection("AssemblyIntegrity");
var expectedHash = integritySection["ExpectedHash"];
if (!string.IsNullOrEmpty(expectedHash))
{
    var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
    if (assemblyPath != null && File.Exists(assemblyPath))
    {
        using var stream = File.OpenRead(assemblyPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Assembly integrity check FAILED for {Assembly}. Expected: {Expected}, Actual: {Actual}",
                Path.GetFileName(assemblyPath), expectedHash, actualHash);
        }
        else
        {
            Log.Information("Assembly integrity check PASSED for {Assembly}", Path.GetFileName(assemblyPath));
        }
    }
}
else if (!builder.Environment.IsDevelopment())
{
    Log.Warning("Assembly integrity check not configured. Set AssemblyIntegrity:ExpectedHash in appsettings.json");
}

if (!useInMemory)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Database.Migrate();
        Log.Information("Migraciones EF aplicadas correctamente");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error al aplicar migraciones EF — la app continuará");
    }
}

if (app.Environment.IsDevelopment())
{
    var warmupSw = Stopwatch.StartNew();
    using (var scope = app.Services.CreateScope())
    {
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try { ctx.SegUsuario.Any(); }
        catch { /* BD no accesible en warmup, se tolera */ }
    }
    warmupSw.Stop();
    Log.Information("Warmup EF + pool ejecutado en {ElapsedMs}ms", warmupSw.ElapsedMilliseconds);
}

app.UseResponseCompression();
app.UseResponseCaching();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("SecurePolicy");
app.UseForwardedHeaders();
app.UseHttpsRedirection();

app.UseMiddleware<WebAPIDevSecOps.Middleware.CorrelationIdMiddleware>();
app.UseMiddleware<WebAPIDevSecOps.Middleware.RequestTimeoutMiddleware>();
app.UseMiddleware<WebAPIDevSecOps.Middleware.AuditLoggingMiddleware>();
app.UseMiddleware<WebAPIDevSecOps.Middleware.ExceptionHandlingMiddleware>();

app.Use(async (context, next) =>
{
    var token = context.Request.Headers["Authorization"]
        .ToString()
        .Replace("Bearer ", "");

    if (!string.IsNullOrEmpty(token))
    {
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var jti = jwt?.Id;
            if (!string.IsNullOrEmpty(jti))
            {
                var blacklistService = context.RequestServices.GetRequiredService<WebAPIDevSecOps.Interfaces.ITokenBlacklistService>();
                if (await blacklistService.IsBlacklistedAsync(jti))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Token inválido");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Error al validar blacklist de token");
        }
    }

    await next();
});

app.UseMiddleware<WebAPIDevSecOps.Middleware.SecurityHeadersMiddleware>();
app.UseMiddleware<WebAPIDevSecOps.Middleware.CspNonceMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.AddPreferredSecuritySchemes("Bearer");
        options.AddHttpAuthentication("Bearer", bearer =>
        {
            bearer.Token = "ejemplo-de-token";
        });
    });
}

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("db"),
    ResponseWriter = HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecksUI(options =>
{
    options.UIPath = "/health-ui";
    options.ApiPath = "/health-ui-api";
});

app.MapPrometheusScrapingEndpoint();

if (builder.Configuration.GetValue<bool>("EnableProviderStates"))
{
    app.MapPost("/provider-states", async (HttpContext context) =>
    {
        using var scope = context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();

        if (!await db.SegUsuario.AnyAsync(u => u.id == 1))
        {
            db.SegUsuario.Add(new SegUsuario
            {
                id = 1,
                strNombre = "admin",
                strPWD = hasher.HashPassword("Admin123!"),
                strCorreoElectronico = "admin@edelmeza.com",
                dteFechaRegistro = DateTime.UtcNow,
                bln2FAHabilitado = false
            });
        }

        if (!await db.CliCliente.AnyAsync(c => c.id == 1))
        {
            db.CliCliente.Add(new CliCliente
            {
                id = 1,
                strNombreCliente = "Cliente Demo",
                strDireccionCliente = "Av. Demo 123",
                strCorreoElectronico = "cliente@demo.com",
                strNumeroTelefono = "5551234567"
            });
        }

        if (!await db.ProProducto.AnyAsync(p => p.id == 1))
        {
            db.ProProducto.Add(new ProProducto
            {
                id = 1,
                strNombreProducto = "Coca Cola 600ml",
                strURLImagen = "https://imagen.com/coca.png",
                strDescripcion = "Refresco",
                intNumeroExistencia = 100,
                decPrecio = 18.5m,
                strCreadoPorUsuario = "admin"
            });
        }

        if (!await db.VenCatEstado.AnyAsync(e => e.id == 1))
        {
            db.VenCatEstado.Add(new VenCatEstado
            {
                id = 1,
                strValor = "ACTIVA",
                strDescripcion = "Venta activa"
            });
        }

        await db.SaveChangesAsync();
        return Results.Ok(new { });
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación terminó inesperadamente");
    Log.CloseAndFlush();
}

public partial class Program { }
