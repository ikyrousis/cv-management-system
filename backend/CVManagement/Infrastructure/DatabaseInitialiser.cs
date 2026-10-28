using CVManagement.Infrastructure.Persistence;

namespace CVManagement.Infrastructure;

public interface IDatabaseInitialiser
{
    Task InitialiseAsync(CancellationToken cancellationToken = default);
}

public sealed class DatabaseInitialiser : IDatabaseInitialiser
{
    private readonly ApplicationDbContext _context;

    public DatabaseInitialiser(ApplicationDbContext context) => _context = context;

    public Task InitialiseAsync(CancellationToken cancellationToken = default)
        => _context.Database.EnsureCreatedAsync(cancellationToken);
}
