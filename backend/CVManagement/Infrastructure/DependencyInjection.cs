using CVManagement.Application.Abstractions;
using CVManagement.Application.Services;
using CVManagement.Infrastructure;
using CVManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Single composition root for the backend: persistence, the application services that use it,
    /// and the startup initialiser.
    /// </summary>
    public static IServiceCollection AddBackend(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<IDegreeRepository, DegreeRepository>();

        services.AddScoped<ICandidateService, CandidateService>();
        services.AddScoped<IDegreeService, DegreeService>();

        services.AddScoped<IDatabaseInitialiser, DatabaseInitialiser>();

        return services;
    }
}
