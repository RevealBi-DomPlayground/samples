using Microsoft.Extensions.Configuration;
using Reveal.Sdk;
using Reveal.Sdk.Data;
using Reveal.Sdk.Data.Microsoft.SqlServer;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevealSdk.Server.Reveal
{
    internal class DataSourceProvider : IRVDataSourceProvider
    { 
        private readonly IConfiguration _configuration;
        
        // Cache the tables to avoid repeated file reads
        private static List<TableInfo>? _cachedTables;
        private static readonly object _cacheLock = new();
        private static DateTime _lastCacheRefresh = DateTime.MinValue;
        private static readonly TimeSpan _cacheTimeout = TimeSpan.FromMinutes(5);
        
        public DataSourceProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<RVDataSourceItem> ChangeDataSourceItemAsync(IRVUserContext userContext, 
                string dashboardId, RVDataSourceItem dataSourceItem)
        {
            if (dataSourceItem is not RVSqlServerDataSourceItem sqlDsi)
                return Task.FromResult(dataSourceItem);

            // Ensure data source is updated
            ChangeDataSourceAsync(userContext, sqlDsi.DataSource);
            
            // Check if this is a query-type item from the JSON
            var queryInfo = GetQueryInfoFromJson(sqlDsi.Id);
            
            if (queryInfo != null && !string.IsNullOrEmpty(queryInfo.Query))
            {
                // Set custom query and clear the Table property to prevent SQL errors
                sqlDsi.CustomQuery = queryInfo.Query;
                sqlDsi.Table = null;
                return Task.FromResult((RVDataSourceItem)sqlDsi);
            }

            return Task.FromResult(dataSourceItem);
        }

        public Task<RVDashboardDataSource> ChangeDataSourceAsync(IRVUserContext userContext, RVDashboardDataSource dataSource)
        {
            if (dataSource is RVSqlServerDataSource sqlDs)
            {
                // Get database settings from configuration
                var host = _configuration["DatabaseSettings:Host"];
                var database = _configuration["DatabaseSettings:Database"];
                
                // Apply settings if they exist in configuration
                if (!string.IsNullOrEmpty(host))
                    sqlDs.Host = host;
                
                if (!string.IsNullOrEmpty(database))
                    sqlDs.Database = database;
            }
            return Task.FromResult(dataSource);
        }
        
        /// <summary>
        /// Gets table info for a query by its ID from the allowedTables.json file
        /// </summary>
        /// <param name="id">The ID (TABLE_NAME) to look for</param>
        /// <returns>The TableInfo if found and is a QUERY type, otherwise null</returns>
        private TableInfo? GetQueryInfoFromJson(string? id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            
            try
            {
                var tables = GetTablesWithCaching();
                
                if (tables == null || !tables.Any())
                    return null;

                // Find query item with matching ID and type=QUERY (case-insensitive)
                return tables.FirstOrDefault(t => 
                    string.Equals(t.TableName, id, StringComparison.OrdinalIgnoreCase) && 
                    string.Equals(t.Type, "QUERY", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving query from JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the tables from allowedTables.json with caching support
        /// </summary>
        private List<TableInfo>? GetTablesWithCaching()
        {
            // Check if cache is valid
            if (_cachedTables != null && (DateTime.Now - _lastCacheRefresh) < _cacheTimeout)
                return _cachedTables;
                
            lock (_cacheLock)
            {
                // Double-check after acquiring lock
                if (_cachedTables != null && (DateTime.Now - _lastCacheRefresh) < _cacheTimeout)
                    return _cachedTables;
                    
                try
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "schemas", "allowedTables.json");
                    if (!File.Exists(filePath))
                        return null;
                        
                    var jsonData = File.ReadAllText(filePath);
                    
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    
                    _cachedTables = JsonSerializer.Deserialize<List<TableInfo>>(jsonData, options);
                    _lastCacheRefresh = DateTime.Now;
                    
                    return _cachedTables;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading tables from JSON: {ex.Message}");
                    return null;
                }
            }
        }
    }

    // TableInfo class definition
    internal class TableInfo
    {
        [JsonPropertyName("TABLE_SCHEMA")]
        public string? TableSchema { get; set; }
        
        [JsonPropertyName("TABLE_NAME")]
        public string? TableName { get; set; }
        
        [JsonPropertyName("COLUMN_NAME")]
        public string? ColumnName { get; set; }
        
        [JsonPropertyName("TYPE")]
        public string? Type { get; set; }
        
        [JsonPropertyName("FRIENDLY_NAME")]
        public string? FriendlyName { get; set; }
        
        [JsonPropertyName("QUERY")]
        public string? Query { get; set; }
    }
}
