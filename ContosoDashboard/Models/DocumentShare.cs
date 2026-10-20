using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    public int SharedWithUserId { get; set; }

    [Required]
    public int SharedByUserId { get; set; }

    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    // Set when the share was created by a (snapshot) team share
    [MaxLength(100)]
    public string? SharedViaDepartment { get; set; }

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("SharedWithUserId")]
    public virtual User SharedWithUser { get; set; } = null!;

    [ForeignKey("SharedByUserId")]
    public virtual User SharedByUser { get; set; } = null!;
}
