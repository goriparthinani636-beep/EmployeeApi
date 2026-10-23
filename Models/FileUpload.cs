using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmployeeApi.Models;

[Table("FileUploads")]
public class FileUpload
{
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Remark { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string FileFormat { get; set; } = string.Empty;

    public long FileSize { get; set; }

    [Required]
    [MaxLength(2048)]
    public string CloudinaryPublicId { get; set; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    public string FileUrl { get; set; } = string.Empty;

    public int UploadedByUserId { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
