using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ReFuel_Macedonia.Data;

public sealed record SchemaRepairResult(bool DatabaseCreated, int TablesAdded, int ColumnsAdded, bool Rebuilt, int AccountsPreserved)
{
    public bool Changed => DatabaseCreated || Rebuilt || TablesAdded > 0 || ColumnsAdded > 0;
}

public static class SchemaGuard
{
    private static readonly string[] IdentityTables =
    {
        "AspNetRoles",
        "AspNetUsers",
        "AspNetUserRoles",
        "AspNetUserClaims",
        "AspNetUserLogins",
        "AspNetUserTokens",
        "AspNetRoleClaims"
    };

    public static async Task<SchemaRepairResult> EnsureUpToDateAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.Database.EnsureCreatedAsync())
        {
            logger.LogInformation("refuel.db did not exist and was created from the current model.");
            return new SchemaRepairResult(true, 0, 0, false, 0);
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            var (tables, columns) = await RepairAsync(db, logger);

            await transaction.CommitAsync();

            if (tables > 0 || columns > 0)
                logger.LogInformation("Schema repaired without data loss: {Tables} table(s) and {Columns} column(s) added.", tables, columns);

            return new SchemaRepairResult(false, tables, columns, false, 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The schema could not be repaired in place. Rebuilding refuel.db and restoring the accounts.");

            var backup = await CaptureIdentityAsync(db);

            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            var restored = await RestoreIdentityAsync(db, backup, logger);

            logger.LogInformation("refuel.db was rebuilt. {Count} account(s) were carried over.", restored);
            return new SchemaRepairResult(false, 0, 0, true, restored);
        }
    }

    private static async Task<(int Tables, int Columns)> RepairAsync(ApplicationDbContext db, ILogger logger)
    {
        var statements = SplitStatements(db.Database.GenerateCreateScript());
        var shape = await ReadShapeAsync(db);

        var tablesAdded = 0;
        var columnsAdded = 0;

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;
            if (shape.ContainsKey(table)) continue;

            var executed = 0;

            foreach (var sql in StatementsFor(statements, table))
            {
                await db.Database.ExecuteSqlRawAsync(sql);
                executed++;
            }

            if (executed == 0)
                throw new InvalidOperationException($"No CREATE TABLE statement was generated for '{table}'.");

            logger.LogInformation("Added the missing table {Table}.", table);
            tablesAdded++;
        }

        if (tablesAdded > 0) shape = await ReadShapeAsync(db);

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;
            if (!shape.TryGetValue(table, out var present)) continue;

            var storeObject = StoreObjectIdentifier.Table(table, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(storeObject);
                if (string.IsNullOrEmpty(column)) continue;
                if (present.Contains(column)) continue;

                await db.Database.ExecuteSqlRawAsync(AddColumnSql(table, column, property));

                logger.LogInformation("Added the missing column {Table}.{Column}.", table, column);
                present.Add(column);
                columnsAdded++;
            }
        }

        foreach (var statement in statements)
        {
            var trimmed = statement.TrimStart();
            if (!trimmed.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase)) continue;
            if (!trimmed.Contains(" INDEX ", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                await db.Database.ExecuteSqlRawAsync(WithIfNotExists(statement));
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                logger.LogWarning(ex, "An index could not be created. Existing data may not satisfy it: {Statement}", trimmed);
            }
        }

        return (tablesAdded, columnsAdded);
    }

    private static string WithIfNotExists(string createIndex)
    {
        var trimmed = createIndex.TrimStart();

        if (trimmed.StartsWith("CREATE UNIQUE INDEX IF NOT EXISTS", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("CREATE INDEX IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("CREATE UNIQUE INDEX ", StringComparison.OrdinalIgnoreCase))
            return "CREATE UNIQUE INDEX IF NOT EXISTS " + trimmed["CREATE UNIQUE INDEX ".Length..];

        if (trimmed.StartsWith("CREATE INDEX ", StringComparison.OrdinalIgnoreCase))
            return "CREATE INDEX IF NOT EXISTS " + trimmed["CREATE INDEX ".Length..];

        return trimmed;
    }

    private static string AddColumnSql(string table, string column, IProperty property)
    {
        var type = property.GetColumnType();
        if (string.IsNullOrWhiteSpace(type)) type = StoreTypeFor(property);

        var sql = new StringBuilder();
        sql.Append("ALTER TABLE ").Append(Quote(table))
           .Append(" ADD COLUMN ").Append(Quote(column))
           .Append(' ').Append(type);

        if (!property.IsNullable)
            sql.Append(" NOT NULL DEFAULT ").Append(DefaultLiteralFor(type, property));

        return sql.ToString();
    }

    private static string StoreTypeFor(IProperty property)
    {
        var clr = property.GetProviderClrType() ?? property.ClrType;
        clr = Nullable.GetUnderlyingType(clr) ?? clr;

        if (clr == typeof(byte[])) return "BLOB";
        if (clr == typeof(double) || clr == typeof(float) || clr == typeof(decimal)) return "REAL";
        if (clr == typeof(bool) || clr == typeof(int) || clr == typeof(long) ||
            clr == typeof(short) || clr == typeof(byte)) return "INTEGER";

        return "TEXT";
    }

    private static string DefaultLiteralFor(string storeType, IProperty property)
    {
        var type = storeType.ToUpperInvariant();

        if (type.Contains("INT")) return "0";
        if (type.Contains("REAL") || type.Contains("DOUB") || type.Contains("FLOA") || type.Contains("NUM") || type.Contains("DEC")) return "0";
        if (type.Contains("BLOB")) return "x''";

        var clr = property.GetProviderClrType() ?? property.ClrType;
        clr = Nullable.GetUnderlyingType(clr) ?? clr;

        if (clr == typeof(DateTime)) return "'0001-01-01 00:00:00'";
        if (clr == typeof(DateTimeOffset)) return "'0001-01-01 00:00:00+00:00'";
        if (clr == typeof(TimeSpan)) return "'00:00:00'";
        if (clr == typeof(Guid)) return "'00000000-0000-0000-0000-000000000000'";

        return "''";
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static List<string> SplitStatements(string script)
    {
        var statements = new List<string>();
        var current = new StringBuilder();

        foreach (var raw in script.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase)) continue;

            current.Append(line).Append('\n');

            if (line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
            {
                var statement = current.ToString().Trim().TrimEnd(';').Trim();
                if (statement.Length > 0) statements.Add(statement);
                current.Clear();
            }
        }

        var tail = current.ToString().Trim().TrimEnd(';').Trim();
        if (tail.Length > 0) statements.Add(tail);

        return statements;
    }

    private static IEnumerable<string> StatementsFor(IReadOnlyList<string> statements, string table)
    {
        var quoted = Quote(table);

        foreach (var statement in statements)
        {
            if (statement.TrimStart().StartsWith("CREATE TABLE " + quoted, StringComparison.OrdinalIgnoreCase))
                yield return statement;
        }

        foreach (var statement in statements)
        {
            var trimmed = statement.TrimStart();
            if (!trimmed.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase)) continue;
            if (!trimmed.Contains(" INDEX ", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.Contains(" ON " + quoted + " ", StringComparison.OrdinalIgnoreCase)) yield return statement;
        }
    }

    private static async Task<Dictionary<string, HashSet<string>>> ReadShapeAsync(ApplicationDbContext db)
    {
        var shape = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            opened = true;
        }

        try
        {
            var tables = new List<string>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            }

            foreach (var table in tables)
            {
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA table_info(" + Quote(table) + ")";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));

                shape[table] = columns;
            }
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }

        return shape;
    }

    private static async Task<Dictionary<string, List<Dictionary<string, object?>>>> CaptureIdentityAsync(ApplicationDbContext db)
    {
        var captured = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

        var connection = db.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            opened = true;
        }

        try
        {
            foreach (var table in IdentityTables)
            {
                var rows = new List<Dictionary<string, object?>>();

                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM " + Quote(table);
                    await using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        for (var i = 0; i < reader.FieldCount; i++)
                            row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);

                        rows.Add(row);
                    }
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    continue;
                }

                captured[table] = rows;
            }
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }

        return captured;
    }

    private static async Task<int> RestoreIdentityAsync(
        ApplicationDbContext db,
        Dictionary<string, List<Dictionary<string, object?>>> backup,
        ILogger logger)
    {
        if (backup.Count == 0) return 0;

        var shape = await ReadShapeAsync(db);
        var users = 0;

        var connection = db.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            opened = true;
        }

        try
        {
            foreach (var table in IdentityTables)
            {
                if (!backup.TryGetValue(table, out var rows) || rows.Count == 0) continue;
                if (!shape.TryGetValue(table, out var columns)) continue;

                foreach (var row in rows)
                {
                    var usable = row.Keys.Where(columns.Contains).ToList();
                    if (usable.Count == 0) continue;

                    using var command = connection.CreateCommand();

                    var names = string.Join(", ", usable.Select(Quote));
                    var parameters = string.Join(", ", usable.Select((_, i) => "@p" + i));

                    command.CommandText = "INSERT OR IGNORE INTO " + Quote(table) + " (" + names + ") VALUES (" + parameters + ")";

                    for (var i = 0; i < usable.Count; i++)
                    {
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = "@p" + i;
                        parameter.Value = row[usable[i]] ?? DBNull.Value;
                        command.Parameters.Add(parameter);
                    }

                    try
                    {
                        await command.ExecuteNonQueryAsync();
                        if (string.Equals(table, "AspNetUsers", StringComparison.OrdinalIgnoreCase)) users++;
                    }
                    catch (Microsoft.Data.Sqlite.SqliteException ex)
                    {
                        logger.LogDebug(ex, "A row of {Table} could not be carried over.", table);
                    }
                }
            }
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }

        return users;
    }
}
