using Amazon.S3;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Interfaces;
using TorreClou.S3.Worker.Interfaces;

namespace TorreClou.S3.Worker.Services
{
    /// <summary>
    /// Factory for creating S3ResumableUploadService instances with user-specific S3 clients
    /// </summary>
    public class S3ResumableUploadServiceFactory : IS3ResumableUploadServiceFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public S3ResumableUploadServiceFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public IS3ResumableUploadService Create(IAmazonS3 s3Client)
        {
            if (s3Client == null)
                throw new ArgumentNullException(nameof(s3Client));

            var logger = _loggerFactory.CreateLogger<S3ResumableUploadService>();
            return new S3ResumableUploadService(s3Client, logger);
        }
    }
}
