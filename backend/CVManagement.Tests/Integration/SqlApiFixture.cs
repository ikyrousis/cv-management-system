using System.IO.Pipes;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MsSql;

namespace CVManagement.Tests.Integration;

/// <summary>Owns the SQL Server container for the whole test run and the API factory wired to it.</summary>
public sealed class SqlApiFixture : IAsyncLifetime
{
    private const string ConnectionStringKey = "ConnectionStrings__CVManagement";

    private MsSqlContainer? _sql;

    public static bool DockerAvailable { get; } = ProbeDocker();

    public ApiFactory? Factory { get; private set; }

    public async Task InitializeAsync()
    {
        if (!DockerAvailable)
        {
            return;
        }

        // Built lazily: MsSqlBuilder.Build() throws when Docker is unreachable.
        _sql = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _sql.StartAsync();

        // Program.cs reads config at startup, before factory hooks apply, so inject via environment.
        Environment.SetEnvironmentVariable(ConnectionStringKey, _sql.GetConnectionString());
        Factory = new ApiFactory();
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }

        Environment.SetEnvironmentVariable(ConnectionStringKey, null);
    }

    private static bool ProbeDocker()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", "docker_engine", PipeDirection.Out);
            pipe.Connect(TimeSpan.FromSeconds(2).Milliseconds);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlApiCollection : ICollectionFixture<SqlApiFixture>
{
    public const string Name = "SQL API";
}
