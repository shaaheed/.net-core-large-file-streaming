using LargeFileUpload.Helpers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System;
using System.IO;
using System.Threading.Tasks;

namespace LargeFileUpload.Controllers
{
    [Route("api/files")]
    public class FileStreamingController : Controller
    {

        private static readonly FormOptions _defaultFormOptions = new FormOptions();
        private readonly ILogger _logger;

        public FileStreamingController(ILogger<FileStreamingController> logger)
        {
            _logger = logger;
        }

        [HttpPost("upload")]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> Upload()
        {

            if (!MultipartUtilities.IsMultipartContentType(Request.ContentType))
            {
                return BadRequest($"Expected a multipart request, but got {Request.ContentType}");
            }

            var formAccumulator = new KeyValueAccumulator();
            var targetFilePath = string.Empty;

            var boundary = MultipartUtilities.GetBoundary(MediaTypeHeaderValue.Parse(Request.ContentType), _defaultFormOptions.MultipartBoundaryLengthLimit);

            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync();
            while (section != null)
            {
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue contentDisposition))
                {
                    if (MultipartUtilities.HasFileContentDisposition(contentDisposition))
                    {
                        targetFilePath = Path.Combine(Directory.GetCurrentDirectory(), HeaderUtilities.RemoveQuotes(contentDisposition.FileName).Value);
                        using (var targetStream = System.IO.File.Create(targetFilePath))
                        {
                            await section.Body.CopyToAsync(targetStream);
                            _logger.LogInformation($"Copied the uploaded file '{targetFilePath}'");
                        }
                    }
                    else if (MultipartUtilities.HasFormDataContentDisposition(contentDisposition))
                    {
                        // Content-Disposition: form-data; name="key"
                        // value

                        // Do not limit the key name length here because the 
                        // multipart headers length limit is already in effect.

                        var key = HeaderUtilities.RemoveQuotes(contentDisposition.Name).Value;
                        var encoding = MultipartUtilities.GetEncoding(section);

                        using (var streamReader = new StreamReader(
                            section.Body,
                            encoding,
                            detectEncodingFromByteOrderMarks: true,
                            bufferSize: 1024,
                            leaveOpen: true))
                        {
                            // The value length limit is enforced by MultipartBodyLengthLimit
                            var value = await streamReader.ReadToEndAsync();
                            if (string.Equals(value, "undefined", StringComparison.OrdinalIgnoreCase))
                            {
                                value = string.Empty;
                            }
                            formAccumulator.Append(key, value);
                            if (formAccumulator.ValueCount > _defaultFormOptions.ValueCountLimit)
                            {
                                throw new InvalidDataException($"Form key count limit {_defaultFormOptions.ValueCountLimit} exceeded.");
                            }
                        }
                    }
                }

                // Drains any remaining section body that has not been consumed and
                // reads the headers for the next section.
                section = await reader.ReadNextSectionAsync();

            }

            return Json(new
            {
                FilePath = targetFilePath
            });
        }

        [HttpGet("upload")]
        [ActionName("Upload")]
        public IActionResult UploadView()
        {
            return View();
        }

    }
}
