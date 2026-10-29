namespace CVManagement.Abstractions;

/// Commits pending changes as one unit shared by the repositories in a scope.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
