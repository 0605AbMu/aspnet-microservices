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

    Task<string> UploadStreamAsync(
        string bucketName,
        string objectKey,
        Stream stream,
        long size,
        string contentType = "application/octet-stream",
        CancellationToken ct = default);

    Task DownloadStreamAsync(
        string bucketName,
        string objectKey,
        Stream destination,
        CancellationToken ct = default);

    Task DeleteObjectAsync(
        string bucketName,
        string objectKey,
        CancellationToken ct = default);

    Task<string> GenerateDownloadUrlAsync(
        string bucketName,
        string objectKey,
        int expiryMinutes = 15,
        string? fallbackRelativePath = null,
        CancellationToken ct = default);
}
