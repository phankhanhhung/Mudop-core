namespace BMMDL.Runtime.Constants;

/// <summary>
/// Centralized OData-related constants used across the Runtime API.
/// </summary>
public static class ODataConstants
{
    /// <summary>
    /// Standard OData and application-level error codes.
    /// </summary>
    public static class ErrorCodes
    {
        // Entity errors
        public const string EntityNotFound = "ENTITY_NOT_FOUND";
        public const string EntityNotExposed = "ENTITY_NOT_EXPOSED";
        public const string NotFound = "NOT_FOUND";

        // Validation errors
        public const string ValidationError = "VALIDATION_ERROR";
        public const string ValidationFieldError = "VALIDATION_FIELD_ERROR";
        public const string InvalidArgument = "INVALID_ARGUMENT";
        public const string InvalidRequest = "INVALID_REQUEST";

        // Auth errors
        public const string Unauthorized = "UNAUTHORIZED";
        public const string Forbidden = "FORBIDDEN";
        public const string AccessDenied = "AccessDenied";
        public const string AccountLocked = "ACCOUNT_LOCKED";
        public const string InvalidProvider = "INVALID_PROVIDER";

        // Tenant errors
        public const string TenantNotFound = "TENANT_NOT_FOUND";
        public const string TenantRequired = "TENANT_REQUIRED";

        // Action errors
        public const string ActionExecutionFailed = "ACTION_EXECUTION_FAILED";
        public const string NotImplemented = "NOT_IMPLEMENTED";

        // Server errors
        public const string InternalError = "INTERNAL_ERROR";
        public const string Conflict = "CONFLICT";
        public const string RequestTimeout = "REQUEST_TIMEOUT";
        public const string DatabaseError = "DATABASE_ERROR";
        public const string UnknownError = "UNKNOWN_ERROR";

        // Sequence errors
        public const string SequenceNotFound = "SEQUENCE_NOT_FOUND";
        public const string SequenceError = "SEQUENCE_ERROR";

        // File storage errors
        public const string FieldNotFound = "FIELD_NOT_FOUND";
        public const string InvalidFieldType = "INVALID_FIELD_TYPE";
        public const string FileTooLarge = "FILE_TOO_LARGE";
        public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";

        // Singleton errors
        public const string SingletonNotFound = "SINGLETON_NOT_FOUND";
        public const string SingletonNoKey = "SINGLETON_NO_KEY";

        // Media errors
        public const string NotMediaEntity = "NOT_MEDIA_ENTITY";

        // Compute errors
        public const string InvalidCompute = "INVALID_COMPUTE";

        // Batch errors
        public const string BatchMissingDependency = "BATCH_MISSING_DEPENDENCY";
        public const string BatchAtomicityRollback = "BATCH_ATOMICITY_ROLLBACK";
        public const string BatchInvalidUrl = "BATCH_INVALID_URL";
        public const string BatchUnsupportedMethod = "BATCH_UNSUPPORTED_METHOD";
        public const string BatchEmptyBody = "BATCH_EMPTY_BODY";
        public const string BatchMissingId = "BATCH_MISSING_ID";

        // View errors
        public const string ViewNotFound = "VIEW_NOT_FOUND";
        public const string ViewNotMaterialized = "VIEW_NOT_MATERIALIZED";
        public const string ViewRefreshFailed = "VIEW_REFRESH_FAILED";
        public const string MissingParameter = "MISSING_PARAMETER";
    }

    /// <summary>
    /// OData system query option names.
    /// </summary>
    public static class QueryOptions
    {
        public const string Filter = "$filter";
        public const string OrderBy = "$orderby";
        public const string Expand = "$expand";
        public const string Select = "$select";
        public const string Top = "$top";
        public const string Skip = "$skip";
        public const string Count = "$count";
        public const string Search = "$search";
        public const string Apply = "$apply";
        public const string Compute = "$compute";
        public const string Value = "$value";
        public const string DeltaToken = "$deltatoken";
    }

    /// <summary>
    /// OData annotation names.
    /// </summary>
    public static class Annotations
    {
        public const string Singleton = "OData.Singleton";
    }

    /// <summary>
    /// OData namespace constants.
    /// </summary>
    public static class Namespaces
    {
        public const string Default = "Default";
    }

    /// <summary>
    /// OData HTTP header names.
    /// </summary>
    public static class Headers
    {
        public const string PreferenceApplied = "Preference-Applied";
    }

    /// <summary>
    /// OData Prefer header values.
    /// </summary>
    public static class PreferValues
    {
        public const string ReturnMinimal = "return=minimal";
        public const string ReturnRepresentation = "return=representation";
        public const string MaxPageSize = "odata.maxpagesize";
    }

    /// <summary>
    /// OData JSON response property names (@odata.*).
    /// </summary>
    public static class JsonProperties
    {
        public const string Context = "@odata.context";
        public const string Count = "@odata.count";
        public const string Id = "@odata.id";
        public const string Etag = "@odata.etag";
        public const string NextLink = "@odata.nextLink";
        public const string DeltaLink = "@odata.deltaLink";
        public const string MediaReadLink = "@odata.mediaReadLink";
        public const string MediaContentType = "@odata.mediaContentType";
        public const string MediaEtag = "@odata.mediaEtag";
        public const string Prefix = "@odata.";
    }
}
