using CVManagement.Abstractions;
using CVManagement.Services;
using CVManagement.Infrastructure;
using CVManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Infrastructure;

internal static class DependencyInjection
{
    /// <summary>Single composition root: persistence, application services, startup initialiser.</summary>
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
