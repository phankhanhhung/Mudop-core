using Npgsql;

namespace BMMDL.Runtime.Plugins.Contexts;

/// <summary>
/// A delete operation produced by <see cref="IFeatureDeleteStrategy"/>.
/// Contains the SQL template and parameters to execute instead of a hard DELETE.
/// </summary>
public record DeleteOperation(string SqlTemplate, List<NpgsqlParameter> Parameters);
