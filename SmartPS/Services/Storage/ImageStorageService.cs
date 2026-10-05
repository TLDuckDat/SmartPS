using System.IO;
using System.Text.RegularExpressions;

namespace SmartPS.Services.Storage;

public class ImageStorageService : IImageStorageService
{
    private readonly string _storageBasePath;

    public ImageStorageService(string? customBasePath = null)
    {
        _storageBasePath = !string.IsNullOrEmpty(customBasePath)
            ? customBasePath
            : Path.Combine(AppContext.BaseDirectory, "Storage", "Captures");

        Directory.CreateDirectory(_storageBasePath);
    }

    private static string SanitizeFileName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "UNKNOWN";
        return Regex.Replace(text, @"[^a-zA-Z0-9_-]", "").ToUpperInvariant();
    }

    public async Task<string?> ArchiveCaptureAsync(string? sourceImagePath, string category, string licensePlate)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
        {
            return sourceImagePath;
        }

        try
        {
            var dateDir = DateTime.UtcNow.ToString("yyyyMMdd");
            var cleanPlate = SanitizeFileName(licensePlate);
            var cleanCategory = SanitizeFileName(category);

            var targetFolder = Path.Combine(_storageBasePath, cleanCategory, dateDir);
            Directory.CreateDirectory(targetFolder);

            var ext = Path.GetExtension(sourceImagePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

            var timeStamp = DateTime.UtcNow.ToString("HHmmss_fff");
            var uniqueId = Guid.NewGuid().ToString("N")[..4];
            var targetFileName = $"{cleanPlate}_{timeStamp}_{uniqueId}{ext}";
            var targetFullPath = Path.Combine(targetFolder, targetFileName);

            // Sao chép tệp tin an toàn (kể cả khi tệp nguồn đang được mở đọc bởi luồng khác)
            await using (var sourceStream = new FileStream(sourceImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            await using (var destinationStream = new FileStream(targetFullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await sourceStream.CopyToAsync(destinationStream);
            }

            return targetFullPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageStorageService Error]: Không thể lưu trữ ảnh chụp '{sourceImagePath}': {ex.Message}");
            return sourceImagePath; // Fallback trả về ảnh gốc nếu không copy được
        }
    }
}
