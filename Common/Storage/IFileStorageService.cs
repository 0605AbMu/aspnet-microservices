namespace Microservices.Common.Storage;

public interface IFileStorageService
{
    string BuildObjectKey(long tenantId, string entityName, Guid fileGuid, string extension);

    Task<string> UploadBytesAsync(
        string bucketName,
        string objectKey,
        byte[] fileBytes,
        string contentType = "application/octet-stream",
        CancellationToken ct = default);

    Task<string> GenerateDownloadUrlAsync(
        string bucketName,
        string objectKey,
        int expiryMinutes = 15,
        string? fallbackRelativePath = null,
        CancellationToken ct = default);
}
