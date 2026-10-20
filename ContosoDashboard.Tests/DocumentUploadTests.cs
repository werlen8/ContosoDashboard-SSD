using System.Text;
using System.Text.RegularExpressions;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Services.Scanning;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests;

public class DocumentUploadTests
{
    internal static MemoryStream Pdf(long size = 1000)
    {
        var bytes = new byte[size];
        "%PDF-1.7"u8.ToArray().AsSpan(0, (int)Math.Min(8, size)).CopyTo(bytes);
        return new MemoryStream(bytes);
    }

    internal static UploadRequest Request(Stream content, string fileName = "spec.pdf", string title = "Spec",
        string category = DocumentCategories.Reports, int? projectId = null, params string[] tags) => new()
    {
        Content = content,
        OriginalFileName = fileName,
        Title = title,
        Category = category,
        ProjectId = projectId,
        Tags = tags
    };

    private sealed class ThrowingScanner : IMalwareScanner
    {
        public Task<ScanResult> ScanAsync(Stream content, CancellationToken ct = default) => throw new InvalidOperationException("scanner down");
    }

    private static async Task AssertRejected(TestDb db, FakeFileStorageService storage, OperationResult result, string expectedError)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.Error);
        Assert.Empty(storage.Files);
        Assert.Equal(0, await db.Context.Documents.CountAsync());
    }

    [Fact]
    public async Task Valid_upload_stores_file_then_record_with_canonical_metadata()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(Request(Pdf(1234), "../../evil name.pdf", "  Spec  "), db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
        var doc = await db.Context.Documents.SingleAsync();
        Assert.Equal(result.Value, doc.DocumentId);
        Assert.Equal("Spec", doc.Title);
        Assert.Equal("application/pdf", doc.FileType);
        Assert.Equal(1234, doc.FileSizeBytes);
        Assert.Equal("evil name.pdf", doc.OriginalFileName);
        Assert.Equal(TestDb.Ni, doc.UploadedByUserId);
        Assert.Matches(new Regex(@"^4/personal/[0-9a-f]{32}\.pdf$"), doc.FilePath);
        Assert.Equal(1234, storage.Files[doc.FilePath].Length);
        Assert.Equal(1, await db.Context.DocumentActivities.CountAsync(a => a.DocumentId == doc.DocumentId && a.Action == DocumentActions.Upload));
    }

    [Fact]
    public async Task File_of_exactly_25_MB_is_accepted()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();

        var result = await service.UploadAsync(Request(Pdf(DocumentFileRules.MaxFileSizeBytes)), db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
    }

    [Theory]
    [InlineData("big.pdf", 26_214_401, DocumentMessages.TooLarge)]
    [InlineData("empty.pdf", 0, DocumentMessages.Empty)]
    [InlineData("tool.exe", 1000, DocumentMessages.UnsupportedType)]
    [InlineData("noextension", 1000, DocumentMessages.UnsupportedType)]
    public async Task Invalid_files_are_rejected_without_side_effects(string fileName, long size, string expected)
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(Request(Pdf(size), fileName), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, expected);
    }

    [Fact]
    public async Task Content_not_matching_extension_is_rejected()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(Request(new MemoryStream("MZ not a pdf"u8.ToArray()), "fake.pdf"), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, DocumentMessages.ContentMismatch);
    }

    [Fact]
    public async Task Eicar_test_file_is_rejected_by_scan()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);
        var eicar = new MemoryStream(Encoding.ASCII.GetBytes(SignatureMalwareScanner.EicarSignature));

        var result = await service.UploadAsync(Request(eicar, "eicar.txt"), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, DocumentMessages.ScanRejected);
    }

    [Fact]
    public async Task Scanner_failure_fails_closed()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage, new ThrowingScanner());

        var result = await service.UploadAsync(Request(Pdf()), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, DocumentMessages.ScanRejected);
    }

    [Fact]
    public async Task Non_member_cannot_upload_to_project()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(Request(Pdf(), projectId: TestDb.Project1), db.For(TestDb.Avery));

        await AssertRejected(db, storage, result, DocumentMessages.NotFound);
    }

    [Fact]
    public async Task Project_member_can_upload_to_project()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(Request(Pdf(), projectId: TestDb.Project1), db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
        var doc = await db.Context.Documents.SingleAsync();
        Assert.Equal(TestDb.Project1, doc.ProjectId);
        Assert.StartsWith("4/1/", doc.FilePath);
    }

    [Fact]
    public async Task Personal_files_cannot_be_linked_to_a_project()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(
            Request(Pdf(), category: DocumentCategories.PersonalFiles, projectId: TestDb.Project1), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, DocumentMessages.PersonalFilesNotLinkable);
    }

    [Theory]
    [InlineData("   ", "Reports", DocumentMessages.TitleRequired)]
    [InlineData("x", "Not a category", DocumentMessages.InvalidCategory)]
    public async Task Invalid_metadata_is_rejected(string title, string category, string expected)
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(Request(Pdf(), title: title, category: category), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, expected);
    }

    [Fact]
    public async Task Title_longer_than_200_characters_is_rejected()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(Request(Pdf(), title: new string('a', 201)), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, DocumentMessages.TitleTooLong);
    }

    [Fact]
    public async Task More_than_ten_tags_are_rejected()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);
        var tags = Enumerable.Range(1, 11).Select(i => $"t{i}").ToArray();

        var result = await service.UploadAsync(Request(Pdf(), tags: tags), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, DocumentMessages.TooManyTags);
    }

    [Fact]
    public async Task Tags_are_normalized()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();

        var result = await service.UploadAsync(Request(Pdf(), tags: new[] { " Budget ", "budget", "", "Q3" }), db.For(TestDb.Ni));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("budget,q3", (await db.Context.Documents.SingleAsync()).Tags);
    }

    [Fact]
    public async Task Stored_file_is_removed_when_saving_the_record_fails()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService();
        var service = db.CreateDocumentService(storage);
        // A user id that does not exist makes the insert fail on the foreign key after the file was written.
        var ghost = new UserContext(999, UserRole.Employee, null);

        var result = await service.UploadAsync(Request(Pdf()), ghost);

        Assert.False(result.Succeeded);
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task Storage_failure_creates_no_record()
    {
        using var db = new TestDb();
        var storage = new FakeFileStorageService { ThrowOnUpload = true };
        var service = db.CreateDocumentService(storage);

        var result = await service.UploadAsync(Request(Pdf()), db.For(TestDb.Ni));

        await AssertRejected(db, storage, result, DocumentMessages.StorageFailed);
    }
}
