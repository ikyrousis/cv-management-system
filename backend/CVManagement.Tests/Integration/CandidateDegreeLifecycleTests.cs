using System.IO.Pipes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace CVManagement.Tests.Integration;

/// <summary>
/// Boots the real API pipeline against a disposable SQL Server container.
/// Every test class sharing this fixture reuses one container.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Built lazily because <see cref="MsSqlBuilder.Build"/> itself throws when Docker is unreachable,
    /// and the skip path must stay usable without Docker.
    /// </summary>
    private MsSqlContainer? _sql;

    public static bool DockerAvailable { get; } = ProbeDocker();

    public async Task InitializeAsync()
    {
        if (!DockerAvailable)
        {
            return;
        }

        _sql = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _sql.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_sql is null)
        {
            return;
        }

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CVManagement"] = _sql.GetConnectionString()
            });
        });
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

public sealed class CandidateDegreeLifecycleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public CandidateDegreeLifecycleTests(ApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private bool SkipWithoutDocker()
    {
        if (!ApiFactory.DockerAvailable)
        {
            _output.WriteLine("SKIPPED: Docker is not running; start Docker Desktop to execute integration tests.");
            return true;
        }

        return false;
    }

    [Fact]
    public async Task FullLifecycle_DegreeBlocksWhileAssigned_ThenCleansUp()
    {
        if (SkipWithoutDocker())
        {
            return;
        }

        var client = _factory.CreateClient();

        var degreeResponse = await client.PostAsJsonAsync("/api/degrees", new { name = "BSc Integration" });
        degreeResponse.EnsureSuccessStatusCode();
        var degree = await degreeResponse.Content.ReadFromJsonAsync<DegreePayload>();
        Assert.NotNull(degree);

        var candidateResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Ada",
            lastName = "Lovelace",
            email = "ada@example.com",
            mobile = "1234567890",
            degreeId = degree!.Id
        });
        Assert.Equal(HttpStatusCode.Created, candidateResponse.StatusCode);
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<CandidatePayload>();
        Assert.NotNull(candidate);

        var blockedDelete = await client.DeleteAsync($"/api/degrees/{degree.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, blockedDelete.StatusCode);

        var deleteCandidate = await client.DeleteAsync($"/api/candidates/{candidate!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteCandidate.StatusCode);

        var deleteDegree = await client.DeleteAsync($"/api/degrees/{degree.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteDegree.StatusCode);

        var missing = await client.GetAsync($"/api/candidates/{candidate.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task UploadAndDownloadCv_RoundTrips()
    {
        if (SkipWithoutDocker())
        {
            return;
        }

        var client = _factory.CreateClient();

        var candidateResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Grace",
            lastName = "Hopper",
            email = "grace@example.com"
        });
        Assert.Equal(HttpStatusCode.Created, candidateResponse.StatusCode);
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        using var form = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(new byte[] { 1, 2, 3 })
                {
                    Headers = { ContentType = new("application/pdf") }
                },
                "file",
                "cv.pdf"
            }
        };
        var upload = await client.PostAsync($"/api/candidates/{candidate!.Id}/cv", form);
        upload.EnsureSuccessStatusCode();

        var download = await client.GetAsync($"/api/candidates/{candidate.Id}/cv");
        download.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await download.Content.ReadAsByteArrayAsync());

        var rejected = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(new byte[] { 9 })
                {
                    Headers = { ContentType = new("application/octet-stream") }
                },
                "file",
                "bad.exe"
            }
        };
        var rejectedUpload = await client.PostAsync($"/api/candidates/{candidate.Id}/cv", rejected);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedUpload.StatusCode);
    }

    private sealed record DegreePayload(int Id, string Name);

    private sealed record CandidatePayload(int Id, string FirstName, string LastName, string Email);
}
