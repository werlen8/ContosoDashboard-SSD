using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

// Insert-only audit record. DocumentId deliberately has no foreign key so history survives deletion.
public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(200)]
    public string DocumentTitle { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Details { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}

public static class DocumentActions
{
    public const string Upload = "Upload";
    public const string Download = "Download";
    public const string Preview = "Preview";
    public const string EditMetadata = "EditMetadata";
    public const string ReplaceFile = "ReplaceFile";
    public const string Delete = "Delete";
    public const string Share = "Share";
    public const string RevokeShare = "RevokeShare";
    public const string AttachToTask = "AttachToTask";
}
