using ContosoDashboard.Models;
using ContosoDashboard.Services;

namespace ContosoDashboard.Tests;

public class DocumentListTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static TestDb Arrange()
    {
        var db = new TestDb();
        db.AddDocument(TestDb.Ni, DocumentCategories.Reports, title: "Alpha", size: 300, uploaded: Now.AddDays(-3));
        db.AddDocument(TestDb.Ni, DocumentCategories.Presentations, TestDb.Project1, title: "Beta", size: 100, uploaded: Now.AddDays(-1));
        db.AddDocument(TestDb.Ni, DocumentCategories.Other, title: "Gamma", size: 200, uploaded: Now.AddDays(-2));
        db.AddDocument(TestDb.Camille, DocumentCategories.Reports, title: "Camille's");
        return db;
    }

    private static string Titles(IEnumerable<DocumentListItem> items) => string.Join(",", items.Select(i => i.Title));

    [Theory]
    [InlineData(DocumentSortField.Title, false, "Alpha,Beta,Gamma")]
    [InlineData(DocumentSortField.Title, true, "Gamma,Beta,Alpha")]
    [InlineData(DocumentSortField.UploadedDate, true, "Beta,Gamma,Alpha")]
    [InlineData(DocumentSortField.UploadedDate, false, "Alpha,Gamma,Beta")]
    [InlineData(DocumentSortField.FileSize, false, "Beta,Gamma,Alpha")]
    [InlineData(DocumentSortField.Category, false, "Gamma,Beta,Alpha")]
    public async Task Lists_only_own_documents_in_requested_order(DocumentSortField sort, bool descending, string expected)
    {
        using var db = Arrange();
        var service = db.CreateDocumentService();

        var items = await service.GetMyDocumentsAsync(db.For(TestDb.Ni), new DocumentListQuery { SortBy = sort, Descending = descending });

        Assert.Equal(expected, Titles(items));
    }

    [Fact]
    public async Task Filters_by_category_project_and_inclusive_date_range()
    {
        using var db = Arrange();
        var service = db.CreateDocumentService();
        var ni = db.For(TestDb.Ni);

        Assert.Equal("Alpha", Titles(await service.GetMyDocumentsAsync(ni, new DocumentListQuery { Category = DocumentCategories.Reports })));
        Assert.Equal("Beta", Titles(await service.GetMyDocumentsAsync(ni, new DocumentListQuery { ProjectId = TestDb.Project1 })));
        Assert.Equal("Gamma", Titles(await service.GetMyDocumentsAsync(ni, new DocumentListQuery { From = Now.AddDays(-2), To = Now.AddDays(-2) })));
    }

    [Fact]
    public async Task Items_carry_owner_permissions_and_project_name()
    {
        using var db = Arrange();
        var service = db.CreateDocumentService();

        var items = await service.GetMyDocumentsAsync(db.For(TestDb.Ni), new DocumentListQuery());

        Assert.All(items, i => Assert.Equal(DocumentPermissions.Owner, i.Permissions));
        Assert.Equal("ContosoDashboard Development", items.Single(i => i.Title == "Beta").ProjectName);
        Assert.Equal("Ni Kang", items[0].UploadedByName);
    }

    [Fact]
    public async Task Other_users_see_none_of_my_documents()
    {
        using var db = Arrange();
        var service = db.CreateDocumentService();

        Assert.Empty(await service.GetMyDocumentsAsync(db.For(TestDb.Avery), new DocumentListQuery()));
    }
}
