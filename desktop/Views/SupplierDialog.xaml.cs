using System.Windows;

namespace MediStock.Desktop.Views;

public partial class SupplierDialog : Window
{
    public string SupplierName => NameBox.Text;
    public string Phone => PhoneBox.Text;
    public string Email => EmailBox.Text;
    public string Address => AddressBox.Text;

    public SupplierDialog() => InitializeComponent();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SupplierName) || string.IsNullOrWhiteSpace(Phone))
        {
            ErrorText.Text = "Supplier name and phone are required.";
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
