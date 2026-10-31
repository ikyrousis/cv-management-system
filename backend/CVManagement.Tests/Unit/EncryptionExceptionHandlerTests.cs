using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CVManagement.Tests.Unit;

/// The fault classifier and response shape of the handler that renders unreadable stored ciphertext.
public sealed class EncryptionExceptionHandlerTests
{
    private static readonly EncryptionExceptionHandler Handler = new(new NullLogger<EncryptionExceptionHandler>());

    [Fact]
    public async Task WrappedCryptographicFault_ProducedByRealDecryption_IsHandledAsSanitizedServerError()
    {
        using var encryptor = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));

        Exception realFault = null!;
        try
        {
            encryptor.DecryptString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "Candidate.Email");
        }
        catch (Exception ex)
        {
            realFault = ex;
        }

        // Materialization wraps converter failures, so the handler has to see through the wrapper.
        var wrapped = new InvalidOperationException("An error occurred while materializing the results.", realFault);

        var context = NewContext();

        var handled = await Handler.TryHandleAsync(context, wrapped, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(EncryptionExceptionHandler.UnreadableDataMessage, ReadTitle(context));
    }

    [Fact]
    public async Task MalformedStoredPayloadFault_IsHandled()
    {
        using var encryptor = new AesGcmEncryptor(RandomNumberGenerator.GetBytes(32));

        var context = NewContext();

        Exception fault = null!;
        try
        {
            encryptor.DecryptString("!!!not-base64!!!", "Candidate.Email");
        }
        catch (Exception ex)
        {
            fault = ex;
        }

        Assert.True(await Handler.TryHandleAsync(context, fault, CancellationToken.None));
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Fact]
    public async Task WrappedUnreadableDataFault_IsHandledAndNamesNoContent()
    {
        var fault = new UnreadableStoredDataException("Stored candidate data could not be decrypted.", 42, new CryptographicException("tag mismatch"));
        var context = NewContext();

        Assert.True(await Handler.TryHandleAsync(context, fault, CancellationToken.None));
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain("42", ReadBody(context));
    }

    [Fact]
    public async Task UnrelatedFault_IsLeftForOtherHandlers()
    {
        var context = NewContext();

        var handled = await Handler.TryHandleAsync(context, new InvalidOperationException("unrelated"), CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandledFault_NeverEchoesStoredContentOrExceptionDetail()
    {
        var context = NewContext();
        var secret = "ada@example.com";

        var fault = new CryptographicException($"Failed to decrypt {secret}");

        Assert.True(await Handler.TryHandleAsync(context, fault, CancellationToken.None));

        Assert.DoesNotContain(secret, ReadBody(context));
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };

        context.Request.Method = "GET";
        context.Request.Path = "/api/candidates/7";

        return context;
    }

    private static string ReadBody(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;

        return new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    private static string? ReadTitle(DefaultHttpContext context)
        => JsonDocument.Parse(ReadBody(context)).RootElement.GetProperty("title").GetString();
}
