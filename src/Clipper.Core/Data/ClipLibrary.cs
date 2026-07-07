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
                AlbumId       INTEGER,
                IsFavorite    INTEGER NOT NULL DEFAULT 0,
                Tracks        TEXT NOT NULL DEFAULT '',
                Tags          TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS albums (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        // Migrate older DBs that predate newer columns.
        foreach (var col in new[] { "AlbumId INTEGER", "IsFavorite INTEGER NOT NULL DEFAULT 0" })
        {
            try
            {
                using var alter = c.CreateCommand();
                alter.CommandText = $"ALTER TABLE clips ADD COLUMN {col};";
                alter.ExecuteNonQuery();
            }
            catch { /* column already exists */ }
        }
    }

    public void SetFavorite(long clipId, bool favorite)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE clips SET IsFavorite=$f WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$f", favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", clipId);
        cmd.ExecuteNonQuery();
    }

    // ---- albums ----

    public List<Album> GetAlbums()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, CreatedAt FROM albums ORDER BY Name COLLATE NOCASE;";
        var list = new List<Album>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Album { Id = r.GetInt64(0), Name = r.GetString(1), CreatedAt = DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind) });
        return list;
    }

    public Album AddAlbum(string name)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO albums (Name, CreatedAt) VALUES ($n, $c); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString("o"));
        long id = (long)cmd.ExecuteScalar()!;
        return new Album { Id = id, Name = name };
    }

    public void RenameAlbum(long id, string name)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE albums SET Name=$n WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteAlbum(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE clips SET AlbumId=NULL WHERE AlbumId=$id; DELETE FROM albums WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetClipAlbum(long clipId, long? albumId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE clips SET AlbumId=$a WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$a", (object?)albumId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", clipId);
        cmd.ExecuteNonQuery();
    }

    public long Add(Clip clip)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO clips (FilePath,Title,Game,CreatedAt,DurationMs,SizeBytes,Width,Height,ThumbnailPath,ShareUrl,AlbumId,IsFavorite,Tracks,Tags)
            VALUES ($f,$t,$g,$c,$d,$s,$w,$h,$th,$u,$al,$fav,$tr,$tg);
            SELECT last_insert_rowid();
            """;
        Bind(cmd, clip);
        long id = (long)cmd.ExecuteScalar()!;
        clip.Id = id;
        return id;
    }

    public List<Clip> GetAll(long? albumId = null)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = albumId is null
            ? "SELECT * FROM clips ORDER BY datetime(CreatedAt) DESC;"
            : "SELECT * FROM clips WHERE AlbumId=$a ORDER BY datetime(CreatedAt) DESC;";
        if (albumId is not null) cmd.Parameters.AddWithValue("$a", albumId);
        return ReadAll(cmd);
    }

    public List<Clip> Search(string query, long? albumId = null)
        => Query(query, albumId, false);

    /// <summary>Combined filter: text search + album + favorites-only.</summary>
    public List<Clip> Query(string? search, long? albumId, bool favoritesOnly)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) where.Add("(Title LIKE $q OR Game LIKE $q OR Tags LIKE $q)");
        if (albumId is not null) where.Add("AlbumId=$a");
        if (favoritesOnly) where.Add("IsFavorite=1");

        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM clips"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY datetime(CreatedAt) DESC;";
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("$q", $"%{search}%");
        if (albumId is not null) cmd.Parameters.AddWithValue("$a", albumId);
        return ReadAll(cmd);
    }

    public void Update(Clip clip)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE clips SET FilePath=$f,Title=$t,Game=$g,CreatedAt=$c,DurationMs=$d,SizeBytes=$s,
                Width=$w,Height=$h,ThumbnailPath=$th,ShareUrl=$u,AlbumId=$al,IsFavorite=$fav,Tracks=$tr,Tags=$tg WHERE Id=$id;
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
        cmd.Parameters.AddWithValue("$al", (object?)clip.AlbumId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fav", clip.IsFavorite ? 1 : 0);
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
                AlbumId = r["AlbumId"] is long al ? al : null,
                IsFavorite = r["IsFavorite"] is long fav && fav != 0,
                Tracks = r.GetString(r.GetOrdinal("Tracks")),
                Tags = r.GetString(r.GetOrdinal("Tags")),
            });
        }
        return list;
    }
}
