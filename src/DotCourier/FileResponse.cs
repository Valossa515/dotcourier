namespace DotCourier;

/// <summary>
/// A file a handler returns (<c>Response&lt;FileResponse&gt;</c>), such as an export or a generated PDF. The
/// ASP.NET Core integration sends it as a file download instead of JSON. Lives in the core package so
/// handlers do not depend on ASP.NET Core.
/// </summary>
public sealed class FileResponse
{
    /// <summary>A file from bytes already in memory.</summary>
    public FileResponse(byte[] content, string contentType, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        Content = content;
        ContentType = contentType;
        FileName = fileName;
    }

    /// <summary>A file read from <paramref name="stream"/> while it is sent, e.g. a large export; the stream is disposed afterwards.</summary>
    public FileResponse(Stream stream, string contentType, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        Stream = stream;
        ContentType = contentType;
        FileName = fileName;
    }

    /// <summary>The file's bytes, or <c>null</c> when it comes from <see cref="Stream"/>.</summary>
    public byte[]? Content { get; }

    /// <summary>The file's stream, or <c>null</c> when it comes from <see cref="Content"/>.</summary>
    public Stream? Stream { get; }

    /// <summary>MIME type, e.g. <c>text/csv</c> or <c>application/pdf</c>.</summary>
    public string ContentType { get; }

    /// <summary>Download name; when set, the browser saves the file instead of showing it.</summary>
    public string? FileName { get; }
}
