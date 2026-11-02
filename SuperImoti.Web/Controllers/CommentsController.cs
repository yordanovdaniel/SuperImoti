using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Controllers;

/// <summary>Comments under listings. Authors and administrators can delete them.</summary>
[Authorize]
public class CommentsController(ApplicationDbContext db) : Controller
{
    // POST: /Comments/Create
    [HttpPost]
    public async Task<IActionResult> Create([Bind(Prefix = "NewComment")] CommentFormModel model, CancellationToken ct)
    {
        if (!await db.Listings.AnyAsync(l => l.Id == model.ListingId, ct))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            TempData["CommentDraft"] = model.Text;
            return RedirectToComments(model.ListingId);
        }

        db.Comments.Add(new Comment
        {
            ListingId = model.ListingId,
            AuthorId = User.GetUserId()!,
            Text = model.Text!.Trim(),
        });
        await db.SaveChangesAsync(ct);

        TempData["Success"] = "Коментарът е публикуван.";
        return RedirectToComments(model.ListingId);
    }

    // POST: /Comments/Delete/5
    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comment is null)
        {
            return NotFound();
        }

        if (comment.AuthorId != User.GetUserId() && !User.IsAdmin())
        {
            return Forbid();
        }

        db.Comments.Remove(comment);
        await db.SaveChangesAsync(ct);

        TempData["Success"] = "Коментарът е изтрит.";
        return RedirectToComments(comment.ListingId);
    }

    private RedirectResult RedirectToComments(int listingId) =>
        Redirect(Url.Action("Details", "Listings", new { id = listingId }) + "#comments");
}
