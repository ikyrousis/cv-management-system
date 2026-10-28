namespace CVManagement.Application.Dtos;

/// <summary>
/// Uploaded CV content handed to the service layer, so no transport type crosses the boundary.
/// </summary>
public sealed record CvUpload(byte[] Content, string FileName, string ContentType);

/// <summary>
/// Stored CV content returned for download. Shaped for query projection.
/// </summary>
public sealed class CvFileDto
{
    public required byte[] Content { get; init; }

    public string? FileName { get; init; }

    public string? ContentType { get; init; }
}
