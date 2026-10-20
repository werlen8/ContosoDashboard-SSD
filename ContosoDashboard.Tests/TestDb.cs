using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Services.Scanning;
using ContosoDashboard.Services.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests;

// SQLite in-memory database with the application's seed data:
// 1 Administrator (IT), 2 Camille PM (Engineering, manages project 1), 3 Floris TeamLead (Engineering, member of project 1),
// 4 Ni Employee (Engineering, member of project 1), 5 Avery Employee (Marketing, no projects).
public sealed class TestDb : IDisposable
{
    public const int Admin = 1, Camille = 2, Floris = 3, Ni = 4, Avery = 5;
    public const int Project1 = 1;

    private readonly SqliteConnection _connection;
    private readonly FailingSaveInterceptor _interceptor = new();

    public ApplicationDbContext Context { get; }

    // When true, the next SaveChanges call throws (simulates a database failure).
    public bool FailNextSave { get => _interceptor.FailNext; set => _interceptor.FailNext = value; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);
        Context.Database.EnsureCreated();
    }

    public UserContext For(int userId)
    {
        var u = Context.Users.AsNoTracking().Single(x => x.UserId == userId);
        return new UserContext(u.UserId, u.Role, u.Department);
    }

    public User AddUser(int userId, UserRole role, string? department)
    {
        var user = new User
        {
            UserId = userId,
            Email = $"user{userId}@contoso.com",
            DisplayName = $"User {userId}",
            Department = department,
            Role = role
        };
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    public Project AddProject(int managerId, params int[] memberIds)
    {
        var project = new Project { Name = $"Project of {managerId}", ProjectManagerId = managerId, Status = ProjectStatus.Active };
        Context.Projects.Add(project);
        Context.SaveChanges();
        AddMembers(project.ProjectId, memberIds);
        return project;
    }

    public void AddMembers(int projectId, params int[] memberIds)
    {
        foreach (var id in memberIds)
            Context.ProjectMembers.Add(new ProjectMember { ProjectId = projectId, UserId = id, Role = "Developer" });
        Context.SaveChanges();
    }

    public Document AddDocument(int uploaderId, string category = DocumentCategories.Reports, int? projectId = null,
        string title = "Doc", string fileType = "application/pdf", DateTime? uploaded = null, long size = 100, string? tags = null)
    {
        var doc = new Document
        {
            Title = title,
            Category = category,
            ProjectId = projectId,
            UploadedByUserId = uploaderId,
            OriginalFileName = title + ".pdf",
            FileType = fileType,
            FileSizeBytes = size,
            FilePath = $"{uploaderId}/{projectId?.ToString() ?? "personal"}/{Guid.NewGuid():N}.pdf",
            Tags = tags,
            UploadedDate = uploaded ?? DateTime.UtcNow,
            UpdatedDate = uploaded ?? DateTime.UtcNow
        };
        Context.Documents.Add(doc);
        Context.SaveChanges();
        return doc;
    }

    public void AddShare(int documentId, int withUserId, int byUserId)
    {
        Context.DocumentShares.Add(new DocumentShare { DocumentId = documentId, SharedWithUserId = withUserId, SharedByUserId = byUserId });
        Context.SaveChanges();
    }

    public DocumentService CreateDocumentService(IFileStorageService? storage = null, IMalwareScanner? scanner = null) =>
        new(Context,
            storage ?? new FakeFileStorageService(),
            scanner ?? new SignatureMalwareScanner(new ConfigurationBuilder().Build()),
            new NotificationService(Context),
            NullLogger<DocumentService>.Instance);

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

internal sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public bool FailNext { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new DbUpdateException("Simulated database failure.");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
