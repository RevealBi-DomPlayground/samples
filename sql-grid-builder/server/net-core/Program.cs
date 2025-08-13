using Microsoft.Data.SqlClient;
using Reveal.Sdk;
using Reveal.Sdk.Data;
using Reveal.Sdk.Dom;
using Reveal.Sdk.Dom.Data;
using Reveal.Sdk.Dom.Visualizations;
using RevealSdk.Server.Reveal;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataSourceProvider = RevealSdk.Server.Reveal.DataSourceProvider;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("SqlConnection");

builder.Services.AddControllers().AddReveal(builder =>
{
    builder
        //.AddSettings(settings =>
        //{
        //    settings.License = "eyJhbGciOicCI6IkpXVCJ9.e";
        //})
        .AddAuthenticationProvider<AuthenticationProvider>()
        .AddDataSourceProvider<DataSourceProvider>()
        .DataSources.RegisterMicrosoftSqlServer();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
      builder => builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
    );
});

var app = builder.Build();
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// Get the id's & friendly names for the client from allowedTables.json
app.MapGet("/tables", async () =>
{
    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "schemas", "allowedTables.json");

    if (!File.Exists(filePath))
    {
        return Results.NotFound("The allowedTables.json file was not found.");
    }

    try
    {
        var jsonData = await File.ReadAllTextAsync(filePath);
        var tables = JsonSerializer.Deserialize<List<TableInfo>>(jsonData);

        return Results.Ok(tables);
    }
    catch (Exception ex)
    {
        return Results.Problem($"An error occurred while reading the file: {ex.Message}");
    }
})
.WithName("GetTables")
.Produces<IEnumerable<TableInfo>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

// Get column info for the TypeScript DOM client sample
app.MapGet("/tables/{tableName}/columns", async (string tableName) =>
{
    using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    string schemaName = "dbo";
    string query = @"
                    SELECT
                        t.TABLE_NAME,
                        c.COLUMN_NAME,
                        c.DATA_TYPE,
                        c.CHARACTER_MAXIMUM_LENGTH,
                        CASE WHEN c.IS_NULLABLE = 'YES' THEN 'Yes' ELSE 'No' END AS IS_NULLABLE
                    FROM
                        INFORMATION_SCHEMA.TABLES t
                    JOIN
                        INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME
                    WHERE
                        t.TABLE_SCHEMA = @SchemaName
                        AND t.TABLE_NAME = @TableName
                    ORDER BY
                        c.ORDINAL_POSITION;";

    using var command = new SqlCommand(query, connection);
    command.Parameters.AddWithValue("@SchemaName", schemaName);
    command.Parameters.AddWithValue("@TableName", tableName);

    using var reader = await command.ExecuteReaderAsync();

    var columns = new List<ColumnInfo>();

    while (await reader.ReadAsync())
    {
        var dataType = reader.GetString(2);
        var columnDetails = new ColumnInfo
        {
            TableName = reader.GetString(0),
            ColumnName = reader.GetString(1),
            DataType = dataType,
            MaxLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
            Nullable = reader.GetString(4),
            RevealDataType = MapSqlDataTypeToRevealDataType(dataType)
        };

        columns.Add(columnDetails);
    }
    return Results.Json(columns);
})
.Produces<List<ColumnInfo>>(StatusCodes.Status200OK);




// Endpoint to get the dashboard for a specific table or query
app.MapGet("/dashboard/{tableName}", async (string tableName) =>
{
    var underlyingDataPath = "dashboards/underlyingdata.rdash";
    if (File.Exists(underlyingDataPath))
    {
        File.Delete(underlyingDataPath);
    }

    // Check if this is a query-type item or a table from allowedTables.json
    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "schemas", "allowedTables.json");
    if (!File.Exists(filePath))
    {
        return Results.NotFound("The allowedTables.json file was not found.");
    }

    // Get table info from JSON
    var jsonData = await File.ReadAllTextAsync(filePath);
    var tables = JsonSerializer.Deserialize<List<TableInfo>>(jsonData);
    var tableInfo = tables?.FirstOrDefault(t => string.Equals(t.TableName, tableName, StringComparison.OrdinalIgnoreCase));
    
    if (tableInfo == null)
    {
        return Results.NotFound($"Table or query '{tableName}' not found.");
    }

    // Determine if this is a query or table based on TYPE
    bool isQuery = string.Equals(tableInfo.Type, "QUERY", StringComparison.OrdinalIgnoreCase);
    List<ColumnInfo> columns;

    using (var connection = new SqlConnection(connectionString))
    {
        await connection.OpenAsync();

        if (isQuery && !string.IsNullOrEmpty(tableInfo.Query))
        {
            // QUERY logic: Get column info via sp_describe_first_result_set
            var tsql = tableInfo.Query;

            using var cmd = new SqlCommand("sys.sp_describe_first_result_set", connection);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@tsql", tsql);
            cmd.Parameters.AddWithValue("@params", DBNull.Value);
            cmd.Parameters.AddWithValue("@browse_information_mode", 0);

            using var reader = await cmd.ExecuteReaderAsync();

            columns = new List<ColumnInfo>();
            while (await reader.ReadAsync())
            {
                var dataType = reader["system_type_name"] as string;
                var isNullable = (bool)reader["is_nullable"];
                int? maxLen = reader["max_length"] is short ml && ml > 0 ? (int?)ml : null;

                columns.Add(new ColumnInfo
                {
                    TableName = "", // Empty for queries
                    ColumnName = (reader["name"] as string) ?? "",
                    DataType = dataType ?? "",
                    MaxLength = maxLen,
                    Nullable = isNullable ? "Yes" : "No",
                    RevealDataType = MapSqlDataTypeToRevealDataType(dataType ?? "")
                });
            }
        }
        else
        {
            // TABLE logic: Get column info from INFORMATION_SCHEMA
            string schemaName = tableInfo.TableSchema ?? "dbo";
            string query = @"
                SELECT
                    t.TABLE_NAME,
                    c.COLUMN_NAME,
                    c.DATA_TYPE,
                    c.CHARACTER_MAXIMUM_LENGTH,
                    CASE WHEN c.IS_NULLABLE = 'YES' THEN 'Yes' ELSE 'No' END AS IS_NULLABLE
                FROM
                    INFORMATION_SCHEMA.TABLES t
                JOIN
                    INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME
                WHERE
                    t.TABLE_SCHEMA = @SchemaName
                    AND t.TABLE_NAME = @TableName
                ORDER BY
                    c.ORDINAL_POSITION;";

            using var cmd = new SqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@SchemaName", schemaName);
            cmd.Parameters.AddWithValue("@TableName", tableName);

            using var reader = await cmd.ExecuteReaderAsync();
            columns = new List<ColumnInfo>();
            while (await reader.ReadAsync())
            {
                var dataType = reader.GetString(2);
                columns.Add(new ColumnInfo
                {
                    TableName = reader.GetString(0),
                    ColumnName = reader.GetString(1),
                    DataType = dataType,
                    MaxLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                    Nullable = reader.GetString(4),
                    RevealDataType = MapSqlDataTypeToRevealDataType(dataType)
                });
            }
        }
    }

    if (columns.Count == 0)
    {
        return Results.NotFound($"{(isQuery ? "Query" : "Table")} '{tableName}' returned no columns.");
    }

    // Create and return the dashboard
    return await CreateDashboard(columns, tableName, isQuery, tableInfo.FriendlyName);
});

// Helper method to create dashboard
async Task<IResult> CreateDashboard(List<ColumnInfo> columns, string tableName, bool isQuery = false, string? friendlyName = null)
{
    // 2) Create the main RdashDocument
    var title = friendlyName ?? $"Dynamic Grid - {tableName}";
    var document = new RdashDocument(title);

    // 3) Create the SQL Data Source with config values
    var host = builder.Configuration["DatabaseSettings:Host"] ?? "jberes.database.windows.net";
    var database = builder.Configuration["DatabaseSettings:Database"] ?? "NorthwindCloud";
    
    var sqlServerDataSource = new MicrosoftSqlServerDataSource()
    {
        Title = "NorthwindCloud", // can be dynamically set based on context 
        Subtitle = "", // optional subtitle
        Host = host,
        Database = database,
    };

    // 4) Create a DataSourceItem referencing the table and its columns
    var dataSourceItem = new MicrosoftSqlServerDataSourceItem(title, tableName, sqlServerDataSource)
    {
        Id = isQuery ? tableName : "myTable", // Use tableName as Id for QUERY types to match in DataSourceProvider
        Table = isQuery ? "" : tableName,     // Set empty string for queries to avoid table lookup
        Subtitle = $"Data from {tableName}",
        Fields = MapColumnsToRevealFields(columns)
    };

    // 5) Create a GridVisualization bound to that DataSourceItem
    var gridVisualization = new GridVisualization("Dynamic Grid", dataSourceItem)
    {
        ColumnSpan = 3,
        RowSpan = 4,
        Description = $"Grid visualization for {tableName}",
        IsTitleVisible = true,
        Id = isQuery ? tableName : "myGrid", // Same ID as dataSourceItem for QUERY types
        Title = title
    };

    // 6) Configure some optional Grid settings
    gridVisualization.ConfigureSettings(settings =>
    {
        settings.FontSize = FontSize.Small;
        settings.PageSize = 30;
        settings.IsPagingEnabled = true;
        settings.IsFirstColumnFixed = true;
    });

    // 7) Decide which columns to display in the grid
    gridVisualization.SetColumns(columns.ConvertAll(c => c.ColumnName).ToArray());

    // 9) Add the visualization to the dashboard document
    document.Visualizations.Add(gridVisualization);

    // 10) Save and return the document
    document.Save("dashboards/underlyingdata.rdash");
    return Results.Ok(document.ToJsonString());
}

static List<IField> MapColumnsToRevealFields(List<ColumnInfo> columns)
{
    var result = new List<IField>();

    foreach (var col in columns)
    {
        // Adjust logic for your data type needs:
        switch (col.RevealDataType)
        {
            case "Number":
                result.Add(new NumberField(col.ColumnName) { FieldLabel = col.ColumnName });
                break;
            case "Date":
                result.Add(new DateField(col.ColumnName) { FieldLabel = col.ColumnName });
                break;
            case "Time":
                // There is no direct "Time" field in the same sense; could treat as text or date/time
                result.Add(new DateField(col.ColumnName) { FieldLabel = col.ColumnName });
                break;
            case "Boolean":
                result.Add(new TextField(col.ColumnName) { FieldLabel = col.ColumnName });
                break;
            default:
                // Default to text for simplicity
                result.Add(new TextField(col.ColumnName) { FieldLabel = col.ColumnName });
                break;
        }
    }

    return result;
}
string MapSqlDataTypeToRevealDataType(string dataType)
{
    return dataType.ToUpper() switch
    {
        "CHAR" or "VARCHAR" or "NVARCHAR" or "NCHAR" or "TEXT" or "TINYTEXT" or
        "MEDIUMTEXT" or "LONGTEXT" or "ENUM" or "SET" or "JSON" => "String",
        "INT" or "SMALLINT" or "TINYINT" or "MEDIUMINT" or "BIGINT" or "FLOAT" or
        "DOUBLE" or "DECIMAL" or "MONEY" or "REAL" => "Number",
        "DATE" or "DATETIME" or "TIMESTAMP" => "Date",
        "TIME" => "Time",
        "BOOLEAN" or "BIT" => "Boolean",
        "BINARY" or "VARBINARY" or "BLOB" or "TINYBLOB" or "MEDIUMBLOB" or
        "LONGBLOB" or "GEOMETRY" or "POINT" or "LINESTRING" or "POLYGON" or "BIT" => "Unsupported",
        _ => "String"
        //_ => throw new Exception($"Unsupported data type: {dataType}")
    };
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

public class TableInfo
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

public class ColumnInfo
{
    public required string? TableName { get; set; }
    public required string ColumnName { get; set; }
    public required string DataType { get; set; }
    public string? RevealDataType { get; set; }
    public int? MaxLength { get; set; }
    public string? Nullable { get; set; }
}
