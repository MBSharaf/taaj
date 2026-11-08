namespace LabStage.Application.Files;

/// <summary>
/// Validates dental case uploads (STL, PLY, OBJ, PNG, JPG/JPEG, PDF) by extension, size, and safe headers.
/// </summary>
public static class CaseFileValidator
{
    public const long MaxFileSizeBytes = 50L * 1024 * 1024; // 50 MB

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".stl", ".ply", ".obj", ".png", ".jpg", ".jpeg", ".pdf"
    };

    public static (bool IsValid, string? Error, string Extension) Validate(string fileName, long length, ReadOnlySpan<byte> header)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return (false, "اسم الملف غير صالح.", string.Empty);

        if (length <= 0)
            return (false, "الملف فارغ.", string.Empty);

        if (length > MaxFileSizeBytes)
            return (false, "حجم الملف يتجاوز الحد المسموح (50 ميجابايت).", string.Empty);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return (false, "امتداد الملف غير مسموح. الامتدادات المسموحة: STL, PLY, OBJ, PNG, JPG, PDF.", ext);

        // Magic-byte verification for binary formats to prevent disguised executables
        if (header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z')
            return (false, "نوع الملف التنفيذي مرفوض أمنيًا.", ext);

        if (ext == ".pdf" && (header.Length < 4 || !(header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F')))
            return (false, "محتوى ملف PDF غير مطابق للتوقيع القياسي.", ext);

        if (ext == ".png" && (header.Length < 4 || !(header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)))
            return (false, "محتوى صورة PNG غير مطابق للتوقيع القياسي.", ext);

        if ((ext == ".jpg" || ext == ".jpeg") && (header.Length < 3 || !(header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)))
            return (false, "محتوى صورة JPEG غير مطابق للتوقيع القياسي.", ext);

        if (ext == ".ply" && (header.Length < 3 || !(header[0] == 'p' && header[1] == 'l' && header[2] == 'y')))
            return (false, "محتوى ملف PLY غير مطابق للتوقيع القياسي.", ext);

        return (true, null, ext);
    }

    public static string ContentTypeFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".stl" => "model/stl",
        ".ply" => "application/octet-stream",
        ".obj" => "model/obj",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}
