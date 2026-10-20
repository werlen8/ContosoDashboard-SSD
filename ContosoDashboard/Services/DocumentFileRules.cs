namespace ContosoDashboard.Services;

// Whitelist, canonical MIME types and magic-byte signatures for accepted document types.
public static class DocumentFileRules
{
    public const long MaxFileSizeBytes = 26_214_400; // 25 MB

    public const int SignatureHeaderLength = 8192;

    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] Ole2 = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    public static bool IsAllowedExtension(string extension) => MimeTypes.ContainsKey(extension);

    public static string GetCanonicalMimeType(string extension) => MimeTypes[extension];

    public static bool HasValidSignature(string extension, ReadOnlySpan<byte> header)
    {
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => header.StartsWith(Pdf),
            ".docx" or ".xlsx" or ".pptx" => header.StartsWith(Zip),
            ".doc" or ".xls" or ".ppt" => header.StartsWith(Ole2),
            ".jpg" or ".jpeg" => header.StartsWith(Jpeg),
            ".png" => header.StartsWith(Png),
            ".txt" => header.IndexOf((byte)0) < 0,
            _ => false
        };
    }

    public static bool IsPreviewable(string mimeType) =>
        mimeType is "application/pdf" or "image/jpeg" or "image/png";
}
