using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;

namespace MediStock.Pages.Profile;

[Authorize]
public sealed class EditModel(AuthService auth, ILogger<EditModel> logger) : PageModel
{
    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public IActionResult OnGet()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToPage("/Account/Login");
        }

        ProfileRecord? profile;
        try
        {
            profile = auth.GetProfile(email);
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Could not load profile for {Email}.", email);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (profile is null)
        {
            return NotFound();
        }

        Input = new ProfileInput
        {
            FullName = profile.FullName,
            PhoneNumber = profile.PhoneNumber,
            PharmacyName = profile.PharmacyName
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToPage("/Account/Login");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.IsNullOrEmpty(Input.NewPassword) || !string.IsNullOrEmpty(Input.ConfirmPassword))
        {
            if (string.IsNullOrEmpty(Input.NewPassword))
            {
                ModelState.AddModelError("Input.NewPassword", "Enter a new password.");
                return Page();
            }

            if (Input.NewPassword != Input.ConfirmPassword)
            {
                ModelState.AddModelError("Input.ConfirmPassword", "The passwords do not match.");
                return Page();
            }
        }

        bool updated;
        bool passwordIncorrect;
        try
        {
            updated = auth.UpdateProfile(
                email,
                Input.FullName.Trim(),
                Input.PhoneNumber.Trim(),
                Input.PharmacyName.Trim(),
                Input.CurrentPassword,
                Input.NewPassword,
                out passwordIncorrect);
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Could not save profile for {Email}.", email);
            ModelState.AddModelError(string.Empty, "Profile changes could not be saved.");
            return Page();
        }

        if (!updated)
        {
            ModelState.AddModelError(
                passwordIncorrect ? "Input.CurrentPassword" : string.Empty,
                passwordIncorrect ? "Current password is incorrect." : "Profile could not be updated.");
            return Page();
        }

        AuthenticatedUser? updatedUser;
        try
        {
            updatedUser = auth.GetAuthenticatedUser(email);
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Could not reload profile claims for {Email}.", email);
            ModelState.AddModelError(string.Empty, "Profile was saved, but updated account details could not be reloaded.");
            return Page();
        }
        if (updatedUser is null)
        {
            return NotFound();
        }

        var properties = (await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)).Properties
            ?? new AuthenticationProperties();
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, updatedUser.FullName),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, updatedUser.Role),
            new Claim("PharmacyName", updatedUser.PharmacyName),
            new Claim(ClaimTypes.MobilePhone, Input.PhoneNumber.Trim())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);

        TempData["StatusMessage"] = "Profile changes saved.";
        return RedirectToPage("Index");
    }
}

public sealed class ProfileInput
{
    [Required, StringLength(100)]
    public string FullName { get; set; } = "";

    [Required, Phone, StringLength(30)]
    public string PhoneNumber { get; set; } = "";

    [Required, StringLength(120)]
    public string PharmacyName { get; set; } = "";

    public string? CurrentPassword { get; set; }

    [StringLength(128, MinimumLength = 10)]
    public string? NewPassword { get; set; }

    public string? ConfirmPassword { get; set; }
}
