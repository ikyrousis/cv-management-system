using System.Security.Cryptography;
using System.Text;
using CVManagement.DTOs;
using Xunit;

namespace CVManagement.Tests.Integration;

[Collection(SqlApiCollection.Name)]
public sealed class CandidateDegreeLifecycleTests
{
    private readonly SqlApiFixture _fixture;

    public CandidateDegreeLifecycleTests(SqlApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FullLifecycle_DegreeBlocksWhileAssigned_ThenCleansUp()
    {
        var client = _fixture.Factory.CreateClient();

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
        Assert.Contains("assigned", await blockedDelete.Content.ReadAsStringAsync());

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
        var client = _fixture.Factory.CreateClient();

        var candidateResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Grace",
            lastName = "Hopper",
            email = "grace@example.com"
        });
        Assert.Equal(HttpStatusCode.Created, candidateResponse.StatusCode);
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        var content = new byte[] { 1, 2, 3 };

        using (var form = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(content)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
                },
                "file",
                "cv.pdf"
            }
        })
        {
            var upload = await client.PostAsync($"/api/candidates/{candidate!.Id}/cv", form);
            upload.EnsureSuccessStatusCode();
        }

        var download = await client.GetAsync($"/api/candidates/{candidate.Id}/cv");
        download.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());

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

        await client.DeleteAsync($"/api/candidates/{candidate.Id}");
    }

    [Fact]
    public async Task HealthEndpoint_ReportsHealthy()
    {
        var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pagination_SlicesResultsAndReportsTotal()
    {
        var client = _fixture.Factory.CreateClient();

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
    public async Task Pagination_BeyondLastPage_ReturnsEmptyWithTotal()
    {
        var client = _fixture.Factory.CreateClient();

        var page = await client.GetAsync("/api/degrees?page=2147483647&pageSize=100");

        page.EnsureSuccessStatusCode();
        var items = await page.Content.ReadFromJsonAsync<List<DegreePayload>>();
        Assert.Empty(items!);
        Assert.True(page.Headers.TryGetValues("X-Total-Count", out _));
    }

    [Fact]
    public async Task ConcurrentCreateSameName_ExactlyOneSucceeds()
    {
        var client = _fixture.Factory.CreateClient();
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
        var client = _fixture.Factory.CreateClient();
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
        command.CommandText = "SELECT Email, FirstName, Mobile FROM Candidates WHERE Id = @id";
        command.Parameters.AddWithValue("@id", created.Id);

        using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var storedEmail = reader.GetString(0);
        var storedFirstName = reader.GetString(1);
        var storedMobile = reader.GetString(2);

        Assert.NotEqual(email, storedEmail);
        Assert.DoesNotContain("@", storedEmail);
        Assert.NotEqual("Cipher", storedFirstName);
        Assert.NotEqual("5556667777", storedMobile);
        Convert.FromBase64String(storedEmail);

        await client.DeleteAsync($"/api/candidates/{created.Id}");
    }

    [Fact]
    public async Task CvBytes_StoredEncryptedAndDecryptedForDownload()
    {
        var client = _fixture.Factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Byte",
            lastName = "Vault",
            email = $"cv-{Guid.NewGuid():N}@example.com"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        var original = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        using (var form = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(original)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
                },
                "file",
                "secret-cv.pdf"
            }
        })
        {
            (await client.PostAsync($"/api/candidates/{created!.Id}/cv", form)).EnsureSuccessStatusCode();
        }

        byte[] stored;
        using (var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CVFile, CVFileName FROM Candidates WHERE Id = @id";
            command.Parameters.AddWithValue("@id", created.Id);
            using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            stored = (byte[])reader[0];
            var storedName = reader.GetString(1);
            Assert.NotEqual("secret-cv.pdf", storedName);
            Convert.FromBase64String(storedName);
        }

        // marker(1) + nonce(12) + tag(16) + ciphertext(len): the column is larger than the original and shares no prefix.
        Assert.Equal(29 + original.Length, stored.Length);
        Assert.False(original.AsSpan().SequenceEqual(stored.AsSpan(0, original.Length)));

        var download = await client.GetAsync($"/api/candidates/{created.Id}/cv");
        download.EnsureSuccessStatusCode();
        Assert.Equal(original, await download.Content.ReadAsByteArrayAsync());

        await client.DeleteAsync($"/api/candidates/{created.Id}");
    }

    [Fact]
    public async Task NullOptionalFields_StoredAsNullNotAsEncryptedEmpty()
    {
        var client = _fixture.Factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Null",
            lastName = "Safe",
            email = $"null-{Guid.NewGuid():N}@example.com"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Mobile, CVFile FROM Candidates WHERE Id = @id";
        command.Parameters.AddWithValue("@id", created!.Id);
        using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.True(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));

        await client.DeleteAsync($"/api/candidates/{created.Id}");
    }

    [Fact]
    public async Task UnreadablePersonalFields_RowIsStillDeletable()
    {
        var client = _fixture.Factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Unreadable",
            lastName = "Deletable",
            email = $"unreadable-{Guid.NewGuid():N}@example.com"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        await ExecuteAsync("UPDATE Candidates SET Email = N'not-ciphertext' WHERE Id = @id", created!.Id);

        // The row really is unreadable through the normal read path.
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync($"/api/candidates/{created.Id}")).StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/candidates/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM Candidates WHERE Id = @id", created.Id));
    }

    [Fact]
    public async Task UnreadableCvFileName_CvIsStillClearable()
    {
        var client = _fixture.Factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Cv",
            lastName = "Clearable",
            email = $"cv-clear-{Guid.NewGuid():N}@example.com"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        using (var form = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 })
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
                },
                "file",
                "stuck.pdf"
            }
        })
        {
            (await client.PostAsync($"/api/candidates/{created!.Id}/cv", form)).EnsureSuccessStatusCode();
        }

        await ExecuteAsync("UPDATE Candidates SET CVFileName = N'not-ciphertext' WHERE Id = @id", created.Id);

        var clearResponse = await client.DeleteAsync($"/api/candidates/{created.Id}/cv");

        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM Candidates WHERE Id = @id AND CVFile IS NOT NULL", created.Id));

        // With the CV columns cleared the row is readable again through the ordinary read path.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/candidates/{created.Id}")).StatusCode);

        await client.DeleteAsync($"/api/candidates/{created.Id}");
    }

    [Fact]
    public async Task DecryptFault_AnswersWithAFixedProblemDetailsBodyAndNoStoredContent()
    {
        var client = _fixture.Factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Leak",
            lastName = "Probe",
            email = $"leak-{Guid.NewGuid():N}@example.com"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        await ExecuteAsync("UPDATE Candidates SET Email = N'not-ciphertext' WHERE Id = @id", created!.Id);

        var response = await client.GetAsync($"/api/candidates/{created.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Stored candidate data could not be read", body);

        // The replaced stored value and the candidate's own identity must not travel back to the caller.
        Assert.DoesNotContain("not-ciphertext", body);
        Assert.DoesNotContain(created.FirstName, body);

        await client.DeleteAsync($"/api/candidates/{created.Id}");
    }

    [Fact]
    public async Task CiphertextCopiedBetweenColumns_IsRejectedOnRead()
    {
        var client = _fixture.Factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Bind",
            lastName = "Probe",
            email = $"bind-{Guid.NewGuid():N}@example.com"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        // Each value is authenticated against the column it was written to, so a copy into another column
        // does not verify even though the key and the payload are untouched.
        await ExecuteAsync("UPDATE Candidates SET FirstName = Email WHERE Id = @id", created!.Id);

        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync($"/api/candidates/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync("/api/candidates")).StatusCode);

        await client.DeleteAsync($"/api/candidates/{created.Id}");
    }

    [Fact]
    public async Task UnversionedPayloadWrittenBeforeVersioning_IsStillServedThroughTheApi()
    {
        var client = _fixture.Factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/candidates", new
        {
            firstName = "Legacy",
            lastName = "Reader",
            email = "placeholder@example.com"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        // The unversioned layout: nonce | tag | ciphertext, no marker and no authenticated context.
        var email = $"legacy-{Guid.NewGuid():N}@example.com";
        var plaintext = Encoding.UTF8.GetBytes(email);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(_fixture.EncryptionKeyBytes, 16))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var legacy = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(legacy, 0);
        tag.CopyTo(legacy, 12);
        ciphertext.CopyTo(legacy, 28);

        await ExecuteAsync("UPDATE Candidates SET Email = @value WHERE Id = @id", created!.Id, Convert.ToBase64String(legacy));

        var read = await client.GetAsync($"/api/candidates/{created.Id}");
        read.EnsureSuccessStatusCode();

        Assert.Equal(email, (await read.Content.ReadFromJsonAsync<CandidatePayload>())!.Email);

        await client.DeleteAsync($"/api/candidates/{created.Id}");
    }

    private async Task ExecuteAsync(string sql, int id, string? value = null)
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", id);

        if (value is not null)
        {
            command.Parameters.AddWithValue("@value", value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string sql, int id)
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", id);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record DegreePayload(int Id, string Name);

    private sealed record CandidatePayload(int Id, string FirstName, string LastName, string Email);
}

