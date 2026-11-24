using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;

namespace MediStock.Pages.Account;

public sealed class RegisterModel(AuthService authService, ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public RegistrationInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        var phoneNumber = Input.PhoneNumber ?? string.Empty;
        var digitCount = phoneNumber.Count(char.IsDigit);
        if (digitCount is < 7 or > 15)
        {
            ModelState.AddModelError("Input.PhoneNumber", "Enter a phone number containing 7 to 15 digits.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim().ToLowerInvariant();
        try
        {
            var role = authService.Register(
                Input.FullName.Trim(),
                Input.PharmacyName.Trim(),
                email,
                phoneNumber.Trim(),
                Input.Password);

            TempData["StatusMessage"] = $"Account created. You are the pharmacy {role}. Please log in.";
            return RedirectToPage("/Account/Login", new { email });
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            ModelState.AddModelError("Input.Email", "An account with this email address already exists.");
            return Page();
        }
        catch (SqliteException exception)
        {
            logger.LogError(exception, "Could not save a new MediStock account.");
            ModelState.AddModelError(string.Empty, "MediStock couldn't save your account to the local database.");
            return Page();
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Could not access the MediStock account database.");
            ModelState.AddModelError(string.Empty, "MediStock couldn't save your account. Check available disk space and folder permissions.");
            return Page();
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogError(exception, "Access denied to the MediStock account database.");
            ModelState.AddModelError(string.Empty, "MediStock doesn't have permission to save accounts in its local database.");
            return Page();
        }
    }

    public sealed class RegistrationInput
    {
        [Required(ErrorMessage = "Enter your full name.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 100 characters.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your pharmacy name.")]
        [StringLength(120, MinimumLength = 2, ErrorMessage = "Pharmacy name must be between 2 and 120 characters.")]
        public string PharmacyName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your phone number.")]
        [RegularExpression(@"^\+?[\d\s().-]{7,24}$", ErrorMessage = "Enter a valid phone number.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Create a password.")]
        [StringLength(128, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Password must include uppercase and lowercase letters and a number.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm your password.")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
