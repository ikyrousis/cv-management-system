using Xunit;

namespace CVManagement.Tests.Integration;

[Collection(SqlApiCollection.Name)]
public sealed class CandidateDegreeLifecycleTests
{
    private readonly SqlApiFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CandidateDegreeLifecycleTests(SqlApiFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private bool SkipWithoutDocker()
    {
        if (!SqlApiFixture.DockerAvailable || _fixture.Factory is null)
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

        var client = _fixture.Factory!.CreateClient();

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

        var client = _fixture.Factory!.CreateClient();

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
                    Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
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

        using var rejected = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(new byte[] { 9 })
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
                },
                "file",
                "bad.exe"
            }
        };
        var rejectedUpload = await client.PostAsync($"/api/candidates/{candidate.Id}/cv", rejected);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedUpload.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_ReportsHealthy()
    {
        if (SkipWithoutDocker())
        {
            return;
        }

        var client = _fixture.Factory!.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    private sealed record DegreePayload(int Id, string Name);

    private sealed record CandidatePayload(int Id, string FirstName, string LastName, string Email);
}
