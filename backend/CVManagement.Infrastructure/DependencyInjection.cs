using CVManagement.Application.Abstractions;
using CVManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CVManagement.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence behind the application's own abstractions, so the choice of provider
    /// stays inside this assembly.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string databaseName)
    {
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<IDegreeRepository, DegreeRepository>();

        return services;
    }
}
