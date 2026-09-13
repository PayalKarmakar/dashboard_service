using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class UserService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<User>> GetAllAsync()
    {
        var users = new List<User>();
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                u.user_id,
                u.username,
                u.full_name,
                COALESCE(u.email, ''),
                u.role_id,
                r.role_name,
                u.is_active,
                u.last_login
            FROM public.users u
            INNER JOIN public.roles r ON r.role_id = u.role_id
            ORDER BY u.user_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add(new User
            {
                UserId = reader.GetInt64(0),
                UserName = reader.GetString(1),
                FullName = reader.GetString(2),
                Email = reader.GetString(3),
                RoleId = reader.GetInt32(4),
                Role = reader.GetString(5),
                IsActive = reader.GetBoolean(6),
                LastLogin = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
            });
        }

        return users;
    }

    public async Task<List<Role>> GetRolesAsync()
    {
        var roles = new List<Role>();
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT role_id, role_name
            FROM public.roles
            ORDER BY role_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            roles.Add(new Role
            {
                RoleId = reader.GetInt32(0),
                RoleName = reader.GetString(1)
            });
        }

        return roles;
    }

    public async Task AddAsync(User user, string password)
    {
        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            throw new Exception("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(user.FullName))
        {
            throw new Exception("Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            throw new Exception("Password must be at least 6 characters.");
        }

        if (user.RoleId <= 0)
        {
            throw new Exception("Select a role.");
        }

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
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
                @userName,
                @fullName,
                @email,
                @passwordHash,
                @roleId,
                @isActive
            );
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userName", user.UserName.Trim());
        command.Parameters.AddWithValue("fullName", user.FullName.Trim());
        command.Parameters.AddWithValue(
            "email",
            string.IsNullOrWhiteSpace(user.Email) ? DBNull.Value : user.Email.Trim());
        command.Parameters.AddWithValue("passwordHash", LoginService.HashPassword(password));
        command.Parameters.AddWithValue("roleId", user.RoleId);
        command.Parameters.AddWithValue("isActive", user.IsActive);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new Exception("Username or email already exists.");
        }
    }

    public async Task SetActiveAsync(long userId, bool isActive)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.users
            SET
                is_active = @isActive,
                updated_at = NOW()
            WHERE user_id = @id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("isActive", isActive);
        command.Parameters.AddWithValue("id", userId);
        await command.ExecuteNonQueryAsync();
    }
}
