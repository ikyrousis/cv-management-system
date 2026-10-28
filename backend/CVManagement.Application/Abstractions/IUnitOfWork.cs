namespace CVManagement.Application.Abstractions;

/// <summary>
/// Commits pending changes as a single unit of work shared by the repositories in a scope.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
