namespace CVManagement.DTOs;

/// Uploaded CV content; keeps transport types out of the service layer.
public sealed record CvUpload(byte[] Content, string FileName, string ContentType);

/// Stored CV content for download, shaped for query projection.
public sealed class CvFileDto
{
    public required byte[] Content { get; init; }

    public string? FileName { get; init; }

    public string? ContentType { get; init; }
}
