using FirebirdSql.Data.FirebirdClient;
using System;
using System.IO;
using System.Linq;

namespace DbMetaTool
{
    public static class FirebirdScriptRunner
    {
        public static void ExecuteScriptsFromDir(string connectionString, string scriptsDirectory)
        {
            var directories = new[]
            {
                Path.Combine(scriptsDirectory, "1_domains"),
                Path.Combine(scriptsDirectory, "2_tables"),
                Path.Combine(scriptsDirectory, "3_procedures")
            };

            using var connection = new FbConnection(connectionString);
            try
            {
                connection.Open();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Nie można połączyć się z bazą: {connectionString}. Błąd: {ex.Message}", ex);
            }

            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    Console.WriteLine($"Ostrzeżenie: Katalog nie istnieje, pominieto: {dir}");
                    continue;
                }

                var files = Directory.GetFiles(dir, "*.sql").OrderBy(f => f).ToList();
                Console.WriteLine($"Wykonywanie {files.Count} skryptów z {dir}...");

                foreach (var file in files)
                {
                    try
                    {
                        string scriptContent = File.ReadAllText(file);
                        if (string.IsNullOrWhiteSpace(scriptContent))
                        {
                            Console.WriteLine($"  Pusty plik: {Path.GetFileName(file)}");
                            continue;
                        }

                        using var transaction = connection.BeginTransaction();

                        using var command = new FbCommand(scriptContent, connection, transaction);
                        command.ExecuteNonQuery();

                        transaction.Commit();
                        Console.WriteLine($"  Pomyślnie wykonano: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"BŁĄD podczas wykonywania skryptu {file}: {ex.Message}");
                    }
                }
            }
        }
    }
}