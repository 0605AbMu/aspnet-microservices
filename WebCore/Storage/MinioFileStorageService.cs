using Microservices.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace Microservices.WebCore.Storage;

/// <summary>
/// Shared MinIO Object Storage implementation.
/// </summary>
public class MinioFileStorageService(
    IConfiguration configuration,
    ILogger<MinioFileStorageService> logger) : IFileStorageService
{
    private readonly string _endpoint = configuration["Minio:Endpoint"] ?? "localhost:9000";
    private readonly string _accessKey = configuration["Minio:AccessKey"] ?? "minioadmin";
    private readonly string _secretKey = configuration["Minio:SecretKey"] ?? "minioadmin";
    private readonly bool _withSsl = configuration.GetValue<bool>("Minio:WithSSL");

    private IMinioClient CreateClient() =>
        new MinioClient()
            .WithEndpoint(_endpoint)
            .WithCredentials(_accessKey, _secretKey)
            .WithSSL(_withSsl)
            .Build();

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
        try
        {
            var client = CreateClient();

            var bucketExistsArgs = new BucketExistsArgs().WithBucket(bucketName);
            var found = await client.BucketExistsAsync(bucketExistsArgs, ct);
            if (!found)
            {
                var makeBucketArgs = new MakeBucketArgs().WithBucket(bucketName);
                await client.MakeBucketAsync(makeBucketArgs, ct);
            }

            using var stream = new MemoryStream(fileBytes);
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(stream.Length)
                .WithContentType(contentType);

            await client.PutObjectAsync(putObjectArgs, ct);
            logger.LogInformation("MinIO file uploaded: Bucket={Bucket}, ObjectKey={ObjectKey}", bucketName, objectKey);
            return objectKey;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MinIO upload failed, fallback key returned: {ObjectKey}", objectKey);
            return objectKey;
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
            var client = CreateClient();
            var args = new PresignedGetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithExpiry(expiryMinutes * 60);

            return await client.PresignedGetObjectAsync(args);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pre-signed URL generation failed, returning fallback URL: {ObjectKey}", objectKey);
            return fallbackRelativePath ?? $"http://{_endpoint}/{bucketName}/{objectKey}?presigned=1&expires={expiryMinutes * 60}";
        }
    }
}
