using System.IO;
using System.Windows;
using System.Security.Cryptography;
using MediStock;
using Microsoft.Data.Sqlite;

namespace MediStock.Desktop;

public partial class LoginWindow : Window
{
    private readonly AuthService _auth = new();

    public LoginWindow()
    {
        InitializeComponent();
        try
        {
            _auth.Initialize();
            ThemeManager.Apply(new DesktopServices(_auth).GetSetting("Theme", "Light"));
            var remembered = _auth.GetRememberedEmail();
            if (!string.IsNullOrWhiteSpace(remembered))
            {
                EmailBox.Text = remembered;
                RememberCheckBox.IsChecked = true;
            }
        }
        catch (Exception exception) when (
            exception is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorText.Text = $"Unable to open the MediStock database: {exception.Message}";
        }
    }

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var email = EmailBox.Text.Trim();
        if (email.Length == 0 || PasswordBox.Password.Length == 0)
        {
            ErrorText.Text = "Enter your email and password.";
            return;
        }

        try
        {
            var user = _auth.Login(email, PasswordBox.Password);
            if (user is null)
            {
                ErrorText.Text = "The email or password is incorrect.";
                PasswordBox.Clear();
                return;
            }

            _auth.SetRememberedEmail(RememberCheckBox.IsChecked == true ? email : null);
            ThemeManager.Apply(new DesktopServices(_auth).GetSetting("Theme", "Light"));
            new MainWindow(new DesktopServices(_auth), user, email).Show();
            Close();
        }
        catch (Exception exception) when (
            exception is SqliteException or IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        {
            ErrorText.Text = $"Sign in failed: {exception.Message}";
        }
    }
}
