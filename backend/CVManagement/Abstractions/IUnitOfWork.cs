namespace CVManagement.Abstractions;

/// <summary>Commits pending changes as one unit shared by the repositories in a scope.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
