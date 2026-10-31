using System.Security.Cryptography;
using CVManagement.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CVManagement.Extensions;

/// A fault raised while reading stored ciphertext means the data is unreadable, not that the request
/// was wrong. This renders it as a fixed 500 so the response never depends on the environment, and the
/// only thing identified in the log is the request path, which carries numeric ids.
public sealed class EncryptionExceptionHandler : IExceptionHandler
{
    internal const string UnreadableDataMessage = "Stored candidate data could not be read.";

    private readonly ILogger<EncryptionExceptionHandler> _logger;

    public EncryptionExceptionHandler(ILogger<EncryptionExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!TryClassify(exception, out var rowId))
        {
            return false;
        }

        if (rowId is null)
        {
            _logger.LogWarning(
                exception,
                "Stored ciphertext could not be decrypted for {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Stored ciphertext could not be decrypted for {Method} {Path}, candidate {CandidateId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                rowId);
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = UnreadableDataMessage
            },
            cancellationToken);

        return true;
    }

    /// The repository names the row when it can, so a wrapped fault is the expected shape. A raw
    /// decrypt fault is still caught here: nothing about stored content may reach a caller either way.
    private static bool TryClassify(Exception exception, out int? rowId)
    {
        rowId = null;

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is UnreadableStoredDataException unreadable)
            {
                rowId = unreadable.Id;
                return true;
            }

            if (current is CryptographicException or FormatException)
            {
                return true;
            }
        }

        return false;
    }
}
