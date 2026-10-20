using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;

namespace ContosoDashboard.Pages;

// Serves stored documents. Files live outside wwwroot, so every request passes the service-layer authorization check.
[Authorize]
public class DocumentFileModel : PageModel
{
    private readonly IDocumentService _documentService;

    public DocumentFileModel(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    public async Task<IActionResult> OnGetAsync(int id, string? mode)
    {
        FileAccessMode accessMode;
        switch (mode?.ToLowerInvariant())
        {
            case null or "download": accessMode = FileAccessMode.Download; break;
            case "preview": accessMode = FileAccessMode.Preview; break;
            default: return BadRequest();
        }

        var user = User.ToUserContext();
        Response.Headers.CacheControl = "no-store, private";

        var file = await _documentService.OpenFileAsync(id, user, accessMode);
        if (file == null)
        {
            if (accessMode == FileAccessMode.Preview)
            {
                var document = await _documentService.GetDocumentAsync(id, user);
                if (document != null && !DocumentFileRules.IsPreviewable(document.FileType))
                    return PlainText(StatusCodes.Status400BadRequest, "This file type cannot be previewed. Download it instead.");
            }
            // Same response for "missing" and "not allowed" so guessed ids reveal nothing.
            return PlainText(StatusCodes.Status404NotFound, DocumentMessages.NotFound);
        }

        if (!file.Inline)
            return File(file.Content, file.ContentType, file.FileName);

        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = file.FileName }.ToString();
        return File(file.Content, file.ContentType);
    }

    private static ContentResult PlainText(int statusCode, string message) =>
        new() { StatusCode = statusCode, Content = message, ContentType = "text/plain; charset=utf-8" };
}
