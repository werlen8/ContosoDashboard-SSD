using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using static ContosoDashboard.Tests.DocumentUploadTests;

namespace ContosoDashboard.Tests;

public class ProjectDocumentTests
{
    private static Task<int> NotificationCount(TestDb db, int userId) =>
        db.Context.Notifications.CountAsync(n => n.UserId == userId && n.Type == NotificationType.DocumentAdded);

    [Theory]
    [InlineData(TestDb.Camille, 2)]  // Project Manager
    [InlineData(TestDb.Floris, 2)]   // member
    [InlineData(TestDb.Ni, 2)]       // member
    [InlineData(TestDb.Admin, 2)]    // Administrator
    [InlineData(TestDb.Avery, 0)]    // not a member
    public async Task Project_documents_are_listed_for_project_members_only(int userId, int expectedCount)
    {
        using var db = new TestDb();
        db.AddDocument(TestDb.Camille, DocumentCategories.ProjectDocuments, TestDb.Project1, title: "Plan");
        db.AddDocument(TestDb.Ni, DocumentCategories.Reports, TestDb.Project1, title: "Status");
        db.AddDocument(TestDb.Ni, DocumentCategories.Reports, title: "Unrelated");
        var service = db.CreateDocumentService();

        var items = await service.GetProjectDocumentsAsync(TestDb.Project1, db.For(userId));

        Assert.Equal(expectedCount, items.Count);
        Assert.All(items, i => Assert.Equal(TestDb.Project1, i.ProjectId));
    }

    [Fact]
    public async Task Project_list_items_carry_each_users_permissions()
    {
        using var db = new TestDb();
        var docId = db.AddDocument(TestDb.Floris, DocumentCategories.ProjectDocuments, TestDb.Project1).DocumentId;
        var service = db.CreateDocumentService();

        var forManager = await service.GetProjectDocumentsAsync(TestDb.Project1, db.For(TestDb.Camille));
        var forMember = await service.GetProjectDocumentsAsync(TestDb.Project1, db.For(TestDb.Ni));

        Assert.True(forManager.Single(i => i.DocumentId == docId).Permissions.CanDelete);
        Assert.False(forMember.Single(i => i.DocumentId == docId).Permissions.CanDelete);
        Assert.True(forMember.Single(i => i.DocumentId == docId).Permissions.CanView);
    }

    [Fact]
    public async Task Uploading_to_a_project_notifies_manager_and_members_except_uploader()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();

        var result = await service.UploadAsync(Request(Pdf(), title: "Design", category: DocumentCategories.ProjectDocuments, projectId: TestDb.Project1), db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, await NotificationCount(db, TestDb.Camille));
        Assert.Equal(1, await NotificationCount(db, TestDb.Floris));
        Assert.Equal(0, await NotificationCount(db, TestDb.Ni));
        Assert.Equal(0, await NotificationCount(db, TestDb.Avery));
        var notification = await db.Context.Notifications.FirstAsync(n => n.UserId == TestDb.Camille);
        Assert.Contains("Design", notification.Message);
        Assert.Contains("ContosoDashboard Development", notification.Message);
    }

    [Fact]
    public async Task Upload_without_project_sends_no_notifications()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();

        await service.UploadAsync(Request(Pdf()), db.For(TestDb.Ni));

        Assert.False(await db.Context.Notifications.AnyAsync(n => n.Type == NotificationType.DocumentAdded));
    }

    [Fact]
    public async Task Linking_an_existing_document_to_a_project_notifies_members()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = (await service.UploadAsync(Request(Pdf()), db.For(TestDb.Ni))).Value;

        var result = await service.UpdateMetadataAsync(id,
            new DocumentMetadataUpdate("Spec", null, DocumentCategories.Reports, Array.Empty<string>(), TestDb.Project1), db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, await NotificationCount(db, TestDb.Camille));
        Assert.Equal(1, await NotificationCount(db, TestDb.Floris));
        Assert.Equal(0, await NotificationCount(db, TestDb.Ni));
    }

    [Fact]
    public async Task Project_manager_upload_notifies_members_only()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();

        await service.UploadAsync(Request(Pdf(), category: DocumentCategories.ProjectDocuments, projectId: TestDb.Project1), db.For(TestDb.Camille));

        Assert.Equal(0, await NotificationCount(db, TestDb.Camille));
        Assert.Equal(1, await NotificationCount(db, TestDb.Floris));
        Assert.Equal(1, await NotificationCount(db, TestDb.Ni));
    }
}
