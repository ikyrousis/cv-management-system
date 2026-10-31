using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CVManagement.Data;

/// Options wiring for the encrypted store. Composition root and tests both go through here so the
/// encryption-aware model cache key is never configured in one place and forgotten in another.
internal static class EncryptedDbContextOptions
{
    public static DbContextOptionsBuilder UseEncryptedSqlServer(
        this DbContextOptionsBuilder builder,
        string connectionString)
    {
        builder.UseSqlServer(connectionString);
        builder.ReplaceService<IModelCacheKeyFactory, EncryptedModelCacheKeyFactory>();

        return builder;
    }
}
