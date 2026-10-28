namespace CVManagement.DTOs;

/// <summary>Uploaded CV content; keeps transport types out of the service layer.</summary>
public sealed record CvUpload(byte[] Content, string FileName, string ContentType);

/// <summary>Stored CV content for download, shaped for query projection.</summary>
public sealed class CvFileDto
{
    public required byte[] Content { get; init; }

    public string? FileName { get; init; }

    public string? ContentType { get; init; }
}
