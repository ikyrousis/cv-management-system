using CVManagement.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CVManagement.Data;

/// EF caches one model per context type, and the encryption value converters built while that model
/// was created capture a specific encryptor instance. Including the instance in the cache key keeps a
/// context that carries a different key from reusing a model bound to someone else's key.
internal sealed class EncryptedModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context switch
        {
            ApplicationDbContext encrypted => new EncryptedModelCacheKey(context.GetType(), designTime, encrypted.Encryptor),
            _ => new ModelCacheKey(context, designTime)
        };

    private sealed record EncryptedModelCacheKey(Type ContextType, bool DesignTime, IEncryptor Encryptor);
}
