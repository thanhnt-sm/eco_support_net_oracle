using DataGuard.Core.Models;

namespace DataGuard.Cli.Services;

/// <summary>Reads the database server version for baselines and snapshots.</summary>
internal static class DatabaseVersionReader
{
    internal static async Task<string> GetDatabaseVersionAsync(DataGuardConfiguration config, string provider, CancellationToken cancellationToken = default)
    {
        try
        {
            if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(config.ConnectionString);
                await conn.OpenAsync(cancellationToken);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT @@VERSION";
                var version = await cmd.ExecuteScalarAsync(cancellationToken);
                return version?.ToString() ?? "unknown";
            }
            else if (provider.Equals("oracle", StringComparison.OrdinalIgnoreCase))
            {
                using var conn = new global::Oracle.ManagedDataAccess.Client.OracleConnection(config.ConnectionString);
                await conn.OpenAsync(cancellationToken);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT banner FROM v$version WHERE banner LIKE 'Oracle%'";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    return reader.GetString(0);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to get DB version: {ex.Message}");
        }

        return "unknown";
    }
}
