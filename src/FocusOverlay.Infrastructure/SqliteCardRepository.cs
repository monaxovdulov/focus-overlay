using FocusOverlay.Core;
using Microsoft.Data.Sqlite;

namespace FocusOverlay.Infrastructure;

public sealed class SqliteCardRepository : ICardRepository
{
    private readonly string connectionString;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public SqliteCardRepository(string? path = null)
    {
        var dataDirectory = path is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusOverlay")
            : Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(dataDirectory);
        var databasePath = path ?? Path.Combine(dataDirectory, "focus-overlay.db");
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString();

        InitializeDatabase();
    }

    public async Task<IReadOnlyList<FocusCard>> GetAllAsync()
    {
        var cards = new List<FocusCard>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, type, title, content, image, done, image_fill, x, y, w, h, op, created, updated
            FROM cards
            ORDER BY created ASC
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            try
            {
                cards.Add(new FocusCard
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    Type = (CardType)reader.GetInt32(1),
                    Title = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Content = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    ImagePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Completed = reader.GetInt32(5) > 0,
                    ImageFill = reader.GetInt32(6) > 0,
                    X = reader.GetDouble(7),
                    Y = reader.GetDouble(8),
                    Width = reader.GetDouble(9),
                    Height = reader.GetDouble(10),
                    Opacity = 1,
                    CreatedUtc = ParseUtc(reader, 12),
                    UpdatedUtc = ParseUtc(reader, 13)
                });
            }
            catch
            {
                // A broken row must not prevent the remaining cards from loading.
            }
        }

        return cards;
    }

    public async Task SaveAsync(FocusCard card)
    {
        await writeGate.WaitAsync();
        try
        {
            card.Width = Math.Max(240, card.Width);
            card.Height = Math.Max(160, card.Height);
            card.Opacity = 1;
            card.UpdatedUtc = DateTime.UtcNow;

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO cards (
                    id, type, title, content, image, done, image_fill,
                    x, y, w, h, op, created, updated)
                VALUES (
                    $id, $type, $title, $content, $image, $done, $imageFill,
                    $x, $y, $width, $height, $opacity, $created, $updated)
                ON CONFLICT(id) DO UPDATE SET
                    type = excluded.type,
                    title = excluded.title,
                    content = excluded.content,
                    image = excluded.image,
                    done = excluded.done,
                    image_fill = excluded.image_fill,
                    x = excluded.x,
                    y = excluded.y,
                    w = excluded.w,
                    h = excluded.h,
                    op = excluded.op,
                    updated = excluded.updated
                """;

            command.Parameters.AddWithValue("$id", card.Id.ToString());
            command.Parameters.AddWithValue("$type", (int)card.Type);
            command.Parameters.AddWithValue("$title", card.Title ?? string.Empty);
            command.Parameters.AddWithValue("$content", card.Content ?? string.Empty);
            command.Parameters.AddWithValue("$image", (object?)card.ImagePath ?? DBNull.Value);
            command.Parameters.AddWithValue("$done", card.Completed ? 1 : 0);
            command.Parameters.AddWithValue("$imageFill", card.ImageFill ? 1 : 0);
            command.Parameters.AddWithValue("$x", card.X);
            command.Parameters.AddWithValue("$y", card.Y);
            command.Parameters.AddWithValue("$width", card.Width);
            command.Parameters.AddWithValue("$height", card.Height);
            command.Parameters.AddWithValue("$opacity", card.Opacity);
            command.Parameters.AddWithValue("$created", card.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updated", card.UpdatedUtc.ToString("O"));

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        await writeGate.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM cards WHERE id = $id";
            command.Parameters.AddWithValue("$id", id.ToString());
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            writeGate.Release();
        }
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS cards (
                id TEXT PRIMARY KEY,
                type INTEGER NOT NULL,
                title TEXT NOT NULL DEFAULT '',
                content TEXT NOT NULL DEFAULT '',
                image TEXT NULL,
                done INTEGER NOT NULL DEFAULT 0,
                image_fill INTEGER NOT NULL DEFAULT 0,
                x REAL NOT NULL,
                y REAL NOT NULL,
                w REAL NOT NULL,
                h REAL NOT NULL,
                op REAL NOT NULL DEFAULT 0.96,
                created TEXT NOT NULL,
                updated TEXT NOT NULL
            )
            """;
        command.ExecuteNonQuery();

        using var migration = connection.CreateCommand();
        migration.CommandText = "SELECT COUNT(*) FROM pragma_table_info('cards') WHERE name = 'image_fill'";
        if (Convert.ToInt32(migration.ExecuteScalar()) == 0)
        {
            migration.CommandText = "ALTER TABLE cards ADD COLUMN image_fill INTEGER NOT NULL DEFAULT 0";
            migration.ExecuteNonQuery();
        }
    }

    private static DateTime ParseUtc(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return DateTime.UtcNow;
        }

        return DateTime.TryParse(
            reader.GetString(ordinal),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.UtcNow;
    }
}
