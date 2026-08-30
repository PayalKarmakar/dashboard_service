using System.Security.Cryptography;
using System.Text;
using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class LoginService
{
    private readonly ConfigurationService _configurationService;

    public LoginService()
    {
        _configurationService = new ConfigurationService();
    }

    public async Task<User?> ValidateLoginAsync(string userName, string password)
    {
        await EnsureDefaultAdminAsync();

        string connectionString = _configurationService.GetConnectionString();
        string passwordHash = HashPassword(password);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                u.user_id,
                u.username,
                u.full_name,
                u.password_hash,
                r.role_name,
                u.is_active
            FROM public.users u
            INNER JOIN public.roles r ON r.role_id = u.role_id
            WHERE LOWER(u.username) = LOWER(@userName)
            LIMIT 1;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userName", userName);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        bool isActive = reader.GetBoolean(5);
        string storedHash = reader.GetString(3);

        if (!isActive)
        {
            return null;
        }

        if (!string.Equals(storedHash, passwordHash, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var user = new User
        {
            UserId = reader.GetInt64(0),
            UserName = reader.GetString(1),
            FullName = reader.GetString(2),
            PasswordHash = storedHash,
            Role = reader.GetString(4),
            IsActive = isActive
        };

        await reader.CloseAsync();

        const string updateSql = @"
            UPDATE public.users
            SET last_login = NOW(),
                updated_at = NOW()
            WHERE user_id = @userId;
        ";

        await using var updateCommand = new NpgsqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("userId", user.UserId);
        await updateCommand.ExecuteNonQueryAsync();

        return user;
    }

    private async Task EnsureDefaultAdminAsync()
    {
        string connectionString = _configurationService.GetConnectionString();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var countCommand = new NpgsqlCommand(
            "SELECT COUNT(*) FROM public.users;",
            connection);

        var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync());

        if (count > 0)
        {
            return;
        }

        const string insertSql = @"
            INSERT INTO public.users
            (
                username,
                full_name,
                email,
                password_hash,
                role_id,
                is_active
            )
            VALUES
            (
                'admin',
                'Administrator',
                'admin@srp.local',
                @passwordHash,
                1,
                TRUE
            );
        ";

        await using var insertCommand = new NpgsqlCommand(insertSql, connection);
        insertCommand.Parameters.AddWithValue("passwordHash", HashPassword("admin123"));
        await insertCommand.ExecuteNonQueryAsync();
    }

    public async Task<(bool Success, string? ErrorMessage)> ChangePasswordAsync(
        string userName,
        string currentPassword,
        string newPassword)
    {
        string connectionString = _configurationService.GetConnectionString();
        string currentHash = HashPassword(currentPassword);
        string newHash = HashPassword(newPassword);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        const string selectSql = @"
            SELECT user_id, password_hash, is_active
            FROM public.users
            WHERE LOWER(username) = LOWER(@userName)
            LIMIT 1;
        ";

        await using var selectCommand = new NpgsqlCommand(selectSql, connection);
        selectCommand.Parameters.AddWithValue("userName", userName);

        await using var reader = await selectCommand.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return (false, "User ID not found.");
        }

        long userId = reader.GetInt64(0);
        string storedHash = reader.GetString(1);
        bool isActive = reader.GetBoolean(2);

        await reader.CloseAsync();

        if (!isActive)
        {
            return (false, "This user account is inactive.");
        }

        if (!string.Equals(storedHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Current password is incorrect.");
        }

        const string updateSql = @"
            UPDATE public.users
            SET password_hash = @passwordHash,
                updated_at = NOW()
            WHERE user_id = @userId;
        ";

        await using var updateCommand = new NpgsqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("passwordHash", newHash);
        updateCommand.Parameters.AddWithValue("userId", userId);
        await updateCommand.ExecuteNonQueryAsync();

        return (true, null);
    }

    public static string HashPassword(string password)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hash);
    }
}
