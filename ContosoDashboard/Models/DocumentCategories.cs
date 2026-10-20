namespace ContosoDashboard.Models;

public static class DocumentCategories
{
    public const string ProjectDocuments = "Project Documents";
    public const string TeamResources = "Team Resources";
    public const string PersonalFiles = "Personal Files";
    public const string Reports = "Reports";
    public const string Presentations = "Presentations";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        ProjectDocuments, TeamResources, PersonalFiles, Reports, Presentations, Other
    };
}
