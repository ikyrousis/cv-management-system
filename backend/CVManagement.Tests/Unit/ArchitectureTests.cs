using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Tests.Unit;

/// Controllers may depend only on service and DTO types, never on persistence.
public sealed class ArchitectureTests
{
    [Fact]
    public void Controllers_HaveNoPersistenceDependencies()
    {
        var assembly = typeof(Program).Assembly;

        var controllers = assembly.GetTypes()
            .Where(t => t.Namespace == "CVManagement.Controllers")
            .ToList();

        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            foreach (var parameter in controller.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                Assert.False(
                    parameter.ParameterType.Namespace is "CVManagement.Data" or "CVManagement.Repositories" or "CVManagement.Abstractions"
                        || parameter.ParameterType.Namespace?.StartsWith("CVManagement.Data.") == true
                        || parameter.ParameterType.Namespace?.StartsWith("CVManagement.Repositories.") == true
                        || parameter.ParameterType.Namespace?.StartsWith("CVManagement.Abstractions.") == true,
                    $"{controller.Name} takes {parameter.ParameterType.FullName}, which is a persistence type.");
            }
        }
    }

    /// An equality or ordering predicate over an encrypted column is accepted by EF and answers with
    /// nothing or with ciphertext order, so such a query is a defect rather than a feature. Keeping the
    /// queryable surface inside persistence means every predicate over those columns is reviewable there.
    [Fact]
    public void StoreQuerySurfaces_StayInsidePersistence()
    {
        var assembly = typeof(Program).Assembly;

        var outwardTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not ("CVManagement.Data" or "CVManagement.Repositories"))
            .Where(t => !IsCompilerGenerated(t))
            .ToList();

        Assert.NotEmpty(outwardTypes);

        foreach (var type in outwardTypes)
        {
            foreach (var referenced in PublicSurface(type))
            {
                Assert.False(
                    IsStoreType(referenced),
                    $"{type.FullName} exposes {Describe(referenced)}, which would let callers query the store directly.");
            }
        }
    }

    private static bool IsCompilerGenerated(Type type)
        => type.IsDefined(typeof(CompilerGeneratedAttribute), false) || type.Name.Contains('<');

    private static IEnumerable<Type> PublicSurface(Type type)
    {
        const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(Members))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(Members))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var property in type.GetProperties(Members))
        {
            yield return property.PropertyType;
        }
    }

    private static string Describe(Type type)
        => type.FullName ?? $"{type.Namespace}.{type.Name} (unbound generic)";

    private static bool IsStoreType(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;

        return type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true
            || definition == typeof(IQueryable)
            || definition == typeof(IQueryable<>)
            || definition == typeof(IOrderedQueryable)
            || definition == typeof(IOrderedQueryable<>);
    }
}
