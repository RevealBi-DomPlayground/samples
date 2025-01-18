using Microsoft.Data.SqlClient;
using Reveal.Sdk;
using Reveal.Sdk.Data;
using Reveal.Sdk.Dom;
using Reveal.Sdk.Dom.Data;
using Reveal.Sdk.Dom.Visualizations;
using RevealSdk.Server.Reveal;
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



// ---------------------------------------------------------------------------
//  GET /dashboard/{tableName}
//
//  Creates a minimal dashboard (RdashDocument) containing a single GridVisualization
//  bound to the requested table. It dynamically reads the table's columns
//  from the database, maps them to Reveal fields, and returns the resulting
//  RdashDocument as JSON.
// ---------------------------------------------------------------------------
app.MapGet("/dashboard/{tableName}", async (string tableName) =>
{

    var underlyingDataPath = "dashboards/underlyingdata.rdash";
    if (File.Exists(underlyingDataPath))
    {
        File.Delete(underlyingDataPath);
    }


    // 1) Fetch columns for the specified table
    List<ColumnInfo> columns;
    using (var connection = new SqlConnection(connectionString))
    {
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

    if (columns.Count == 0)
    {
        return Results.NotFound($"Table '{tableName}' not found or has no columns.");
    }

    // 2) Create the main RdashDocument
    var document = new RdashDocument($"Dynamic Grid - {tableName}");

    // 3) Create the SQL Data Source
    var sqlServerDataSource = new MicrosoftSqlServerDataSource()
    {
        Title = "NorthwindCloud",
        Subtitle = "Auto-generated",
        Host = "jberes.database.windows.net",     // or from config
        Database = "NorthwindCloud",                 // or from config
        // Provide credential details if needed
    };

    // 4) Create a DataSourceItem referencing the table and its columns
    var dataSourceItem = new MicrosoftSqlServerDataSourceItem($"{tableName} Table", tableName, sqlServerDataSource)
    {
        Table = tableName,
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
        Id = "GridVS",
        Title = $"Grid - {tableName}"
    };

    // 6) Configure some optional Grid settings
    gridVisualization.ConfigureSettings(settings =>
    {
        settings.FontSize = FontSize.Small;
        settings.PageSize = 30;
        settings.IsPagingEnabled = true;
        settings.IsFirstColumnFixed = true;
        // Alignments, etc. as needed...
    });

    // 7) Decide which columns to display in the grid (all columns or a subset)
    gridVisualization.SetColumns(columns.ConvertAll(c => c.ColumnName).ToArray());

    // 8) Optionally add some filters or quick filters (depends on your scenario)
    // For demonstration, we'll add a quick filter if we see any numeric columns
    //var numericColumns = columns.FindAll(c => c.RevealDataType == "Number");
    //if (numericColumns.Count > 0)
    //{
    //    gridVisualization.AddFilters(numericColumns[0].ColumnName);
    //}

    // 9) Add the visualization to the dashboard document
    document.Visualizations.Add(gridVisualization);

    // 10) Return the document to the caller
    //     The Reveal SDK (AddReveal in the pipeline) typically handles serialization,
    //     but you can also explicitly return JSON if you like.
    //return document.ToJsonString();
    document.Save("dashboards/underlyingdata.rdash");

    return Results.Ok(document.ToJsonString());

});


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
}

public class ColumnInfo
{
    public required string TableName { get; set; }
    public required string ColumnName { get; set; }
    public required string DataType { get; set; }
    public string? RevealDataType { get; set; }

    public int? MaxLength { get; set; }
    public string? Nullable { get; set; }
}
