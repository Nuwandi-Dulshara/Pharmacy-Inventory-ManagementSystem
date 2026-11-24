using System.Security.Cryptography;
using System.IO;
using Microsoft.Data.Sqlite;

namespace MediStock;

public enum AuthMode
{
    Login,
    Register
}

public sealed record AuthenticatedUser(string FullName, string PharmacyName, string Role);
public sealed record ProfileRecord(string FullName, string Email, string PhoneNumber, string PharmacyName);

public sealed class AuthService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 600_000;
    private static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(SaltSize);

    private readonly string _databasePath;

    public AuthService()
    {
        var configuredPath = Environment.GetEnvironmentVariable("MEDISTOCK_DATABASE_PATH");
        _databasePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MediStock",
                "medistock.db")
            : Path.GetFullPath(configuredPath);
    }

    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FullName TEXT NOT NULL,
                PharmacyName TEXT NOT NULL,
                Email TEXT NOT NULL COLLATE NOCASE UNIQUE,
                PhoneNumber TEXT NOT NULL,
                PasswordSalt BLOB NOT NULL,
                PasswordHash BLOB NOT NULL,
                Role TEXT NOT NULL CHECK (Role IN ('Administrator', 'Staff')),
                CreatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                Description TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS Medicines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                GenericName TEXT NULL,
                CategoryId INTEGER NULL REFERENCES Categories(Id) ON DELETE RESTRICT,
                Brand TEXT NULL,
                SellingPrice INTEGER NOT NULL CHECK (SellingPrice >= 0),
                Description TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Suppliers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ContactPerson TEXT NULL,
                Phone TEXT NOT NULL,
                Email TEXT NULL,
                Address TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS StockBatches (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MedicineId INTEGER NOT NULL REFERENCES Medicines(Id) ON DELETE RESTRICT,
                SupplierId INTEGER NOT NULL REFERENCES Suppliers(Id) ON DELETE RESTRICT,
                BatchNumber TEXT NOT NULL,
                Quantity INTEGER NOT NULL CHECK (Quantity >= 0),
                PurchasePrice INTEGER NOT NULL CHECK (PurchasePrice >= 0),
                ExpiryDate TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UNIQUE (MedicineId, BatchNumber)
            );

            CREATE TABLE IF NOT EXISTS Sales (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                InvoiceNumber TEXT NOT NULL UNIQUE,
                SoldAtUtc TEXT NOT NULL,
                TotalAmount INTEGER NOT NULL CHECK (TotalAmount >= 0),
                PaymentMethod TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Completed'
            );

            CREATE TABLE IF NOT EXISTS SaleItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SaleId INTEGER NOT NULL REFERENCES Sales(Id) ON DELETE RESTRICT,
                MedicineId INTEGER NOT NULL REFERENCES Medicines(Id) ON DELETE RESTRICT,
                StockBatchId INTEGER NOT NULL REFERENCES StockBatches(Id) ON DELETE RESTRICT,
                Quantity INTEGER NOT NULL CHECK (Quantity > 0),
                UnitPrice INTEGER NOT NULL CHECK (UnitPrice >= 0),
                LineTotal INTEGER NOT NULL CHECK (LineTotal >= 0)
            );

            CREATE TABLE IF NOT EXISTS StockAdjustments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockBatchId INTEGER NOT NULL REFERENCES StockBatches(Id) ON DELETE RESTRICT,
                QuantityChange INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                AdjustedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RefundHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SaleId INTEGER NOT NULL UNIQUE REFERENCES Sales(Id) ON DELETE RESTRICT,
                Action TEXT NOT NULL CHECK (Action IN ('Refund', 'Void')),
                Reason TEXT NOT NULL,
                AuthorizedUser TEXT NOT NULL,
                AuthorizedUserEmail TEXT NOT NULL,
                Amount INTEGER NOT NULL CHECK (Amount >= 0),
                CreatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Medicines_CategoryId ON Medicines(CategoryId);
            CREATE INDEX IF NOT EXISTS IX_StockBatches_Medicine_Expiry ON StockBatches(MedicineId, ExpiryDate);
            CREATE INDEX IF NOT EXISTS IX_Sales_SoldAtUtc ON Sales(SoldAtUtc);
            CREATE INDEX IF NOT EXISTS IX_SaleItems_SaleId ON SaleItems(SaleId);
            """;
        command.ExecuteNonQuery();

        AddColumnIfMissing(connection, "Sales", "VoidReason", "TEXT NULL");
        AddColumnIfMissing(connection, "Sales", "RefundedAtUtc", "TEXT NULL");
    }

    public string Register(string fullName, string pharmacyName, string email, string phoneNumber, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction(deferred: false);
        using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = "SELECT COUNT(*) FROM Users;";
        var role = Convert.ToInt64(countCommand.ExecuteScalar()) == 0 ? "Administrator" : "Staff";

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO Users (FullName, PharmacyName, Email, PhoneNumber, PasswordSalt, PasswordHash, Role, CreatedUtc)
            VALUES ($fullName, $pharmacyName, $email, $phoneNumber, $salt, $hash, $role, $createdUtc);
            """;
        insertCommand.Parameters.AddWithValue("$fullName", fullName);
        insertCommand.Parameters.AddWithValue("$pharmacyName", pharmacyName);
        insertCommand.Parameters.AddWithValue("$email", email);
        insertCommand.Parameters.AddWithValue("$phoneNumber", phoneNumber);
        insertCommand.Parameters.Add("$salt", SqliteType.Blob).Value = salt;
        insertCommand.Parameters.Add("$hash", SqliteType.Blob).Value = hash;
        insertCommand.Parameters.AddWithValue("$role", role);
        insertCommand.Parameters.AddWithValue("$createdUtc", DateTimeOffset.UtcNow.ToString("O"));
        insertCommand.ExecuteNonQuery();

        transaction.Commit();
        return role;
    }

    public AuthenticatedUser? Login(string email, string password)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FullName, PharmacyName, PasswordSalt, PasswordHash, Role
            FROM Users
            WHERE Email = $email COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$email", email);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            _ = HashPassword(password, DummySalt);
            return null;
        }

        var fullName = reader.GetString(0);
        var pharmacyName = reader.GetString(1);
        var salt = (byte[])reader["PasswordSalt"];
        var expectedHash = (byte[])reader["PasswordHash"];
        var role = reader.GetString(4);
        var actualHash = HashPassword(password, salt);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash)
            ? new AuthenticatedUser(fullName, pharmacyName, role)
            : null;
    }

    public string? GetRememberedEmail()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = 'RememberedEmail';";
        return command.ExecuteScalar() as string;
    }

    public void SetRememberedEmail(string? email)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        if (email is null)
        {
            command.CommandText = "DELETE FROM AppSettings WHERE Key = 'RememberedEmail';";
        }
        else
        {
            command.CommandText = """
                INSERT INTO AppSettings (Key, Value) VALUES ('RememberedEmail', $email)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            command.Parameters.AddWithValue("$email", email);
        }

        command.ExecuteNonQuery();
    }

    public ProfileRecord? GetProfile(string email)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FullName, Email, PhoneNumber, PharmacyName
            FROM Users WHERE Email = $email COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$email", email);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ProfileRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    public AuthenticatedUser? GetAuthenticatedUser(string email)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FullName, PharmacyName, Role
            FROM Users WHERE Email = $email COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$email", email);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new AuthenticatedUser(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    public bool UpdateProfile(
        string email,
        string fullName,
        string phoneNumber,
        string pharmacyName,
        string? currentPassword,
        string? newPassword,
        out bool passwordIncorrect)
    {
        passwordIncorrect = false;
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        byte[]? newSalt = null;
        byte[]? newHash = null;
        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            using var credentials = connection.CreateCommand();
            credentials.Transaction = transaction;
            credentials.CommandText = """
                SELECT PasswordSalt, PasswordHash FROM Users
                WHERE Email=$email COLLATE NOCASE;
                """;
            credentials.Parameters.AddWithValue("$email", email);
            using var reader = credentials.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            var salt = (byte[])reader["PasswordSalt"];
            var hash = (byte[])reader["PasswordHash"];
            if (string.IsNullOrWhiteSpace(currentPassword) ||
                !CryptographicOperations.FixedTimeEquals(HashPassword(currentPassword, salt), hash))
            {
                passwordIncorrect = true;
                return false;
            }

            newSalt = RandomNumberGenerator.GetBytes(SaltSize);
            newHash = HashPassword(newPassword, newSalt);
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Users
            SET FullName=$fullName,
                PhoneNumber=$phone,
                PharmacyName=$pharmacy,
                PasswordSalt=COALESCE($salt, PasswordSalt),
                PasswordHash=COALESCE($hash, PasswordHash)
            WHERE Email=$email COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$fullName", fullName);
        command.Parameters.AddWithValue("$phone", phoneNumber);
        command.Parameters.AddWithValue("$pharmacy", pharmacyName);
        command.Parameters.Add("$salt", SqliteType.Blob).Value = (object?)newSalt ?? DBNull.Value;
        command.Parameters.Add("$hash", SqliteType.Blob).Value = (object?)newHash ?? DBNull.Value;
        command.Parameters.AddWithValue("$email", email);
        var updated = command.ExecuteNonQuery() > 0;
        transaction.Commit();
        return updated;
    }

    public bool ChangePassword(string email, string currentPassword, string newPassword)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PasswordSalt, PasswordHash FROM Users
            WHERE Email=$email COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$email", email);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return false;
        var salt = (byte[])reader["PasswordSalt"];
        var hash = (byte[])reader["PasswordHash"];
        if (!CryptographicOperations.FixedTimeEquals(HashPassword(currentPassword, salt), hash)) return false;
        reader.Close();

        var newSalt = RandomNumberGenerator.GetBytes(SaltSize);
        var newHash = HashPassword(newPassword, newSalt);
        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE Users SET PasswordSalt=$salt, PasswordHash=$hash
            WHERE Email=$email COLLATE NOCASE;
            """;
        update.Parameters.Add("$salt", SqliteType.Blob).Value = newSalt;
        update.Parameters.Add("$hash", SqliteType.Blob).Value = newHash;
        update.Parameters.AddWithValue("$email", email);
        return update.ExecuteNonQuery() > 0;
    }

    public string BackupDatabase(string destinationPath)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        using var source = new SqliteConnection(ConnectionString);
        source.Open();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullDestination,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        return fullDestination;
    }

    public void RestoreDatabase(string sourcePath)
    {
        var fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSource)) throw new FileNotFoundException("Backup file not found.", fullSource);
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullSource,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        source.Open();
        using var destination = new SqliteConnection(ConnectionString);
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        reader.Close();

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
}
