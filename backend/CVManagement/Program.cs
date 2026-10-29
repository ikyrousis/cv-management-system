using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.Repositories;
using CVManagement.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Bootstrap logger covers failures before the host builds, such as a missing connection string.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    const long CvUploadMaxBytes = 20 * 1024 * 1024;

    var builder = WebApplication.CreateBuilder(args);

    // Replaces the bootstrap logger with the configured one during host build.
    builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration));

    builder.Services.AddControllers();

    builder.Services.Configure<FormOptions>(options =>
    {
        // Cap uploads so one request cannot exhaust memory.
        options.MultipartBodyLengthLimit = CvUploadMaxBytes;
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

    builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));

    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<ICandidateRepository, CandidateRepository>();
    builder.Services.AddScoped<IDegreeRepository, DegreeRepository>();

    builder.Services.AddScoped<ICandidateService, CandidateService>();
    builder.Services.AddScoped<IDegreeService, DegreeService>();

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
                  .AllowCredentials();
        });
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    Log.Information("CVManagement API starting in {Environment} environment", app.Environment.EnvironmentName);

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

    app.UseAuthorization();

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
