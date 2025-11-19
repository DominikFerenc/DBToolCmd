using FirebirdSql.Data.FirebirdClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DbMetaTool
{
    /// <summary>
    /// Klasa odpowiedzialna za inteligentną aktualizację schematu bazy danych.
    /// Obsługuje Dry Run oraz usuwanie nadmiarowych kolumn.
    /// </summary>
    public class FirebirdSchemaUpdater
    {
        private readonly string _connectionString;
        private readonly string _scriptsDirectory;
        private readonly bool _dryRun;

        public FirebirdSchemaUpdater(string connectionString, string scriptsDirectory, bool dryRun)
        {
            _connectionString = connectionString;
            _scriptsDirectory = scriptsDirectory;
            _dryRun = dryRun;
        }

        public void UpdateSchema()
        {
            using var connection = new FbConnection(_connectionString);
            try
            {
                connection.Open();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Nie można połączyć się z bazą: {_connectionString}. Błąd: {ex.Message}", ex);
            }

            if (_dryRun)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("!!! TRYB DRY RUN AKTYWNY - ŻADNE ZMIANY NIE ZOSTANĄ ZAPISANE !!!");
                Console.ResetColor();
            }

            Console.WriteLine("--- Rozpoczynanie inteligentnej aktualizacji schematu ---");

            UpdateDomains(connection);
            UpdateTables(connection);
            UpdateProceduresWithRetry(connection);

            Console.WriteLine("--- Aktualizacja schematu zakończona ---");
        }

        private void UpdateDomains(FbConnection connection)
        {
            string domainsDir = Path.Combine(_scriptsDirectory, "1_domains");
            if (!Directory.Exists(domainsDir)) return;

            Console.WriteLine(">> Weryfikacja domen...");

            var dbDomains = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            string query = @"
                SELECT RDB$FIELD_NAME, RDB$FIELD_LENGTH, RDB$FIELD_TYPE 
                FROM RDB$FIELDS 
                WHERE RDB$FIELD_NAME NOT STARTING WITH 'RDB$' 
                  AND RDB$COMPUTED_SOURCE IS NULL";

            using (var cmd = new FbCommand(query, connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name = reader.GetString(0).Trim();
                    short len = reader.GetInt16(1);
                    short type = reader.GetInt16(2);

                    if (type == 37 || type == 14) dbDomains[name] = len;
                    else dbDomains[name] = -1;
                }
            }

            foreach (var file in Directory.GetFiles(domainsDir, "*.sql"))
            {
                string script = File.ReadAllText(file);
                var matchName = Regex.Match(script, @"CREATE\s+(?:OR\s+ALTER\s+)?DOMAIN\s+([A-Z0-9_$]+)", RegexOptions.IgnoreCase);
                if (!matchName.Success) continue;

                string domainName = matchName.Groups[1].Value.Trim();

                if (!dbDomains.ContainsKey(domainName))
                {
                    Console.WriteLine($"  [+] Dodawanie nowej domeny: {domainName}");
                    ExecuteSql(connection, script);
                }
                else
                {
                    var matchType = Regex.Match(script, @"AS\s+(VARCHAR|CHAR)\s*\(\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
                    if (matchType.Success)
                    {
                        string typeName = matchType.Groups[1].Value.ToUpper();
                        int scriptLength = int.Parse(matchType.Groups[2].Value);
                        int dbLength = dbDomains[domainName];

                        if (dbLength > 0 && scriptLength > dbLength)
                        {
                            Console.WriteLine($"  [^] Rozszerzanie domeny {domainName}: {dbLength} -> {scriptLength}");
                            string alterSql = $"ALTER DOMAIN {domainName} TYPE {typeName}({scriptLength})";
                            ExecuteSql(connection, alterSql);
                        }
                    }
                }
            }
        }

        private void UpdateTables(FbConnection connection)
        {
            string tablesDir = Path.Combine(_scriptsDirectory, "2_tables");
            if (!Directory.Exists(tablesDir)) return;

            Console.WriteLine(">> Weryfikacja tabel...");

            var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string queryTables = "SELECT RDB$RELATION_NAME FROM RDB$RELATIONS WHERE (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL) AND RDB$VIEW_SOURCE IS NULL";
            using (var cmd = new FbCommand(queryTables, connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read()) existingTables.Add(reader.GetString(0).Trim());
            }

            foreach (var file in Directory.GetFiles(tablesDir, "*.sql"))
            {
                string script = File.ReadAllText(file);
                var matchTable = Regex.Match(script, @"CREATE\s+TABLE\s+([A-Z0-9_$]+)", RegexOptions.IgnoreCase);

                if (!matchTable.Success) continue;

                string tableName = matchTable.Groups[1].Value.Trim();

                if (!existingTables.Contains(tableName))
                {
                    Console.WriteLine($"  [+] Tworzenie nowej tabeli: {tableName}");
                    ExecuteSql(connection, script);
                }
                else
                {
                    UpdateTableColumns(connection, tableName, script);
                }
            }
        }

        private void UpdateTableColumns(FbConnection connection, string tableName, string scriptContent)
        {
            var dbColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string queryCols = "SELECT RDB$FIELD_NAME FROM RDB$RELATION_FIELDS WHERE RDB$RELATION_NAME = @tableName";
            using (var cmd = new FbCommand(queryCols, connection))
            {
                cmd.Parameters.AddWithValue("@tableName", tableName);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) dbColumns.Add(reader.GetString(0).Trim());
                }
            }

            var scriptLines = ExtractBodyContent(scriptContent);
            var scriptColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in scriptLines)
            {
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string colName = parts[0].Trim();
                    if (colName.ToUpper() == "CONSTRAINT" || colName.ToUpper() == "PRIMARY") continue;

                    scriptColumnNames.Add(colName);

                    if (!dbColumns.Contains(colName))
                    {
                        string columnDef = line.Trim().Substring(colName.Length).Trim().TrimEnd(',');
                        Console.WriteLine($"  [+] Dodawanie pola {colName} do tabeli {tableName}");
                        string alterSql = $"ALTER TABLE {tableName} ADD {colName} {columnDef}";
                        ExecuteSql(connection, alterSql);
                    }
                }
            }

            foreach (var dbCol in dbColumns)
            {
                if (!scriptColumnNames.Contains(dbCol))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [-] Usuwanie pola {dbCol} z tabeli {tableName} (brak w skrypcie)");
                    Console.ResetColor();

                    string dropSql = $"ALTER TABLE {tableName} DROP {dbCol}";
                    ExecuteSql(connection, dropSql);
                }
            }
        }

        private void UpdateProceduresWithRetry(FbConnection connection)
        {
            string procsDir = Path.Combine(_scriptsDirectory, "3_procedures");
            if (!Directory.Exists(procsDir)) return;

            Console.WriteLine(">> Aktualizacja procedur (z rozwiązywaniem zależności)...");

            var pendingScripts = Directory.GetFiles(procsDir, "*.sql").ToList();
            int maxRetries = 5;
            bool madeProgress = true;

            for (int pass = 1; pass <= maxRetries && pendingScripts.Count > 0 && madeProgress; pass++)
            {
                madeProgress = false;
                var failedInThisPass = new List<string>();

                Console.WriteLine($"  Przebieg {pass}/{maxRetries}, pozostało: {pendingScripts.Count}");

                foreach (var file in pendingScripts)
                {
                    string script = File.ReadAllText(file);
                    try
                    {
                        ExecuteSql(connection, script);
                        madeProgress = true;
                    }
                    catch (Exception)
                    {
                        failedInThisPass.Add(file);
                    }
                }

                pendingScripts = failedInThisPass;
            }

            if (pendingScripts.Count > 0)
            {
                Console.WriteLine("!! OSTRZEŻENIE: Nie udało się zaktualizować niektórych procedur:");
                foreach (var f in pendingScripts) Console.WriteLine($"   - {Path.GetFileName(f)}");
            }
            else
            {
                Console.WriteLine("  Wszystkie procedury zaktualizowane pomyślnie.");
            }
        }

        private void ExecuteSql(FbConnection connection, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return;

            if (_dryRun)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"    [DRY RUN] Wykonano by SQL: {sql.Replace(Environment.NewLine, " ").Substring(0, Math.Min(sql.Length, 80))}...");
                Console.ResetColor();
                return;
            }

            using var trans = connection.BeginTransaction();
            using var cmd = new FbCommand(sql, connection, trans);
            try
            {
                cmd.ExecuteNonQuery();
                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        private List<string> ExtractBodyContent(string script)
        {
            var result = new List<string>();
            int start = script.IndexOf('(');
            int end = script.LastIndexOf(')');

            if (start != -1 && end != -1 && end > start)
            {
                string body = script.Substring(start + 1, end - start - 1);
                var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line)) result.Add(line.Trim());
                }
            }
            return result;
        }
    }
}