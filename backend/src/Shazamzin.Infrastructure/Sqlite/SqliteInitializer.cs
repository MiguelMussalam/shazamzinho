using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Shazamzin.Infrastructure.Sqlite;

/// <summary>Cria o arquivo SQLite e aplica o schema.sql embutido. Idempotente (CREATE ... IF NOT EXISTS).</summary>
public static class SqliteInitializer
{
    public static void EnsureCreated(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("schema.sql")
            ?? throw new InvalidOperationException("Resource schema.sql não encontrado no assembly.");
        using var reader = new StreamReader(stream);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Se o Data Source for relativo, ancora na pasta que contém o Shazamzin.slnx (subindo a partir de
    /// <paramref name="startDirectory"/>), para o banco ficar sempre em backend/data/ independente do
    /// diretório de onde a API foi iniciada. Sem .slnx (app publicado), mantém relativo ao diretório atual.
    /// </summary>
    public static string ResolveConnectionString(string connectionString, string startDirectory)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || Path.IsPathRooted(builder.DataSource))
            return connectionString;

        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            if (dir.GetFiles("Shazamzin.slnx").Length > 0)
            {
                builder.DataSource = Path.GetFullPath(Path.Combine(dir.FullName, builder.DataSource));
                return builder.ToString();
            }
        }
        return connectionString;
    }
}
