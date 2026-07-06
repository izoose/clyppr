using Microsoft.Data.Sqlite;

namespace Clipper.Core;

/// <summary>SQLite-backed clip library. Simple CRUD + search over the clips table.</summary>
public sealed class ClipLibrary
{
    private readonly string _cs;

    public ClipLibrary(string? dbPath = null)
    {
        _cs = new SqliteConnectionStringBuilder { DataSource = dbPath ?? AppPaths.DbPath }.ToString();
        Init();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    private void Init()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS clips (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath      TEXT NOT NULL,
                Title         TEXT NOT NULL,
                Game          TEXT,
                CreatedAt     TEXT NOT NULL,
                DurationMs    INTEGER NOT NULL,
                SizeBytes     INTEGER NOT NULL,
                Width         INTEGER NOT NULL,
                Height        INTEGER NOT NULL,
                ThumbnailPath TEXT,
                ShareUrl      TEXT,
                Tracks        TEXT NOT NULL DEFAULT '',
                Tags          TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public long Add(Clip clip)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO clips (FilePath,Title,Game,CreatedAt,DurationMs,SizeBytes,Width,Height,ThumbnailPath,ShareUrl,Tracks,Tags)
            VALUES ($f,$t,$g,$c,$d,$s,$w,$h,$th,$u,$tr,$tg);
            SELECT last_insert_rowid();
            """;
        Bind(cmd, clip);
        long id = (long)cmd.ExecuteScalar()!;
        clip.Id = id;
        return id;
    }

    public List<Clip> GetAll()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM clips ORDER BY datetime(CreatedAt) DESC;";
        return ReadAll(cmd);
    }

    public List<Clip> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM clips WHERE Title LIKE $q OR Game LIKE $q OR Tags LIKE $q ORDER BY datetime(CreatedAt) DESC;";
        cmd.Parameters.AddWithValue("$q", $"%{query}%");
        return ReadAll(cmd);
    }

    public void Update(Clip clip)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE clips SET FilePath=$f,Title=$t,Game=$g,CreatedAt=$c,DurationMs=$d,SizeBytes=$s,
                Width=$w,Height=$h,ThumbnailPath=$th,ShareUrl=$u,Tracks=$tr,Tags=$tg WHERE Id=$id;
            """;
        Bind(cmd, clip);
        cmd.Parameters.AddWithValue("$id", clip.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM clips WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand cmd, Clip clip)
    {
        cmd.Parameters.AddWithValue("$f", clip.FilePath);
        cmd.Parameters.AddWithValue("$t", clip.Title);
        cmd.Parameters.AddWithValue("$g", (object?)clip.Game ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", clip.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$d", clip.DurationMs);
        cmd.Parameters.AddWithValue("$s", clip.SizeBytes);
        cmd.Parameters.AddWithValue("$w", clip.Width);
        cmd.Parameters.AddWithValue("$h", clip.Height);
        cmd.Parameters.AddWithValue("$th", (object?)clip.ThumbnailPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", (object?)clip.ShareUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tr", clip.Tracks);
        cmd.Parameters.AddWithValue("$tg", clip.Tags);
    }

    private static List<Clip> ReadAll(SqliteCommand cmd)
    {
        var list = new List<Clip>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Clip
            {
                Id = r.GetInt64(r.GetOrdinal("Id")),
                FilePath = r.GetString(r.GetOrdinal("FilePath")),
                Title = r.GetString(r.GetOrdinal("Title")),
                Game = r["Game"] as string,
                CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind),
                DurationMs = r.GetInt64(r.GetOrdinal("DurationMs")),
                SizeBytes = r.GetInt64(r.GetOrdinal("SizeBytes")),
                Width = r.GetInt32(r.GetOrdinal("Width")),
                Height = r.GetInt32(r.GetOrdinal("Height")),
                ThumbnailPath = r["ThumbnailPath"] as string,
                ShareUrl = r["ShareUrl"] as string,
                Tracks = r.GetString(r.GetOrdinal("Tracks")),
                Tags = r.GetString(r.GetOrdinal("Tags")),
            });
        }
        return list;
    }
}
