using Npgsql;

// Describe tables created by E2E test
var connStr = "Host=localhost;Database=bmmdl_registry;Username=bmmdl;Password=bmmdl123";
await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

Console.WriteLine("\n=== BUSINESS TABLES (master_data_*) ===\n");

// Get all master_data tables
var tablesQuery = @"
SELECT tablename FROM pg_tables 
WHERE schemaname = 'public' AND tablename LIKE 'master_data%' 
ORDER BY tablename";

var tables = new List<string>();
await using (var cmd = new NpgsqlCommand(tablesQuery, conn))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
        tables.Add(reader.GetString(0));
}

Console.WriteLine($"Found {tables.Count} business tables:\n");

foreach (var table in tables)
{
    Console.WriteLine($"📋 {table.ToUpper()}");
    Console.WriteLine(new string('─', 70));
    
    var colQuery = $@"
        SELECT column_name, data_type, character_maximum_length, is_nullable
        FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = '{table}'
        ORDER BY ordinal_position";
    
    await using var colCmd = new NpgsqlCommand(colQuery, conn);
    await using var colReader = await colCmd.ExecuteReaderAsync();
    
    while (await colReader.ReadAsync())
    {
        var col = colReader.GetString(0);
        var type = colReader.GetString(1);
        var maxLen = colReader.IsDBNull(2) ? "" : $"({colReader.GetInt32(2)})";
        var nullable = colReader.GetString(3) == "YES" ? "NULL" : "NOT NULL";
        Console.WriteLine($"  {col,-25} {type}{maxLen,-15} {nullable}");
    }
    Console.WriteLine();
}
