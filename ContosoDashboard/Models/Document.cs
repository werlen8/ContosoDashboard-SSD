using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class Document
{
    [Key]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    // One of DocumentCategories.All, stored as text
    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = DocumentCategories.Other;

    // Normalized comma-joined list: <=10 tags, <=50 chars each, lower-case, unique
    [MaxLength(600)]
    public string? Tags { get; set; }

    // Display/download name only; never used to build storage paths
    [Required]
    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    // Canonical MIME type from DocumentFileRules (never the browser-reported one)
    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    // Relative, portable path: {uploaderId}/{projectId|personal}/{guid}.{ext}
    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    public int UploadedByUserId { get; set; }

    // Must be null when Category is "Personal Files"
    public int? ProjectId { get; set; }

    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("UploadedByUserId")]
    public virtual User UploadedByUser { get; set; } = null!;

    [ForeignKey("ProjectId")]
    public virtual Project? Project { get; set; }

    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<TaskDocument> TaskDocuments { get; set; } = new List<TaskDocument>();
}
