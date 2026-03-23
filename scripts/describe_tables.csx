using Npgsql;

var connStr = "Host=localhost;Database=bmmdl_registry;Username=bmmdl;Password=bmmdl123";
await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

// Get tables
Console.WriteLine("=== TABLES CREATED BY E2E TEST ===\n");

var tablesCmd = new NpgsqlCommand(
    "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename LIKE 'master_data%' ORDER BY tablename",
    conn);
var tables = new List<string>();
await using (var reader = await tablesCmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
        tables.Add(reader.GetString(0));
}

foreach (var table in tables)
{
    Console.WriteLine($"📋 TABLE: {table}");
    Console.WriteLine(new string('-', 80));
    
    var colCmd = new NpgsqlCommand($@"
        SELECT column_name, data_type, is_nullable, column_default
        FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = '{table}'
        ORDER BY ordinal_position", conn);
    
    Console.WriteLine($"{"Column",-30} {"Type",-20} {"Nullable",-8} {"Default",-20}");
    Console.WriteLine(new string('-', 80));
    
    await using (var reader = await colCmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var col = reader.GetString(0);
            var type = reader.GetString(1);
            var nullable = reader.GetString(2);
            var def = reader.IsDBNull(3) ? "" : reader.GetString(3);
            if (def.Length > 18) def = def[..18] + "...";
            Console.WriteLine($"{col,-30} {type,-20} {nullable,-8} {def}");
        }
    }
    Console.WriteLine();
}

Console.WriteLine("\n=== TOTAL TABLES: " + tables.Count + " ===");
