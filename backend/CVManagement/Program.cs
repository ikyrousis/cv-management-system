using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.Repositories;
using CVManagement.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

const long CvUploadMaxBytes = 20 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<ICandidateRepository, CandidateRepository>();
builder.Services.AddScoped<IDegreeRepository, DegreeRepository>();

builder.Services.AddScoped<ICandidateService, CandidateService>();
builder.Services.AddScoped<IDegreeService, DegreeService>();

builder.Services.AddScoped<IDatabaseInitialiser, DatabaseInitialiser>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

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

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IDatabaseInitialiser>().InitialiseAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowReactApp");

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

await app.RunAsync();

public partial class Program;
