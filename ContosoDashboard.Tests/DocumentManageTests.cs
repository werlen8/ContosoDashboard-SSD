using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using static ContosoDashboard.Tests.DocumentUploadTests;

namespace ContosoDashboard.Tests;

public class DocumentManageTests
{
    private static async Task<int> Upload(DocumentService service, UserContext user, string category = DocumentCategories.Reports,
        int? projectId = null, Stream? content = null, string fileName = "spec.pdf")
    {
        var result = await service.UploadAsync(Request(content ?? Pdf(), fileName, category: category, projectId: projectId), user);
        Assert.True(result.Succeeded, result.Error);
        return result.Value;
    }

    private static MemoryStream Docx() => new(new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4 });

    private static DocumentMetadataUpdate Update(Document doc, string? title = null, string? category = null, int? projectId = -1) =>
        new(title ?? doc.Title, doc.Description, category ?? doc.Category,
            doc.Tags?.Split(',') ?? Array.Empty<string>(), projectId == -1 ? doc.ProjectId : projectId);

    private static async Task<Document> Reload(TestDb db, int id) =>
        await db.Context.Documents.AsNoTracking().SingleAsync(d => d.DocumentId == id);

    // ----- Open (download / preview) -----

    [Fact]
    public async Task Owner_downloads_original_content_and_name_and_it_is_logged()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni), content: Pdf(321), fileName: "Quarterly Report.pdf");

        var file = await service.OpenFileAsync(id, db.For(TestDb.Ni), FileAccessMode.Download);

        Assert.NotNull(file);
        Assert.Equal("Quarterly Report.pdf", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.False(file.Inline);
        Assert.Equal(321, file.Content.Length);
        Assert.Equal(1, await db.Context.DocumentActivities.CountAsync(a => a.DocumentId == id && a.Action == DocumentActions.Download));
    }

    [Fact]
    public async Task Project_member_can_preview_pdf_inline()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni), DocumentCategories.ProjectDocuments, TestDb.Project1);

        var file = await service.OpenFileAsync(id, db.For(TestDb.Floris), FileAccessMode.Preview);

        Assert.NotNull(file);
        Assert.True(file.Inline);
        Assert.Equal(1, await db.Context.DocumentActivities.CountAsync(a => a.DocumentId == id && a.Action == DocumentActions.Preview && a.UserId == TestDb.Floris));
    }

    [Fact]
    public async Task Unauthorized_user_cannot_open_and_nothing_is_logged()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni));

        Assert.Null(await service.OpenFileAsync(id, db.For(TestDb.Avery), FileAccessMode.Download));
        Assert.Equal(0, await db.Context.DocumentActivities.CountAsync(a => a.UserId == TestDb.Avery));
    }

    [Fact]
    public async Task Office_documents_cannot_be_previewed()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni), content: Docx(), fileName: "plan.docx");

        Assert.Null(await service.OpenFileAsync(id, db.For(TestDb.Ni), FileAccessMode.Preview));
        Assert.NotNull(await service.OpenFileAsync(id, db.For(TestDb.Ni), FileAccessMode.Download));
    }

    [Fact]
    public async Task Missing_stored_file_returns_null()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);
        var id = await Upload(service, db.For(TestDb.Ni));
        storage.Files.Clear();

        Assert.Null(await service.OpenFileAsync(id, db.For(TestDb.Ni), FileAccessMode.Download));
    }

    // ----- Edit metadata -----

    [Fact]
    public async Task Owner_edits_metadata_and_it_is_logged()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni));
        var before = await Reload(db, id);

        var result = await service.UpdateMetadataAsync(id,
            new DocumentMetadataUpdate("New title", "Desc", DocumentCategories.Presentations, new[] { "Budget" }, null), db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
        var after = await Reload(db, id);
        Assert.Equal("New title", after.Title);
        Assert.Equal("Desc", after.Description);
        Assert.Equal(DocumentCategories.Presentations, after.Category);
        Assert.Equal("budget", after.Tags);
        Assert.True(after.UpdatedDate >= before.UpdatedDate);
        Assert.Equal(1, await db.Context.DocumentActivities.CountAsync(a => a.DocumentId == id && a.Action == DocumentActions.EditMetadata));
    }

    [Theory]
    [InlineData(TestDb.Camille, null)]                       // Project Manager of the document's project
    [InlineData(TestDb.Admin, null)]                         // Administrator
    [InlineData(TestDb.Ni, DocumentMessages.NotPermitted)]   // project member, not owner
    [InlineData(TestDb.Avery, DocumentMessages.NotFound)]    // no access at all
    public async Task Metadata_edit_rights_follow_access_matrix(int userId, string? expectedError)
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        // Uploaded by the Team Lead so the Employee member has no team-lead rights over it.
        var id = await Upload(service, db.For(TestDb.Floris), DocumentCategories.ProjectDocuments, TestDb.Project1);

        var result = await service.UpdateMetadataAsync(id, Update(await Reload(db, id), title: "Edited"), db.For(userId));

        Assert.Equal(expectedError, result.Error);
        Assert.Equal(expectedError == null, result.Succeeded);
    }

    [Fact]
    public async Task Team_lead_can_edit_team_member_document()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni));

        var result = await service.UpdateMetadataAsync(id, Update(await Reload(db, id), title: "Edited"), db.For(TestDb.Floris));

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task Share_recipient_cannot_edit()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni));
        db.AddShare(id, TestDb.Avery, TestDb.Ni);

        var result = await service.UpdateMetadataAsync(id, Update(await Reload(db, id), title: "Edited"), db.For(TestDb.Avery));

        Assert.Equal(DocumentMessages.NotPermitted, result.Error);
    }

    [Fact]
    public async Task Only_owner_can_change_project()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni), DocumentCategories.ProjectDocuments, TestDb.Project1);

        var result = await service.UpdateMetadataAsync(id, Update(await Reload(db, id), projectId: null), db.For(TestDb.Camille));

        Assert.Equal(DocumentMessages.NotPermitted, result.Error);
        Assert.Equal(TestDb.Project1, (await Reload(db, id)).ProjectId);
    }

    [Fact]
    public async Task Owner_cannot_link_document_to_project_they_do_not_belong_to()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Avery));

        var result = await service.UpdateMetadataAsync(id, Update(await Reload(db, id), projectId: TestDb.Project1), db.For(TestDb.Avery));

        Assert.Equal(DocumentMessages.NotFound, result.Error);
    }

    [Fact]
    public async Task Project_document_cannot_become_personal_while_linked()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni), DocumentCategories.ProjectDocuments, TestDb.Project1);

        var result = await service.UpdateMetadataAsync(id,
            Update(await Reload(db, id), category: DocumentCategories.PersonalFiles), db.For(TestDb.Ni));

        Assert.Equal(DocumentMessages.PersonalFilesNotLinkable, result.Error);
    }

    // ----- Replace file -----

    [Fact]
    public async Task Owner_replaces_file_with_new_path_and_old_file_is_removed()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);
        var id = await Upload(service, db.For(TestDb.Ni), content: Pdf(100));
        var oldPath = (await Reload(db, id)).FilePath;

        var result = await service.ReplaceFileAsync(id, Docx(), "v2.docx", db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
        var doc = await Reload(db, id);
        Assert.NotEqual(oldPath, doc.FilePath);
        Assert.EndsWith(".docx", doc.FilePath);
        Assert.Equal("v2.docx", doc.OriginalFileName);
        Assert.Equal(8, doc.FileSizeBytes);
        Assert.StartsWith("application/vnd.openxmlformats", doc.FileType);
        Assert.False(storage.Files.ContainsKey(oldPath));
        Assert.True(storage.Files.ContainsKey(doc.FilePath));
        Assert.Equal(1, await db.Context.DocumentActivities.CountAsync(a => a.DocumentId == id && a.Action == DocumentActions.ReplaceFile));
    }

    [Theory]
    [InlineData(TestDb.Camille, DocumentMessages.NotPermitted)]
    [InlineData(TestDb.Admin, DocumentMessages.NotPermitted)]
    [InlineData(TestDb.Avery, DocumentMessages.NotFound)]
    public async Task Only_owner_can_replace_file(int userId, string expectedError)
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni), DocumentCategories.ProjectDocuments, TestDb.Project1);

        var result = await service.ReplaceFileAsync(id, Pdf(), "v2.pdf", db.For(userId));

        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public async Task Replacement_is_validated_and_scanned_like_an_upload()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);
        var id = await Upload(service, db.For(TestDb.Ni));
        var before = await Reload(db, id);
        var eicar = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(Services.Scanning.SignatureMalwareScanner.EicarSignature));

        var result = await service.ReplaceFileAsync(id, eicar, "eicar.txt", db.For(TestDb.Ni));

        Assert.Equal(DocumentMessages.ScanRejected, result.Error);
        Assert.Equal(before.FilePath, (await Reload(db, id)).FilePath);
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task Failed_save_during_replace_keeps_old_file_and_removes_new_one()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);
        var id = await Upload(service, db.For(TestDb.Ni));
        var oldPath = (await Reload(db, id)).FilePath;

        db.FailNextSave = true;
        var result = await service.ReplaceFileAsync(id, Pdf(500), "v2.pdf", db.For(TestDb.Ni));

        Assert.False(result.Succeeded);
        Assert.Equal(oldPath, (await Reload(db, id)).FilePath);
        Assert.Equal(new[] { oldPath }, storage.Files.Keys);
    }

    // ----- Delete -----

    [Fact]
    public async Task Owner_deletes_document_file_shares_and_task_links_but_history_remains()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);
        var id = await Upload(service, db.For(TestDb.Ni), DocumentCategories.ProjectDocuments, TestDb.Project1);
        db.AddShare(id, TestDb.Avery, TestDb.Ni);
        db.Context.TaskDocuments.Add(new TaskDocument { TaskId = 1, DocumentId = id, AttachedByUserId = TestDb.Ni });
        await db.Context.SaveChangesAsync();

        var result = await service.DeleteAsync(id, db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
        Assert.False(await db.Context.Documents.AnyAsync(d => d.DocumentId == id));
        Assert.False(await db.Context.DocumentShares.AnyAsync());
        Assert.False(await db.Context.TaskDocuments.AnyAsync());
        Assert.Empty(storage.Files);
        Assert.Equal(1, await db.Context.DocumentActivities.CountAsync(a => a.DocumentId == id && a.Action == DocumentActions.Delete));
    }

    [Theory]
    [InlineData(TestDb.Camille, null)]
    [InlineData(TestDb.Floris, null)]
    [InlineData(TestDb.Admin, null)]
    [InlineData(TestDb.Avery, DocumentMessages.NotFound)]
    public async Task Delete_rights_follow_access_matrix(int userId, string? expectedError)
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Ni), DocumentCategories.ProjectDocuments, TestDb.Project1);

        var result = await service.DeleteAsync(id, db.For(userId));

        Assert.Equal(expectedError, result.Error);
        Assert.Equal(expectedError != null, await db.Context.Documents.AnyAsync(d => d.DocumentId == id));
    }

    [Fact]
    public async Task Project_member_cannot_delete_another_members_document()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();
        var id = await Upload(service, db.For(TestDb.Floris), DocumentCategories.ProjectDocuments, TestDb.Project1);

        var result = await service.DeleteAsync(id, db.For(TestDb.Ni));

        Assert.Equal(DocumentMessages.NotPermitted, result.Error);
    }
}
