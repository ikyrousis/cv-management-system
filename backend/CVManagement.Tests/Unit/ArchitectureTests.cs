using System.Reflection;

namespace CVManagement.Tests.Unit;

/// <summary>Controllers may depend only on service and DTO types, never on persistence.</summary>
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
}
