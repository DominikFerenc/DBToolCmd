using FirebirdSql.Data.FirebirdClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DbMetaTool
{
    public class FirebirdMetadataExtractor
    {
        private readonly string _connectionString;

        public FirebirdMetadataExtractor(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void ExportProcedures(string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            string queryProcs = "SELECT RDB$PROCEDURE_NAME FROM RDB$PROCEDURES WHERE RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL";

            using var connection = new FbConnection(_connectionString);
            connection.Open();

            var procNames = new List<string>();
            using (var command = new FbCommand(queryProcs, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read()) procNames.Add(reader.GetString(0).Trim());
            }

            foreach (var name in procNames)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"CREATE OR ALTER PROCEDURE {name}");

                string inputs = GetParameters(connection, name, 0);
                if (!string.IsNullOrWhiteSpace(inputs))
                {
                    sb.AppendLine($"({inputs})");
                }


                string outputs = GetParameters(connection, name, 1);
                if (!string.IsNullOrWhiteSpace(outputs))
                {
                    sb.AppendLine($"RETURNS ({outputs})");
                }

                // 5. Pobieramy ciało procedury (BEGIN...END)
                string bodyQuery = "SELECT RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @procName";
                string body = "BEGIN END";
                using (var cmdBody = new FbCommand(bodyQuery, connection))
                {
                    cmdBody.Parameters.AddWithValue("@procName", name);
                    var result = cmdBody.ExecuteScalar();


                    if (result != null && result != DBNull.Value)
                    {
                        body = result.ToString() ?? "BEGIN END";
                    }
                }

                sb.AppendLine("AS");
                sb.AppendLine(body);

                string content = sb.ToString();
                string fileName = Path.Combine(outputDir, $"PROC_{name}.sql");
                File.WriteAllText(fileName, content);
            }
            Console.WriteLine($"Wyeksportowano procedury do: {outputDir}");
        }

        private string GetParameters(FbConnection connection, string procName, int paramType)
        {
            string query = @"
                SELECT 
                    P.RDB$PARAMETER_NAME,
                    F.RDB$FIELD_TYPE,
                    F.RDB$FIELD_LENGTH,
                    F.RDB$FIELD_PRECISION,
                    F.RDB$FIELD_SCALE
                FROM RDB$PROCEDURE_PARAMETERS P
                JOIN RDB$FIELDS F ON P.RDB$FIELD_SOURCE = F.RDB$FIELD_NAME
                WHERE P.RDB$PROCEDURE_NAME = @procName AND P.RDB$PARAMETER_TYPE = @paramType
                ORDER BY P.RDB$PARAMETER_NUMBER";

            using var command = new FbCommand(query, connection);
            command.Parameters.AddWithValue("@procName", procName);
            command.Parameters.AddWithValue("@paramType", paramType);

            var paramList = new List<string>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string paramName = reader.GetString(0).Trim();
                    short typeId = reader.GetInt16(1);
                    short length = reader.GetInt16(2);
                    short precision = reader.IsDBNull(3) ? (short)0 : reader.GetInt16(3);
                    short scale = reader.IsDBNull(4) ? (short)0 : reader.GetInt16(4);

                    string typeName = GetSqlTypeFromId(typeId, length, precision, scale);
                    paramList.Add($"{paramName} {typeName}");
                }
            }
            return string.Join(", ", paramList);
        }

        public void ExportDomains(string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            string query = @"
                SELECT RDB$FIELD_NAME, RDB$FIELD_TYPE, RDB$FIELD_LENGTH, RDB$FIELD_PRECISION, RDB$FIELD_SCALE
                FROM RDB$FIELDS
                WHERE (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL) 
                  AND RDB$COMPUTED_SOURCE IS NULL 
                  AND RDB$FIELD_NAME NOT STARTING WITH 'RDB$'";

            using var connection = new FbConnection(_connectionString);
            connection.Open();
            using var command = new FbCommand(query, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                string name = reader.GetString(0).Trim();
                short typeId = reader.GetInt16(1);
                short length = reader.GetInt16(2);
                short precision = reader.IsDBNull(3) ? (short)0 : reader.GetInt16(3);
                short scale = reader.IsDBNull(4) ? (short)0 : reader.GetInt16(4);

                string sqlTypeName = GetSqlTypeFromId(typeId, length, precision, scale);
                string createSql = $"CREATE DOMAIN {name} AS {sqlTypeName};";

                string fileName = Path.Combine(outputDir, $"DOMAIN_{name}.sql");
                File.WriteAllText(fileName, createSql);
            }
            Console.WriteLine($"Wyeksportowano domeny do: {outputDir}");
        }

        public void ExportTables(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var tables = new List<string>();
            string queryTables = "SELECT RDB$RELATION_NAME FROM RDB$RELATIONS WHERE (RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL) AND RDB$VIEW_SOURCE IS NULL";

            using var connection = new FbConnection(_connectionString);
            connection.Open();
            using (var command = new FbCommand(queryTables, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read()) tables.Add(reader.GetString(0).Trim());
            }

            foreach (var tableName in tables)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"CREATE TABLE {tableName} (");

                string queryFields = @"
                    SELECT 
                        RF.RDB$FIELD_NAME, -- Nazwa pola
                        F.RDB$FIELD_TYPE,  -- ID typu
                        F.RDB$FIELD_LENGTH, -- Długość
                        F.RDB$FIELD_PRECISION, -- Precyzja
                        F.RDB$FIELD_SCALE  -- Skala
                    FROM RDB$RELATION_FIELDS RF
                    JOIN RDB$FIELDS F ON RF.RDB$FIELD_SOURCE = F.RDB$FIELD_NAME
                    WHERE RF.RDB$RELATION_NAME = @tableName
                    ORDER BY RF.RDB$FIELD_POSITION";

                using var cmdFields = new FbCommand(queryFields, connection);
                cmdFields.Parameters.AddWithValue("@tableName", tableName);

                var fieldsList = new List<string>();
                using (var reader = cmdFields.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string fieldName = reader.GetString(0).Trim();
                        short typeId = reader.GetInt16(1);
                        short length = reader.GetInt16(2);
                        short precision = reader.IsDBNull(3) ? (short)0 : reader.GetInt16(3);
                        short scale = reader.IsDBNull(4) ? (short)0 : reader.GetInt16(4);
                        string sqlTypeName = GetSqlTypeFromId(typeId, length, precision, scale);

                        fieldsList.Add($"  {fieldName} {sqlTypeName}");
                    }
                }

                sb.AppendLine(string.Join("," + Environment.NewLine, fieldsList));
                sb.AppendLine(");");

                string fileName = Path.Combine(outputDir, $"TABLE_{tableName}.sql");
                File.WriteAllText(fileName, sb.ToString());
            }
            Console.WriteLine($"Wyeksportowano tabele do: {outputDir}");
        }

        private string GetSqlTypeFromId(short typeId, short length, short precision, short scale)
        {
            // Na podstawie: https://firebirdsql.org/file/documentation/reference_manuals/fblangref25-en/html/fblangref25-dtypes-tbl.html
            switch (typeId)
            {
                case 7: return $"SMALLINT";
                case 8: return $"INTEGER";
                case 10: return $"FLOAT";
                case 12: return $"DATE";
                case 13: return $"TIME";
                case 14: return $"CHAR({length})";
                case 16:
                    if (scale == 0) return precision == 0 ? "BIGINT" : $"NUMERIC({precision}, 0)";
                    return $"NUMERIC({precision}, {Math.Abs(scale)})";
                case 27: return $"DOUBLE PRECISION";
                case 35: return $"TIMESTAMP";
                case 37: return $"VARCHAR({length})";
                case 261: return $"BLOB";
                default: return $"UNKNOWN_TYPE_ID_{typeId}";
            }
        }
    }
}