using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IS3ResumableUploadService
    {
        /// <summary>
        /// Initiates a multipart upload and returns the UploadId
        /// </summary>
        Task<Result<string>> InitiateUploadAsync(string bucketName, string s3Key, long fileSize, string? contentType = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads a single part of a multipart upload
        /// </summary>
        Task<Result<PartETag>> UploadPartAsync(string bucketName, string s3Key, string uploadId, int partNumber, Stream partData, CancellationToken cancellationToken = default);

        /// <summary>
        /// Completes a multipart upload
        /// </summary>
        Task<Result> CompleteUploadAsync(string bucketName, string s3Key, string uploadId, List<PartETag> parts, CancellationToken cancellationToken = default);

        /// <summary>
        /// Aborts a multipart upload
        /// </summary>
        Task<Result> AbortUploadAsync(string bucketName, string s3Key, string uploadId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists all parts of an in-progress multipart upload
        /// </summary>
        Task<Result<List<PartETag>>> ListPartsAsync(string bucketName, string s3Key, string uploadId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if an object already exists in S3
        /// </summary>
        Task<Result<bool>> CheckObjectExistsAsync(string bucketName, string s3Key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads a file to S3 using multipart upload (convenience method that handles all the parts)
        /// </summary>
        Task<Result<string>> UploadFileAsync(string filePath, string credentialsJson, CancellationToken cancellationToken = default);
    }

    public class PartETag
    {
        public int PartNumber { get; set; }
        public string ETag { get; set; } = string.Empty;
    }
}

