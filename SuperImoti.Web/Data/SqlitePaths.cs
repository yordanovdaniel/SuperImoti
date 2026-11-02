using Microsoft.Data.Sqlite;

namespace SuperImoti.Web.Data;

public static class SqlitePaths
{
    /// <summary>
    /// Makes a relative "Data Source=App_Data/x.db" point inside the project folder (not the current
    /// working directory) and creates the folder if it is missing.
    /// </summary>
    public static string ResolveAgainst(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:" || builder.Mode == SqliteOpenMode.Memory)
        {
            return connectionString;
        }

        if (!Path.IsPathRooted(dataSource))
        {
            dataSource = Path.GetFullPath(Path.Combine(contentRootPath, dataSource));
            builder.DataSource = dataSource;
        }

        var directory = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return builder.ToString();
    }
}
