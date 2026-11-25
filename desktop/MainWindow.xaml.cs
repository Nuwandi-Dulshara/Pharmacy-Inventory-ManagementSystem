using System.Windows;
using MediStock;
using MediStock.Desktop.Views;

namespace MediStock.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopServices _services;
    private AuthenticatedUser _user;
    private readonly string _email;

    public MainWindow(DesktopServices services, AuthenticatedUser user, string email)
    {
        InitializeComponent();
        _services = services;
        _user = user;
        _email = email;
        UserLabel.Text = $"{user.FullName}\n{user.PharmacyName}\n{user.Role}";
        SettingsButton.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        CategoriesButton.Visibility = IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        NavigateDashboard();
    }

    private bool IsAdministrator => string.Equals(_user.Role, "Administrator", StringComparison.OrdinalIgnoreCase);

    private void Dashboard_Click(object sender, RoutedEventArgs e) => NavigateDashboard();

    private void Inventory_Click(object sender, RoutedEventArgs e) =>
        PageHost.Content = new InventoryView(_services, IsAdministrator);

    private void Sales_Click(object sender, RoutedEventArgs e) =>
        PageHost.Content = new SalesView(_services, _user, _email);

    private void Invoices_Click(object sender, RoutedEventArgs e) =>
        PageHost.Content = new InvoicesView(_services, _user, _email);

    private void Expiry_Click(object sender, RoutedEventArgs e) =>
        PageHost.Content = new ExpiryView(_services, IsAdministrator);

    private void Predictions_Click(object sender, RoutedEventArgs e) =>
        PageHost.Content = new PredictionsView(_services);

    private void Reports_Click(object sender, RoutedEventArgs e) =>
        PageHost.Content = new ReportsView(_services, _user);

    private void Categories_Click(object sender, RoutedEventArgs e)
    {
        if (IsAdministrator)
            PageHost.Content = new CategoriesView(_services);
    }

    private void Profile_Click(object sender, RoutedEventArgs e) =>
        PageHost.Content = new ProfileView(_services, _email, RefreshAuthenticatedUser);

    private void RefreshAuthenticatedUser()
    {
        var updated = _services.Auth.GetAuthenticatedUser(_email);
        if (updated is null) return;
        _user = updated;
        UserLabel.Text = $"{updated.FullName}\n{updated.PharmacyName}\n{updated.Role}";
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (IsAdministrator)
            PageHost.Content = new SettingsView(_services);
    }

    private void NavigateDashboard() => PageHost.Content = new DashboardView(_services, _user);

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        new LoginWindow().Show();
        Close();
    }
}
