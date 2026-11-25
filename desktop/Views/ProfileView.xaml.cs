using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MediStock;
using Microsoft.Data.Sqlite;

namespace MediStock.Desktop.Views;

public partial class ProfileView : UserControl
{
    private readonly DesktopServices _services;
    private readonly string _email;
    private readonly Action _profileUpdated;

    public ProfileView(DesktopServices services, string email, Action profileUpdated)
    {
        InitializeComponent();
        _services = services;
        _email = email;
        _profileUpdated = profileUpdated;
        LoadProfile();
    }

    private void LoadProfile()
    {
        try
        {
            var profile = _services.Auth.GetProfile(_email);
            if (profile is null)
            {
                StatusText.Text = "The account could not be found.";
                return;
            }
            FullNameBox.Text = profile.FullName;
            EmailBox.Text = profile.Email;
            PhoneBox.Text = profile.PhoneNumber;
            PharmacyBox.Text = profile.PharmacyName;
        }
        catch (Exception exception) when (
            exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not load profile: {exception.Message}";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var fullName = FullNameBox.Text.Trim();
        var phone = PhoneBox.Text.Trim();
        var pharmacy = PharmacyBox.Text.Trim();
        var newPassword = NewPasswordBox.Password;
        var confirmPassword = ConfirmPasswordBox.Password;

        if (fullName.Length is 0 or > 100 || phone.Length is 0 or > 30 ||
            !new PhoneAttribute().IsValid(phone) || pharmacy.Length is 0 or > 120)
        {
            StatusText.Text = "Enter a valid name, phone number, and pharmacy name.";
            return;
        }
        if (newPassword.Length > 0 && newPassword.Length < 10)
        {
            StatusText.Text = "New passwords must be at least 10 characters.";
            return;
        }
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            StatusText.Text = "The new password and confirmation do not match.";
            return;
        }

        try
        {
            var updated = _services.Auth.UpdateProfile(
                _email,
                fullName,
                phone,
                pharmacy,
                CurrentPasswordBox.Password,
                string.IsNullOrEmpty(newPassword) ? null : newPassword,
                out var passwordIncorrect);
            if (!updated)
            {
                StatusText.Text = passwordIncorrect
                    ? "The current password is incorrect."
                    : "The profile could not be updated.";
                return;
            }

            var authenticatedUser = _services.Auth.GetAuthenticatedUser(_email);
            if (authenticatedUser is null)
            {
                StatusText.Text = "Profile saved, but the updated account could not be reloaded.";
                return;
            }

            _profileUpdated();
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            StatusText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"];
            StatusText.Text = "Profile updated. Your navigation name and pharmacy details are refreshed.";
        }
        catch (Exception exception) when (
            exception is SqliteException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            StatusText.Text = $"Could not update profile: {exception.Message}";
        }
    }
}
