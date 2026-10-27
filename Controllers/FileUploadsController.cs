using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Manager,Employee")]
public class FileUploadsController : ControllerBase
{
    private const long MaxFileSize = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".docx"
        };

    private readonly AppDbContext _db;
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<FileUploadsController> _logger;

    public FileUploadsController(
        AppDbContext db,
        Cloudinary cloudinary,
        ILogger<FileUploadsController> logger)
    {
        _db = db;
        _cloudinary = cloudinary;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 30 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] List<IFormFile> files,
        [FromForm] List<string> remarks,
        CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new { message = "Select at least one file." });

        if (files.Count > 5)
            return BadRequest(new { message = "Upload a maximum of 5 files at a time." });

        if (remarks == null || remarks.Count != files.Count)
            return BadRequest(new { message = "Provide one remark for each file." });

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var extension = Path.GetExtension(file.FileName);

            if (file.Length == 0)
                return BadRequest(new { message = $"File '{file.FileName}' is empty." });

            if (file.Length > MaxFileSize)
                return BadRequest(new { message = $"'{file.FileName}' exceeds the 5 MB limit." });

            if (!AllowedExtensions.Contains(extension))
                return BadRequest(new { message = $"'{file.FileName}' has an unsupported format." });

            if (string.IsNullOrWhiteSpace(remarks[i]))
                return BadRequest(new { message = $"Remark is required for '{file.FileName}'." });

            if (remarks[i].Trim().Length > 1000)
                return BadRequest(new { message = "Each remark must be 1000 characters or fewer." });
        }

        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!int.TryParse(userIdValue, out var userId))
            return Unauthorized(new { message = "User ID is missing from the login token." });

        var uploadedRecords = new List<FileUpload>();
        var uploadedResources = new List<(string PublicId, ResourceType ResourceType)>();

        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var fileName = Path.GetFileName(file.FileName);
                var isImage = extension is ".jpg" or ".jpeg" or ".png";

                await using var stream = file.OpenReadStream();

                string publicId;
                string? secureUrl;

                if (isImage)
                {
                    var imageResult = await _cloudinary.UploadAsync(
                        new ImageUploadParams
                        {
                            File = new FileDescription(fileName, stream),
                            Folder = "employee-app-uploads",
                            PublicId = Guid.NewGuid().ToString("N"),
                            Overwrite = false
                        });

                    if (imageResult.Error != null)
                        throw new InvalidOperationException(imageResult.Error.Message);

                    publicId = imageResult.PublicId;
                    secureUrl = imageResult.SecureUrl?.ToString();
                    uploadedResources.Add((publicId, ResourceType.Image));
                }
                else
                {
                    var rawResult = await _cloudinary.UploadAsync(
                        new RawUploadParams
                        {
                            File = new FileDescription(fileName, stream),
                            Folder = "employee-app-uploads",
                            // Raw public IDs must retain the original extension.
                            PublicId = Guid.NewGuid().ToString("N") + extension,
                            Overwrite = false
                        });

                    if (rawResult.Error != null)
                        throw new InvalidOperationException(rawResult.Error.Message);

                    publicId = rawResult.PublicId;
                    secureUrl = rawResult.SecureUrl?.ToString();
                    uploadedResources.Add((publicId, ResourceType.Raw));
                }

                if (string.IsNullOrWhiteSpace(secureUrl))
                    throw new InvalidOperationException("Cloudinary did not return a secure URL.");

                uploadedRecords.Add(new FileUpload
                {
                    FileName = fileName,
                    Remark = remarks[i].Trim(),
                    FileFormat = extension,
                    FileSize = file.Length,
                    CloudinaryPublicId = publicId,
                    FileUrl = secureUrl,
                    UploadedByUserId = userId,
                    UploadedAt = DateTime.UtcNow
                });
            }

            _db.FileUploads.AddRange(uploadedRecords);
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                message = "Files uploaded successfully.",
                files = uploadedRecords.Select(f => new
                {
                    f.Id,
                    f.FileName,
                    f.Remark,
                    f.FileFormat,
                    f.FileSize,
                    f.FileUrl,
                    f.UploadedAt
                })
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "File upload failed for user {UserId}.", userId);

            foreach (var resource in uploadedResources)
            {
                try
                {
                    var deletionResult = await _cloudinary.DestroyAsync(
                        new DeletionParams(resource.PublicId)
                        {
                            ResourceType = resource.ResourceType
                        });

                    if (deletionResult.Error != null)
                    {
                        _logger.LogError(
                            "Cloudinary cleanup failed for resource {PublicId}: {Error}",
                            resource.PublicId,
                            deletionResult.Error.Message);
                    }
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(
                        cleanupException,
                        "Cloudinary cleanup failed for resource {PublicId}.",
                        resource.PublicId);
                }
            }

            return StatusCode(500, new
            {
                message = "Upload failed. Check server logs and Cloudinary configuration."
            });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUpload(int id, CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(userIdValue, out var userId))
            return Unauthorized(new { message = "Please sign in again." });

        var upload = await _db.FileUploads.FindAsync(new object[] { id }, cancellationToken);
        if (upload == null)
            return NoContent();
        if (!User.IsManager() && upload.UploadedByUserId != userId)
            return StatusCode(403, new { message = "You can only delete files you uploaded." });

        if (await _db.Employees.AnyAsync(e => e.ResumeUrl == upload.FileUrl, cancellationToken))
            return Conflict(new { message = "This file is attached to an employee. Remove it from the employee profile first." });

        try
        {
            var isImage = upload.FileFormat.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";
            var result = await _cloudinary.DestroyAsync(new DeletionParams(upload.CloudinaryPublicId)
            {
                ResourceType = isImage ? ResourceType.Image : ResourceType.Raw,
                Invalidate = true
            });
            // A retry after a database failure may find the cloud asset already gone.
            if (result.Error != null || (result.Result != "ok" && result.Result != "not found"))
            {
                _logger.LogWarning("Cloudinary deletion failed for upload {UploadId}.", id);
                return StatusCode(502, new { message = "The file host could not delete the file. Please try again." });
            }

            _db.FileUploads.Remove(upload);
            await _db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Deletion failed for upload {UploadId}.", id);
            return StatusCode(500, new { message = "Could not complete file deletion. Please retry." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUploads(CancellationToken cancellationToken)
    {
        var uploads = await _db.FileUploads
            .Where(f => User.IsManager() || f.UploadedByUserId == User.UserId())
            .Where(f => User.IsManager() || f.UploadedByUserId == User.UserId())
            .AsNoTracking()
            .OrderByDescending(f => f.UploadedAt)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.Remark,
                f.FileFormat,
                f.FileSize,
                f.FileUrl,
                f.UploadedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(uploads);
    }
}
