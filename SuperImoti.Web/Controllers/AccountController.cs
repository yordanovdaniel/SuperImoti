using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;
using SuperImoti.Web.ViewModels;

namespace SuperImoti.Web.Controllers;

/// <summary>Registration, login, logout and the user's profile (ASP.NET Core Identity).</summary>
public class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db,
    ILogger<AccountController> logger) : Controller
{
    // GET: /Account/Register
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new RegisterViewModel());
    }

    // POST: /Account/Register
    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email!.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = model.DisplayName!.Trim(),
            NotifyAboutNewListings = model.NotifyAboutNewListings,
        };

        var result = await userManager.CreateAsync(user, model.Password!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        logger.LogInformation("New user registered: {Email}.", email);
        await signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = $"Добре дошли, {user.DisplayName}! Регистрацията е успешна.";
        return RedirectToLocal(returnUrl);
    }

    // GET: /Account/Login
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    // POST: /Account/Login
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            model.Email!.Trim(), model.Password!, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            TempData["Success"] = "Влязохте успешно.";
            return RedirectToLocal(returnUrl);
        }

        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "Профилът е временно заключен след няколко грешни опита. Опитайте отново след 5 минути."
            : "Грешен имейл или парола.");
        return View(model);
    }

    // POST: /Account/Logout
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        TempData["Success"] = "Излязохте от профила си.";
        return RedirectToAction("Index", "Home");
    }

    // GET: /Account/AccessDenied
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    // GET: /Account/Profile
    [Authorize]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var input = new ProfileViewModel
        {
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            NotifyAboutNewListings = user.NotifyAboutNewListings,
        };

        return View(await BuildProfileAsync(user, input, ct));
    }

    // POST: /Account/Profile
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Profile([Bind(Prefix = "Input")] ProfileViewModel input, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildProfileAsync(user, input, ct));
        }

        user.DisplayName = input.DisplayName!.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
        user.NotifyAboutNewListings = input.NotifyAboutNewListings;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(await BuildProfileAsync(user, input, ct));
        }

        // Re-issue the login cookie so the menu shows the new name.
        await signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Профилът е обновен.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<ProfilePageViewModel> BuildProfileAsync(ApplicationUser user, ProfileViewModel input, CancellationToken ct) => new()
    {
        Input = input,
        Email = user.Email ?? string.Empty,
        CreatedUtc = user.CreatedUtc,
        ListingCount = await db.Listings.CountAsync(l => l.OwnerId == user.Id, ct),
        FavoriteCount = await db.Favorites.CountAsync(f => f.UserId == user.Id, ct),
        CommentCount = await db.Comments.CountAsync(c => c.AuthorId == user.Id, ct),
        IsAdmin = User.IsAdmin(),
    };

    private IActionResult RedirectToLocal(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
}
