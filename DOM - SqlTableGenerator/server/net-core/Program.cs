using Microsoft.Data.SqlClient;
using Reveal.Sdk;
using Reveal.Sdk.Data;
using RevealSdk.Server.Reveal;
using System.Text.Json;
using System.Text.Json.Serialization;

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