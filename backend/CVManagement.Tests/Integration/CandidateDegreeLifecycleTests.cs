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

    [Fact]
    public async Task Pagination_SlicesResultsAndReportsTotal()
    {
        if (SkipWithoutDocker())
        {
            return;
        }

        var client = _fixture.Factory!.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = new List<int>();
        for (var i = 1; i <= 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/degrees", new { name = $"Paged {suffix} {i}" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var degree = await response.Content.ReadFromJsonAsync<DegreePayload>();
            created.Add(degree!.Id);
        }

        var plain = await client.GetFromJsonAsync<List<DegreePayload>>("/api/degrees");
        Assert.True(plain!.Count >= 3);

        var paged = await client.GetAsync("/api/degrees?page=1&pageSize=2");
        paged.EnsureSuccessStatusCode();
        Assert.True(paged.Headers.TryGetValues("X-Total-Count", out var totals));
        Assert.True(int.Parse(totals!.Single()) >= 3);
        var items = await paged.Content.ReadFromJsonAsync<List<DegreePayload>>();
        Assert.Equal(2, items!.Count);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/degrees?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/degrees?pageSize=101")).StatusCode);

        foreach (var id in created)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/degrees/{id}")).StatusCode);
        }
    }

    [Fact]
    public async Task ConcurrentCreateSameName_ExactlyOneSucceeds()
    {
        if (SkipWithoutDocker())
        {
            return;
        }

        var client = _fixture.Factory!.CreateClient();
        var name = $"Race {Guid.NewGuid().ToString("N")[..12]}";

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/degrees", new { name }),
            client.PostAsJsonAsync("/api/degrees", new { name }));

        var codes = responses.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.Created));
        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.BadRequest));

        var list = await client.GetFromJsonAsync<List<DegreePayload>>("/api/degrees");
        var survivor = list!.Single(d => d.Name == name);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/degrees/{survivor.Id}")).StatusCode);
    }

    [Fact]
    public async Task SensitiveFields_StoredAsCiphertextServedAsPlaintext()
    {
        if (SkipWithoutDocker())
        {
            return;
        }

        var client = _fixture.Factory!.CreateClient();
        var email = $"probe-{Guid.NewGuid():N}@example.com";

        var createResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Cipher",
            lastName = "Proof",
            email,
            mobile = "5556667777"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        Assert.Equal(email, created!.Email);

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Email, FirstName FROM Candidates WHERE Id = @id";
        command.Parameters.AddWithValue("@id", created.Id);

        using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var storedEmail = reader.GetString(0);
        var storedFirstName = reader.GetString(1);

        Assert.NotEqual(email, storedEmail);
        Assert.DoesNotContain("@", storedEmail);
        Assert.NotEqual("Cipher", storedFirstName);
        Convert.FromBase64String(storedEmail);
    }

    private sealed record DegreePayload(int Id, string Name);

    private sealed record CandidatePayload(int Id, string FirstName, string LastName, string Email);
}
