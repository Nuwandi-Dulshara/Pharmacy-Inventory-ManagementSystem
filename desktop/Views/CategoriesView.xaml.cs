using System.Windows;
using System.Windows.Controls;
using MediStock.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Desktop.Views;

public partial class CategoriesView : UserControl
{
    private readonly DesktopServices _services;
    private int? _selectedId;

    public CategoriesView(DesktopServices services)
    {
        InitializeComponent();
        _services = services;
        LoadCategories();
    }

    private void LoadCategories()
    {
        try
        {
            using var db = _services.CreateDbContext();
            var search = SearchBox.Text.Trim();
            var query = db.Categories.AsNoTracking().AsQueryable();
            if (search.Length > 0)
                query = query.Where(category => EF.Functions.Like(category.Name, $"%{search}%"));
            CategoriesGrid.ItemsSource = query.OrderBy(category => category.Name)
                .Select(category => new CategoryRow
                {
                    Id = category.Id,
                    Name = category.Name,
                    Description = category.Description,
                    IsActive = category.IsActive,
                    MedicineCount = category.Medicines.Count
                }).ToList();
        }
        catch (Exception exception) when (
            exception is SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not load categories: {exception.Message}";
        }
    }

    private void CategoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoriesGrid.SelectedItem is not CategoryRow row) return;
        _selectedId = row.Id;
        NameBox.Text = row.Name;
        DescriptionBox.Text = row.Description ?? string.Empty;
        ActiveCheckBox.IsChecked = row.IsActive;
        SaveButton.Content = "Save changes";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length is 0 or > 120 || DescriptionBox.Text.Length > 1000)
        {
            StatusText.Text = "Enter a category name (1–120 characters) and a description up to 1000 characters.";
            return;
        }

        try
        {
            using var db = _services.CreateDbContext();
            if (db.Categories.Any(category => category.Name == name && category.Id != (_selectedId ?? 0)))
            {
                StatusText.Text = "A category with this name already exists.";
                return;
            }

            if (_selectedId is int id)
            {
                var category = db.Categories.SingleOrDefault(item => item.Id == id);
                if (category is null)
                {
                    StatusText.Text = "The selected category no longer exists.";
                    return;
                }
                category.Name = name;
                category.Description = Clean(DescriptionBox.Text);
                category.IsActive = ActiveCheckBox.IsChecked == true;
            }
            else
            {
                db.Categories.Add(new Category
                {
                    Name = name,
                    Description = Clean(DescriptionBox.Text),
                    IsActive = ActiveCheckBox.IsChecked == true
                });
            }

            db.SaveChanges();
            ClearForm();
            LoadCategories();
            StatusText.Text = "Category saved.";
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not save category: {exception.Message}";
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (CategoriesGrid.SelectedItem is not CategoryRow row)
        {
            StatusText.Text = "Select a category to delete.";
            return;
        }
        if (MessageBox.Show($"Delete category “{row.Name}”?", "Confirm category deletion",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var db = _services.CreateDbContext();
            var category = db.Categories.SingleOrDefault(item => item.Id == row.Id);
            if (category is null)
            {
                StatusText.Text = "The selected category no longer exists.";
                return;
            }
            if (db.Medicines.Any(medicine => medicine.CategoryId == category.Id))
            {
                StatusText.Text = "This category is used by medicines and cannot be deleted.";
                return;
            }
            db.Categories.Remove(category);
            db.SaveChanges();
            ClearForm();
            LoadCategories();
            StatusText.Text = "Category deleted.";
        }
        catch (Exception exception) when (
            exception is DbUpdateException or SqliteException or InvalidOperationException)
        {
            StatusText.Text = $"Could not delete category: {exception.Message}";
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => LoadCategories();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClearForm();
        SearchBox.Clear();
        LoadCategories();
    }

    private void ClearForm()
    {
        _selectedId = null;
        CategoriesGrid.SelectedItem = null;
        NameBox.Clear();
        DescriptionBox.Clear();
        ActiveCheckBox.IsChecked = true;
        SaveButton.Content = "Add category";
    }

    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class CategoryRow
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsActive { get; init; }
        public int MedicineCount { get; init; }
    }
}
