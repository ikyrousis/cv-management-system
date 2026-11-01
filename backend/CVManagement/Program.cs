using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.Entities;
using CVManagement.Extensions;
using CVManagement.Repositories;
using CVManagement.Services;
using CVManagement.Validation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Threading.RateLimiting;

// Bootstrap logger covers failures before the host builds, such as a missing connection string.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Replaces the bootstrap logger with the configured one during host build.
    builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration));

    builder.Services.AddControllers();

    // The exception handler middleware requires a problem details service before it can be used without a path.
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<EncryptionExceptionHandler>();

    builder.Services.Configure<FormOptions>(options =>
    {
        // Cap uploads so one request cannot exhaust memory.
        options.MultipartBodyLengthLimit = CvLimits.MaxCvBytes;
        options.ValueLengthLimit = int.MaxValue;
        options.KeyLengthLimit = int.MaxValue;
    });

    builder.Services.AddHealthChecks()
        .AddCheck<SqlHealthCheck>("sqlserver");

    var connectionString = builder.Configuration.GetConnectionString("CVManagement")
        ?? throw new InvalidOperationException(
            "Connection string 'CVManagement' is not configured. Set it with " +
            "'dotnet user-secrets set ConnectionStrings:CVManagement <value>' (Development) " +
            "or the ConnectionStrings__CVManagement environment variable.");

    var encryptionKey = builder.Configuration["CvEncryption:Key"]
        ?? throw new InvalidOperationException(
            "Encryption key 'CvEncryption:Key' is not configured. Set it with " +
            "'dotnet user-secrets set CvEncryption:Key <base64>' (Development) " +
            "or the CvEncryption__Key environment variable. Generate one with 'openssl rand -base64 32'.");

    byte[] encryptionKeyBytes;
    try
    {
        encryptionKeyBytes = Convert.FromBase64String(encryptionKey);
    }
    catch (FormatException)
    {
        throw new InvalidOperationException("CvEncryption:Key must be a base64-encoded 32-byte key.");
    }

    builder.Services.AddSingleton<IEncryptor>(_ => new AesGcmEncryptor(encryptionKeyBytes));

    // The auth configuration follows the same rules as the encryption key: fail fast when it is
    // missing, because an API that silently boots with no authentication is the worst outcome.
    var authOptions = new AuthOptions();
    builder.Configuration.GetSection(AuthOptions.SectionName).Bind(authOptions);

    if (string.IsNullOrWhiteSpace(authOptions.SigningKey))
    {
        throw new InvalidOperationException(
            "Auth signing key 'CvAuth:SigningKey' is not configured. Set it with " +
            "'dotnet user-secrets set CvAuth:SigningKey <base64>' (Development) or the " +
            "CvAuth__SigningKey environment variable. Generate one with 'openssl rand -base64 32'.");
    }

    byte[] signingKeyBytes;
    try
    {
        signingKeyBytes = Convert.FromBase64String(authOptions.SigningKey);
    }
    catch (FormatException)
    {
        throw new InvalidOperationException("CvAuth:SigningKey must be a base64-encoded key.");
    }

    if (signingKeyBytes.Length < 32)
    {
        throw new InvalidOperationException("CvAuth:SigningKey must be at least 32 bytes for HS256.");
    }

    if (signingKeyBytes.SequenceEqual(encryptionKeyBytes))
    {
        throw new InvalidOperationException(
            "CvAuth:SigningKey must differ from CvEncryption:Key. Sharing one value would let anyone " +
            "who can decrypt stored data also forge authentication tokens.");
    }


    if (string.IsNullOrWhiteSpace(authOptions.Issuer) || string.IsNullOrWhiteSpace(authOptions.Audience))
    {
        throw new InvalidOperationException(
            "CvAuth:Issuer and CvAuth:Audience must be configured so tokens cannot be replayed across systems.");
    }

    if (authOptions.RefreshTokenLifetimeDays < 1)
    {
        throw new InvalidOperationException(
            "CvAuth:RefreshTokenLifetimeDays must be at least 1, so every issued credential is usable.");
    }

    var rateLimits = new RateLimitOptions();
    builder.Configuration.GetSection(RateLimitOptions.SectionName).Bind(rateLimits);

    if (rateLimits.AuthPermitLimit < 1 || rateLimits.AuthWindowSeconds < 1)
    {
        throw new InvalidOperationException(
            "RateLimit:AuthPermitLimit and RateLimit:AuthWindowSeconds must both be at least 1.");
    }

    builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseEncryptedSqlServer(connectionString));

    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<ICandidateRepository, CandidateRepository>();
    builder.Services.AddScoped<IDegreeRepository, DegreeRepository>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

    builder.Services.AddScoped<ICandidateService, CandidateService>();
    builder.Services.AddScoped<IDegreeService, DegreeService>();

    builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

    // Explicit work factor: hashing cost is a security decision, not a framework default.
    builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = AuthLimits.Pbkdf2Iterations);
    builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<ITokenProvider, JwtTokenProvider>();
    builder.Services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
    builder.Services.AddScoped<IAuthService, AuthService>();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            // The API only ever issues HS256; anything else in an incoming token header means a
            // crafted or downgraded token, and the allow-list rejects it.
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateLifetime = true,
            // Small skew so slightly unsynchronized clocks do not reject fresh tokens; it is not a
            // lifetime extension.
            ClockSkew = TimeSpan.FromMinutes(1)
        });

    builder.Services.AddAuthorization();

    // Login, bootstrap and refresh are the endpoints an unauthenticated stranger can hammer, so
    // they carry the only throttling policy. Partitioned by source address: one machine guessing
    // passwords exhausts its own budget, not everyone else's.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(RateLimitPolicy.Auth, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-address",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimits.AuthPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimits.AuthWindowSeconds),
                    QueueLimit = 0
                }));
    });

    builder.Services.AddScoped<IDatabaseInitialiser, DatabaseInitialiser>();

    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

    if (allowedOrigins is null || allowedOrigins.Length == 0)
    {
        throw new InvalidOperationException(
            "No CORS origins configured. Set 'Cors:AllowedOrigins' in configuration to the frontend URLs; " +
            "without it the frontend would be blocked with no clear cause.");
    }

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowReactApp", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials()
                  // Custom response headers are invisible to cross-origin scripts unless they are exposed.
                  .WithExposedHeaders("X-Total-Count");
        });
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    Log.Information("CVManagement API starting in {Environment} environment", app.Environment.EnvironmentName);

    app.UseExceptionHandler();

    app.UseSerilogRequestLogging();

    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider.GetRequiredService<IDatabaseInitialiser>().InitialiseAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors("AllowReactApp");

    app.UseAuthentication();

    app.UseAuthorization();

    app.UseRateLimiter();

    app.MapControllers();

    app.MapHealthChecks("/health");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
