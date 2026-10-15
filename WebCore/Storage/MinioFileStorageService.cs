using System.Collections.Concurrent;
using Microservices.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace Microservices.WebCore.Storage;

/// <summary>
/// Shared MinIO Object Storage implementation using pooled/singleton IMinioClient.
/// </summary>
public class MinioFileStorageService(
    IMinioClient minioClient,
    IConfiguration configuration,
    ILogger<MinioFileStorageService> logger) : IFileStorageService
{
    private static readonly ConcurrentDictionary<string, bool> EnsuredBuckets = new();
    private readonly string _endpoint = configuration["Minio:Endpoint"] ?? "localhost:9000";

    public string BuildObjectKey(long tenantId, string entityName, Guid fileGuid, string extension)
    {
        var ext = extension.TrimStart('.');
        var year = DateTime.UtcNow.Year;
        return $"{tenantId}/{entityName}/{year}/{fileGuid}.{ext}";
    }

    public async Task<string> UploadBytesAsync(
        string bucketName,
        string objectKey,
        byte[] fileBytes,
        string contentType = "application/octet-stream",
        CancellationToken ct = default)
    {
        using var stream = new MemoryStream(fileBytes);
        return await UploadStreamAsync(bucketName, objectKey, stream, fileBytes.Length, contentType, ct);
    }

    public async Task<string> UploadStreamAsync(
        string bucketName,
        string objectKey,
        Stream stream,
        long size,
        string contentType = "application/octet-stream",
        CancellationToken ct = default)
    {
        try
        {
            await EnsureBucketAsync(bucketName, ct);

            var putObjectArgs = new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(size)
                .WithContentType(contentType);

            await minioClient.PutObjectAsync(putObjectArgs, ct);
            logger.LogInformation("MinIO file streamed: Bucket={Bucket}, ObjectKey={ObjectKey}, Size={Size}", bucketName, objectKey, size);
            return objectKey;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MinIO stream upload failed for Bucket={Bucket}, ObjectKey={ObjectKey}", bucketName, objectKey);
            throw new InvalidOperationException($"MinIO file upload failed for '{objectKey}': {ex.Message}", ex);
        }
    }

    public async Task DownloadStreamAsync(
        string bucketName,
        string objectKey,
        Stream destination,
        CancellationToken ct = default)
    {
        try
        {
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithCallbackStream(async (stream, token) =>
                {
                    await stream.CopyToAsync(destination, token);
                });

            await minioClient.GetObjectAsync(getObjectArgs, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MinIO download failed for Bucket={Bucket}, ObjectKey={ObjectKey}", bucketName, objectKey);
            throw new InvalidOperationException($"MinIO file download failed for '{objectKey}': {ex.Message}", ex);
        }
    }

    public async Task DeleteObjectAsync(
        string bucketName,
        string objectKey,
        CancellationToken ct = default)
    {
        try
        {
            var removeArgs = new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey);

            await minioClient.RemoveObjectAsync(removeArgs, ct);
            logger.LogInformation("MinIO file deleted: Bucket={Bucket}, ObjectKey={ObjectKey}", bucketName, objectKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MinIO delete failed for Bucket={Bucket}, ObjectKey={ObjectKey}", bucketName, objectKey);
            throw new InvalidOperationException($"MinIO file delete failed for '{objectKey}': {ex.Message}", ex);
        }
    }

    public async Task<string> GenerateDownloadUrlAsync(
        string bucketName,
        string objectKey,
        int expiryMinutes = 15,
        string? fallbackRelativePath = null,
        CancellationToken ct = default)
    {
        try
        {
            var args = new PresignedGetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithExpiry(expiryMinutes * 60);

            return await minioClient.PresignedGetObjectAsync(args);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pre-signed URL generation failed, returning fallback URL: {ObjectKey}", objectKey);
            return fallbackRelativePath ?? $"http://{_endpoint}/{bucketName}/{objectKey}?presigned=1&expires={expiryMinutes * 60}";
        }
    }

    private async Task EnsureBucketAsync(string bucketName, CancellationToken ct)
    {
        if (EnsuredBuckets.ContainsKey(bucketName))
            return;

        var bucketExistsArgs = new BucketExistsArgs().WithBucket(bucketName);
        var found = await minioClient.BucketExistsAsync(bucketExistsArgs, ct);
        if (!found)
        {
            var makeBucketArgs = new MakeBucketArgs().WithBucket(bucketName);
            await minioClient.MakeBucketAsync(makeBucketArgs, ct);
        }

        EnsuredBuckets.TryAdd(bucketName, true);
    }
}
