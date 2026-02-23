using IIoTVale.Backend.Core.DTOs.Configuration;
using IIoTVale.Backend.Core.DTOs.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace IIoTVale.Backend.API.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly ConcurrentDictionary<string, byte> _knownTables = [];
        private readonly ILogger<DatabaseService> _logger;
        private const string AlarmConfigTableName = "alarm_configs";

        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> _logger)
        {
            this._connectionString = configuration.GetConnectionString("TimescaleDb") ?? throw new ArgumentNullException();
            this._logger = _logger;

            InitAsync();
        }
        public async void InitAsync()
        {
            await LoadExistingTablesAsync();
            await EnsureAlarmConfigTableExistsAsync();
        }

        public string GetConnectionString() => _connectionString;

        public async Task SaveBatchAsync(List<DataStreamingDto> batchBuffer, CancellationToken stoppingToken)
        {
            var batchSensors = batchBuffer.GroupBy(x => x.SensorMAC);

            foreach (var sensorGroup in batchSensors)
            {
                string tableName = SanitizeTableName(sensorGroup.Key);
                if (string.IsNullOrWhiteSpace(tableName)) continue;
                var count = sensorGroup.Count();
                //_logger.LogInformation("Inserindo batch com {count} informações para o sensor {tableName}", count, tableName);

                try
                {
                    using (NpgsqlConnection connection = new(_connectionString))
                    {
                        await connection.OpenAsync(stoppingToken);

                        if (!_knownTables.ContainsKey(tableName))
                        {
                            await EnsureTableExistsAsync(connection, tableName, stoppingToken);
                        }

                        // Insert by binary copy
                        string copyCommand = $"COPY \"{tableName}\" (time, acc_x, acc_y, acc_z) FROM STDIN (FORMAT BINARY)";

                        using (NpgsqlBinaryImporter writer = await connection.BeginBinaryImportAsync(copyCommand, stoppingToken))
                        {
                            foreach (DataStreamingDto dto in sensorGroup)
                            {
                                foreach (DataModel data in dto.DataModel)
                                {
                                    await writer.StartRowAsync(stoppingToken);

                                    await writer.WriteAsync(data.SampleTime, NpgsqlDbType.TimestampTz, stoppingToken);
                                    await writer.WriteAsync(data.AccX, NpgsqlDbType.Double, stoppingToken);
                                    await writer.WriteAsync(data.AccY, NpgsqlDbType.Double, stoppingToken);
                                    await writer.WriteAsync(data.AccZ, NpgsqlDbType.Double, stoppingToken);
                                }
                            }

                            await writer.CompleteAsync(stoppingToken);
                            _logger.LogInformation("Dados salvos no banco.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ocorreu um erro ao tentar inserir os dados do sensor {tableName} no banco de dados.", tableName);
                }
            }
        }

        private async Task LoadExistingTablesAsync()
        {
            try
            {
                using (NpgsqlConnection connection = new NpgsqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    // Busca nomes de todas as tabelas no schema public
                    string query = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';";

                    using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
                    using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string dbTableName = reader.GetString(0);
                            _knownTables.TryAdd(dbTableName, 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao carregar tabelas iniciais: {ex.Message}");
            }
        }

        private string SanitizeTableName(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            StringBuilder sb = new();
            foreach (char c in input)
            {
                // Permite apenas letras, números e underscore para evitar SQL Injection ou erros de sintaxe
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
            }

            // Adiciona um prefixo para garantir que não comece com número (o que o SQL não gosta muito)
            return "sensor_" + sb.ToString();
        }
        private async Task EnsureTableExistsAsync(NpgsqlConnection connection, string tableName, CancellationToken stoppingToken)
        {
            // Cria a tabela com as colunas exatas do DataModel
            string createTableSql = $@"
            CREATE TABLE IF NOT EXISTS ""{tableName}"" (
                time TIMESTAMPTZ NOT NULL,
                acc_x DOUBLE PRECISION NULL,
                acc_y DOUBLE PRECISION NULL,
                acc_z DOUBLE PRECISION NULL
            );";

            using (NpgsqlCommand command = new NpgsqlCommand(createTableSql, connection))
            {
                await command.ExecuteNonQueryAsync(stoppingToken);
            }

            // Transforma em Hypertable (TimescaleDB) particionada pelo tempo
            string createHypertableSql = $"SELECT create_hypertable('\"{tableName}\"', 'time', if_not_exists => TRUE, migrate_data => TRUE);";

            using (NpgsqlCommand command = new NpgsqlCommand(createHypertableSql, connection))
            {
                await command.ExecuteNonQueryAsync(stoppingToken);
            }

            // Adiciona ao cache local
            _knownTables.TryAdd(tableName, 0);
        }

        private async Task EnsureAlarmConfigTableExistsAsync()
        {
            try
            {
                using (NpgsqlConnection connection = new NpgsqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string createTableSql = $@"
                    CREATE TABLE IF NOT EXISTS ""{AlarmConfigTableName}"" (
                        mac_address TEXT PRIMARY KEY,
                        config JSONB NOT NULL,
                        created_at TIMESTAMPTZ DEFAULT NOW(),
                        updated_at TIMESTAMPTZ DEFAULT NOW()
                    );";

                    using (NpgsqlCommand command = new NpgsqlCommand(createTableSql, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }

                    _knownTables.TryAdd(AlarmConfigTableName, 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar tabela de configurações de alarmes.");
            }
        }

        public async Task<bool> SaveAlarmConfigAsync(string macAddress, GlobalAlarmConfig alarmConfig, CancellationToken cancellationToken = default)
        {
            try
            {
                using (NpgsqlConnection connection = new NpgsqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);

                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    };

                    string configJson = JsonSerializer.Serialize(alarmConfig, jsonOptions);

                    string upsertSql = $@"
                    INSERT INTO ""{AlarmConfigTableName}"" (mac_address, config, updated_at)
                    VALUES (@macAddress, @config::jsonb, NOW())
                    ON CONFLICT (mac_address)
                    DO UPDATE SET config = @config::jsonb, updated_at = NOW();";

                    using (NpgsqlCommand command = new NpgsqlCommand(upsertSql, connection))
                    {
                        command.Parameters.AddWithValue("@macAddress", macAddress);
                        command.Parameters.AddWithValue("@config", configJson);

                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    _logger.LogInformation("Configuração de alarme salva para o sensor {MacAddress}", macAddress);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao salvar configuração de alarme para o sensor {MacAddress}", macAddress);
                return false;
            }
        }

        public async Task<GlobalAlarmConfig?> GetAlarmConfigAsync(string macAddress, CancellationToken cancellationToken = default)
        {
            try
            {
                using (NpgsqlConnection connection = new NpgsqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);

                    string selectSql = $@"
                    SELECT config FROM ""{AlarmConfigTableName}""
                    WHERE mac_address = @macAddress;";

                    using (NpgsqlCommand command = new NpgsqlCommand(selectSql, connection))
                    {
                        command.Parameters.AddWithValue("@macAddress", macAddress);

                        using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                string configJson = reader.GetString(0);

                                var jsonOptions = new JsonSerializerOptions
                                {
                                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                    PropertyNameCaseInsensitive = true
                                };

                                return JsonSerializer.Deserialize<GlobalAlarmConfig>(configJson, jsonOptions);
                            }
                        }
                    }

                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar configuração de alarme para o sensor {MacAddress}", macAddress);
                return null;
            }
        }
    }
}
