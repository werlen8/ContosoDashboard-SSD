using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class TaskDocument
{
    [Key]
    public int TaskDocumentId { get; set; }

    [Required]
    public int TaskId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    public int AttachedByUserId { get; set; }

    public DateTime AttachedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("TaskId")]
    public virtual TaskItem Task { get; set; } = null!;

    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("AttachedByUserId")]
    public virtual User AttachedByUser { get; set; } = null!;
}
