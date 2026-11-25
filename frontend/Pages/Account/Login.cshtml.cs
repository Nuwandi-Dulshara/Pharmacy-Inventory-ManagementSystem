using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;

namespace MediStock.Pages.Account;

public sealed class LoginModel(
    AuthService authService,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    public void OnGet(string? email)
    {
        Input.Email = string.IsNullOrWhiteSpace(email)
            ? authService.GetRememberedEmail() ?? string.Empty
            : email;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        try
        {
            var user = authService.Login(email, Input.Password);
            if (user is null)
            {
                ModelState.AddModelError(string.Empty, "The email or password is incorrect. Please try again.");
                return Page();
            }

            authService.SetRememberedEmail(Input.RememberEmail ? email : null);
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("PharmacyName", user.PharmacyName)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = Input.RememberEmail });

            return RedirectToPage("/Dashboard");
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Could not access the MediStock account database during login.");
            ModelState.AddModelError(string.Empty, "MediStock couldn't access the account database.");
            return Page();
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Could not access the MediStock account database during login.");
            ModelState.AddModelError(string.Empty, "MediStock couldn't access the local account database.");
            return Page();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogError(exception, "Access denied to the MediStock account database during login.");
            ModelState.AddModelError(string.Empty, "MediStock doesn't have permission to access the local account database.");
            return Page();
        }
    }

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your password.")]
        [StringLength(128)]
        public string Password { get; set; } = string.Empty;

        public bool RememberEmail { get; set; }
    }
}
