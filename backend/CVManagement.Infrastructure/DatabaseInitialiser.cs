using CVManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CVManagement.Infrastructure;

public static class DatabaseInitialiser
{
    public static async Task EnsureCreatedAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.EnsureCreatedAsync();
    }
}
