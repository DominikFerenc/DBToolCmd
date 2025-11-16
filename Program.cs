using FirebirdSql.Data.FirebirdClient;


namespace DbMetaTool
{
    public static class Program
    {
        // Przykładowe wywołania:
        // DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\scripts"
        // DbMetaTool export-scripts --connection-string "..." --output-dir "C:\out"
        // DbMetaTool update-db --connection-string "..." --scripts-dir "C:\scripts"
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Użycie:");
                Console.WriteLine("  build-db --db-dir <ścieżka> --scripts-dir <ścieżka>");
                Console.WriteLine("  export-scripts --connection-string <connStr> --output-dir <ścieżka>");
                Console.WriteLine("  update-db --connection-string <connStr> --scripts-dir <ścieżka>");
                return 1;
            }

            try
            {
                var command = args[0].ToLowerInvariant();

                switch (command)
                {
                    case "build-db":
                        {
                            string dbDir = GetArgValue(args, "--db-dir");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            BuildDatabase(dbDir, scriptsDir);
                            Console.WriteLine("Baza danych została zbudowana pomyślnie.");
                            return 0;
                        }

                    case "export-scripts":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string outputDir = GetArgValue(args, "--output-dir");

                            ExportScripts(connStr, outputDir);
                            Console.WriteLine("Skrypty zostały wyeksportowane pomyślnie.");
                            return 0;
                        }

                    case "update-db":
                        {
                            string connStr = GetArgValue(args, "--connection-string");
                            string scriptsDir = GetArgValue(args, "--scripts-dir");

                            UpdateDatabase(connStr, scriptsDir);
                            Console.WriteLine("Baza danych została zaktualizowana pomyślnie.");
                            return 0;
                        }

                    default:
                        Console.WriteLine($"Nieznane polecenie: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Błąd: " + ex.Message);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("Szczegóły: " + ex.InnerException.Message);
                }
                return -1;
            }
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx == -1 || idx + 1 >= args.Length)
                throw new ArgumentException($"Brak wymaganego parametru {name}");
            return args[idx + 1];
        }


        /// <summary>
        /// Buduje nową bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void BuildDatabase(string databaseDirectory, string scriptsDirectory)
        {

            string dbName = $"NewDb_{DateTime.Now:yyyyMMdd_HHmmss}.fdb";
            string dbPath = Path.Combine(databaseDirectory, dbName);
            var csBuilder = new FbConnectionStringBuilder
            {
                Database = dbPath,
                UserID = "SYSDBA",
                Password = "podajhaslo",
                DataSource = "localhost",
                Port = 3050,
                Dialect = 3,
                Charset = "UTF8",
                ServerType = FbServerType.Default
            };
            string connectionString = csBuilder.ConnectionString;

            try
            {
                Console.WriteLine($"Tworzenie nowej bazy danych w: {dbPath}");
                FbConnection.CreateDatabase(connectionString, pageSize: 8192, forcedWrites: true, overwrite: false);
                Console.WriteLine("Baza danych utworzona.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Nie udało się utworzyć bazy danych (sprawdź czy serwer działa i hasło SYSDBA jest poprawnie): {ex.Message}", ex);
            }
            try
            {
                Console.WriteLine("Wykonywanie skryptów na nowej bazie...");
                FirebirdScriptRunner.ExecuteScriptsFromDir(connectionString, scriptsDirectory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas wypełniania nowej bazy danymi: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Generuje skrypty metadanych z istniejącej bazy danych Firebird 5.0.
        /// </summary>
        public static void ExportScripts(string connectionString, string outputDirectory)
        {
            Console.WriteLine("Rozpoczynanie eksportu metadanych...");
            var extractor = new FirebirdMetadataExtractor(connectionString);
            string domainsDir = Path.Combine(outputDirectory, "1_domains");
            string tablesDir = Path.Combine(outputDirectory, "2_tables");
            string procsDir = Path.Combine(outputDirectory, "3_procedures");
            extractor.ExportDomains(domainsDir);
            extractor.ExportTables(tablesDir);
            extractor.ExportProcedures(procsDir);

            Console.WriteLine($"Eksport zakończony. Skrypty zapisano w: {outputDirectory}");
        }

        /// <summary>
        /// Aktualizuje istniejącą bazę danych Firebird 5.0 na podstawie skryptów.
        /// </summary>
        public static void UpdateDatabase(string connectionString, string scriptsDirectory)
        {
            Console.WriteLine("Rozpoczynanie aktualizacji bazy danych...");

            FirebirdScriptRunner.ExecuteScriptsFromDir(connectionString, scriptsDirectory);

            Console.WriteLine("Aktualizacja bazy danych zakończona.");
        }
    }
}