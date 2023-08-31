using System.IO;
using Amazon;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static System.Net.Mime.MediaTypeNames;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace imageResizer;

public class Function
{
    IAmazonS3 S3Client { get; set; }
    private const int TargetWidth = 800;
    private const int TargetHeight = 600;

    /// <summary>
    /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
    /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
    /// region the Lambda function is executed in.
    /// </summary>
    public Function()
    {
        S3Client = new AmazonS3Client();
    }

    /// <summary>
    /// Constructs an instance with a preconfigured S3 client. This can be used for testing outside of the Lambda environment.
    /// </summary>
    /// <param name="s3Client"></param>
    public Function(IAmazonS3 s3Client)
    {
        this.S3Client = s3Client;
    }

    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
    /// to respond to S3 notifications.
    /// </summary>
    /// <param name="evnt"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task FunctionHandler(S3Event evnt, ILambdaContext context)
    {
        var eventRecords = evnt.Records ?? new List<S3Event.S3EventNotificationRecord>();
        foreach (var record in eventRecords)
        {
            var s3Event = record.S3;
            if (s3Event == null)
            {
                continue;
            }

            try
            {
                string sourceBucket = record.S3.Bucket.Name;
                string sourceKey = record.S3.Object.Key;

                using (GetObjectResponse response = await S3Client.GetObjectAsync(sourceBucket, sourceKey))
                using (Stream inputStream = response.ResponseStream)
                using (System.Drawing.Image sourceImage = System.Drawing.Image.FromStream(inputStream))
                {
                    int originalWidth = sourceImage.Width;
                    int originalHeight = sourceImage.Height;

                    // Calculate new dimensions while maintaining the aspect ratio
                    int newWidth, newHeight;
                    if (originalWidth > originalHeight)
                    {
                        newWidth = TargetWidth;
                        newHeight = (int)((float)originalHeight / originalWidth * TargetWidth);
                    }
                    else
                    {
                        newHeight = TargetHeight;
                        newWidth = (int)((float)originalWidth / originalHeight * TargetHeight);
                    }

                    using (Bitmap resizedImage = new Bitmap(newWidth, newHeight))
                    using (Graphics graphics = Graphics.FromImage(resizedImage))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(sourceImage, 0, 0, newWidth, newHeight);

                        using (MemoryStream outputStream = new MemoryStream())
                        {
                            resizedImage.Save(outputStream, ImageFormat.Jpeg);

                            await S3Client.PutObjectAsync(new PutObjectRequest
                            {
                                BucketName = sourceBucket,
                                Key = sourceKey,
                                InputStream = outputStream
                            });
                        }
                    }
                }
            }
            catch (Exception e)
            {
                context.Logger.LogError($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
                context.Logger.LogError(e.Message);
                context.Logger.LogError(e.StackTrace);
                throw;
            }
        }
    }
}