using ContosoDashboard.Models;
using ContosoDashboard.Services;

namespace ContosoDashboard.Tests;

// FR-016 access matrix. Expected rights are encoded as letters:
// V = view, E = edit metadata, R = replace file, D = delete, S = share; "" = no access.
public class DocumentAuthorizationTests
{
    private const int SalesMember = 6;      // Employee, Sales, member of project 1
    private const int NoDeptLead = 7;       // TeamLead with no department

    private static (TestDb Db, Dictionary<string, int> Docs) Arrange()
    {
        var db = new TestDb();
        db.AddUser(SalesMember, UserRole.Employee, "Sales");
        db.AddUser(NoDeptLead, UserRole.TeamLead, null);
        db.AddMembers(TestDb.Project1, SalesMember);

        var docs = new Dictionary<string, int>
        {
            ["project"] = db.AddDocument(TestDb.Ni, DocumentCategories.ProjectDocuments, TestDb.Project1).DocumentId,
            ["reports"] = db.AddDocument(TestDb.Ni, DocumentCategories.Reports).DocumentId,
            ["personal"] = db.AddDocument(TestDb.Ni, DocumentCategories.PersonalFiles).DocumentId,
            ["shared"] = db.AddDocument(TestDb.Ni, DocumentCategories.Other).DocumentId,
        };
        db.AddShare(docs["shared"], TestDb.Avery, TestDb.Ni);
        return (db, docs);
    }

    [Theory]
    // Owner
    [InlineData("project", TestDb.Ni, "VERDS")]
    [InlineData("personal", TestDb.Ni, "VERDS")]
    // Project member (not a team lead of the uploader)
    [InlineData("project", SalesMember, "V")]
    // Project Manager of the document's project
    [InlineData("project", TestDb.Camille, "VED")]
    // Team Lead in the uploader's department
    [InlineData("project", TestDb.Floris, "VED")]
    [InlineData("reports", TestDb.Floris, "VED")]
    [InlineData("personal", TestDb.Floris, "")]
    // ProjectManager role counts as >= TeamLead for team rules (same department)
    [InlineData("reports", TestDb.Camille, "VED")]
    [InlineData("personal", TestDb.Camille, "")]
    // Team Lead without a department gets no team rights
    [InlineData("reports", NoDeptLead, "")]
    // Share recipient
    [InlineData("shared", TestDb.Avery, "V")]
    // Administrator
    [InlineData("personal", TestDb.Admin, "VED")]
    [InlineData("project", TestDb.Admin, "VED")]
    // Unrelated users
    [InlineData("project", TestDb.Avery, "")]
    [InlineData("reports", TestDb.Avery, "")]
    [InlineData("personal", TestDb.Avery, "")]
    [InlineData("reports", SalesMember, "")]
    public async Task Permissions_and_visibility_follow_access_matrix(string docKey, int userId, string expected)
    {
        var (db, docs) = Arrange();
        using var _ = db;
        var service = db.CreateDocumentService();
        var user = db.For(userId);

        var perms = await service.GetPermissionsAsync(docs[docKey], user);
        var visible = await service.GetDocumentAsync(docs[docKey], user);

        Assert.Equal(expected, Encode(perms));
        Assert.Equal(perms.CanView, visible != null);
    }

    [Fact]
    public async Task Unknown_document_returns_no_access()
    {
        using var db = new TestDb();
        var service = db.CreateDocumentService();

        Assert.Equal(DocumentPermissions.None, await service.GetPermissionsAsync(999, db.For(TestDb.Admin)));
        Assert.Null(await service.GetDocumentAsync(999, db.For(TestDb.Admin)));
    }

    private static string Encode(DocumentPermissions p) =>
        (p.CanView ? "V" : "") + (p.CanEditMetadata ? "E" : "") + (p.CanReplace ? "R" : "") +
        (p.CanDelete ? "D" : "") + (p.CanShare ? "S" : "");
}
