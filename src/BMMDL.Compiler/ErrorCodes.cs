namespace BMMDL.Compiler;

/// <summary>
/// Centralized error codes for the BMMDL compiler.
/// Organized by category prefix:
/// - LEX: Lexical Analysis
/// - SYN: Syntactic Analysis
/// - MOD: Model Building / Modification
/// - SYM: Symbol Resolution
/// - DEP: Dependency Analysis
/// - SEM: Semantic Validation
/// - FILE: File Storage Validation
/// - TENANT: Tenant Isolation Validation
/// - OPT: Optimization
/// - ANN: Annotation Merge
/// - EXT: Extension Merge
/// - INH: Inheritance Resolution
/// - PASS: Pipeline
/// </summary>
public static class ErrorCodes
{
    #region Pipeline (PASS)
    /// <summary>Unhandled exception in a compiler pass</summary>
    public const string PASS_EXCEPTION = "PASS_EXCEPTION";
    #endregion

    #region Lexical Analysis (LEX)
    /// <summary>Failed to read source file</summary>
    public const string LEX_FILE_ERROR = "LEX001";
    /// <summary>Lexical analysis error (unrecognized token)</summary>
    public const string LEX_ERROR = "LEX002";
    #endregion

    #region Syntactic Analysis (SYN)
    /// <summary>Parse error (exception during parsing)</summary>
    public const string SYN_PARSE_ERROR = "SYN001";
    /// <summary>Syntax error reported by ANTLR parser</summary>
    public const string SYN_ERROR = "SYN002";
    #endregion

    #region Symbol Resolution (SYM)
    public const string SYM_NO_MODEL = "SYM001";
    public const string SYM_UNRESOLVED_REF = "SYM010";
    #endregion

    #region Dependency Analysis (DEP)
    public const string DEP_NO_MODEL = "DEP001";
    public const string DEP_CIRCULAR_ENTITY = "DEP010";
    public const string DEP_CIRCULAR_EXPRESSION = "DEP020";
    #endregion

    #region Semantic Validation (SEM)
    public const string SEM_NO_MODEL = "SEM001";
    public const string SEM_ENTITY_NO_FIELDS = "SEM010";
    public const string SEM_ENTITY_NO_KEY = "SEM011";
    public const string SEM_DUPLICATE_FIELD = "SEM012";
    public const string SEM_COMPUTED_NO_EXPR = "SEM013";
    public const string SEM_ASSOC_NO_TARGET = "SEM014";
    public const string SEM_RULE_NO_TARGET = "SEM020";
    public const string SEM_RULE_NO_TRIGGERS = "SEM021";
    public const string SEM_RULE_NO_STATEMENTS = "SEM022";
    public const string SEM_VALIDATE_NO_EXPR = "SEM023";
    public const string SEM_COMPUTE_NO_TARGET = "SEM024";
    public const string SEM_COMPUTE_NO_EXPR = "SEM025";
    public const string SEM_WHEN_NO_CONDITION = "SEM026";
    public const string SEM_CALL_NO_TARGET = "SEM027";
    public const string SEM_ACCESS_NO_TARGET = "SEM030";
    public const string SEM_ACCESS_NO_RULES = "SEM031";
    public const string SEM_TYPE_NO_FIELDS = "SEM040";
    public const string SEM_ENUM_NO_VALUES = "SEM041";
    public const string SEM_DUPLICATE_ENTITY = "SEM050";
    public const string SEM_DUPLICATE_TYPE = "SEM051";

    /// <summary>Duplicate enum integer value within same enum</summary>
    public const string SEM_DUPLICATE_ENUM_VALUE = "SEM060";
    /// <summary>Index references non-existent field/association</summary>
    public const string SEM_INVALID_INDEX_COLUMN = "SEM061";
    /// <summary>Cardinality min > max or min < 0</summary>
    public const string SEM_INVALID_CARDINALITY = "SEM062";
    /// <summary>Default expression type incompatible with field type</summary>
    public const string SEM_DEFAULT_TYPE_MISMATCH = "SEM063";
    /// <summary>Constraint references non-existent field</summary>
    public const string SEM_INVALID_CONSTRAINT_FIELD = "SEM064";
    /// <summary>Function/action parameter type unresolved</summary>
    public const string SEM_UNRESOLVED_PARAM_TYPE = "SEM065";
    /// <summary>Type alias base type unresolved</summary>
    public const string SEM_UNRESOLVED_BASE_TYPE = "SEM066";
    /// <summary>Action 'modifies' references non-existent field</summary>
    public const string SEM_ACTION_MODIFIES_INVALID_FIELD = "SEM067";
    /// <summary>Action 'emits' references unknown event</summary>
    public const string SEM_ACTION_EMITS_UNKNOWN_EVENT = "SEM068";

    // --- Statement validation (H8) ---
    /// <summary>Emit statement references unknown event</summary>
    public const string SEM_EMIT_NO_EVENT = "SEM070";
    /// <summary>Return statement has no expression</summary>
    public const string SEM_RETURN_NO_EXPR = "SEM071";
    /// <summary>Let statement has no variable name</summary>
    public const string SEM_LET_NO_VARIABLE = "SEM072";
    /// <summary>Let statement has no expression</summary>
    public const string SEM_LET_NO_EXPR = "SEM073";
    /// <summary>Reject statement has no message</summary>
    public const string SEM_REJECT_NO_MESSAGE = "SEM074";
    /// <summary>Foreach statement has no variable name</summary>
    public const string SEM_FOREACH_NO_VARIABLE = "SEM075";
    /// <summary>Foreach statement has no collection</summary>
    public const string SEM_FOREACH_NO_COLLECTION = "SEM076";
    /// <summary>Foreach statement has no body statements</summary>
    public const string SEM_FOREACH_NO_BODY = "SEM077";
    /// <summary>Subquery/EXISTS expression not supported in rule context</summary>
    public const string SEM_SUBQUERY_IN_RULE = "SEM078";

    // --- View validation (H9) ---
    /// <summary>View projection references non-existent entity</summary>
    public const string SEM_VIEW_ENTITY_NOT_FOUND = "SEM080";
    /// <summary>View projection field does not exist on source entity</summary>
    public const string SEM_VIEW_FIELD_NOT_FOUND = "SEM081";
    /// <summary>View excluded field does not exist on source entity</summary>
    public const string SEM_VIEW_EXCLUDED_FIELD_NOT_FOUND = "SEM082";
    /// <summary>View has neither SELECT statement nor projection</summary>
    public const string SEM_VIEW_NO_DEFINITION = "SEM083";

    // --- Event validation (H10) ---
    /// <summary>Event field type is not a known type</summary>
    public const string SEM_EVENT_FIELD_UNKNOWN_TYPE = "SEM090";
    /// <summary>Duplicate field name in event</summary>
    public const string SEM_EVENT_DUPLICATE_FIELD = "SEM091";
    /// <summary>Event has no fields</summary>
    public const string SEM_EVENT_NO_FIELDS = "SEM092";

    // --- Event validation (M9) ---
    /// <summary>Duplicate event name in model</summary>
    public const string SEM_DUPLICATE_EVENT = "SEM093";
    /// <summary>Service action emits clause references non-existent event</summary>
    public const string SEM_EMITS_EVENT_NOT_FOUND = "SEM094";

    // --- Sequence validation (H11) ---
    /// <summary>Sequence increment is zero</summary>
    public const string SEM_SEQUENCE_ZERO_INCREMENT = "SEM100";
    /// <summary>Sequence min >= max</summary>
    public const string SEM_SEQUENCE_INVALID_RANGE = "SEM101";
    /// <summary>Sequence references non-existent entity</summary>
    public const string SEM_SEQUENCE_ENTITY_NOT_FOUND = "SEM102";
    /// <summary>Sequence references non-existent field on entity</summary>
    public const string SEM_SEQUENCE_FIELD_NOT_FOUND = "SEM103";
    /// <summary>Sequence field must be integer-compatible type</summary>
    public const string SEM_SEQUENCE_FIELD_NOT_INTEGER = "SEM104";
    /// <summary>Sequence increment must be positive</summary>
    public const string SEM_SEQUENCE_NEGATIVE_INCREMENT = "SEM105";

    // --- Migration validation (M10) ---
    /// <summary>Duplicate migration version in model</summary>
    public const string SEM_MIGRATION_DUPLICATE_VERSION = "SEM110";
    /// <summary>Migration references non-existent entity</summary>
    public const string SEM_MIGRATION_ENTITY_NOT_FOUND = "SEM111";
    /// <summary>Migration has no up steps</summary>
    public const string SEM_MIGRATION_NO_STEPS = "SEM112";
    /// <summary>Migration references non-existent field</summary>
    public const string SEM_MIGRATION_FIELD_NOT_FOUND = "SEM113";

    // --- Seed data validation ---
    /// <summary>Seed references non-existent entity</summary>
    public const string SEM_SEED_ENTITY_NOT_FOUND = "SEM120";
    /// <summary>Seed column not found on target entity</summary>
    public const string SEM_SEED_COLUMN_NOT_FOUND = "SEM121";
    /// <summary>Seed row value count doesn't match column count</summary>
    public const string SEM_SEED_ROW_COUNT_MISMATCH = "SEM122";
    /// <summary>Seed column references a computed/virtual field</summary>
    public const string SEM_SEED_COMPUTED_FIELD = "SEM123";
    /// <summary>Duplicate seed name in model</summary>
    public const string SEM_SEED_DUPLICATE_NAME = "SEM124";
    /// <summary>Seed has no data rows (warning)</summary>
    public const string SEM_SEED_NO_ROWS = "SEM125";
    /// <summary>Seed does not include key field (warning — rows may not be idempotent)</summary>
    public const string SEM_SEED_MISSING_KEY = "SEM126";
    #endregion

    #region File Storage (FILE)
    public const string FILE_MISSING_PROVIDER = "FILE001";
    public const string FILE_INVALID_PROVIDER = "FILE002";
    public const string FILE_MISSING_BUCKET = "FILE003";
    public const string FILE_INVALID_MAX_SIZE = "FILE004";
    public const string FILE_INVALID_MIME_TYPES = "FILE005";
    public const string FILE_CANNOT_BE_KEY = "FILE006";
    public const string FILE_CANNOT_BE_COMPUTED = "FILE007";
    #endregion

    #region Tenant Isolation (TENANT)
    public const string TENANT_MISSING_ID = "TENANT001";
    public const string TENANT_GLOBAL_REFS_SCOPED = "TENANT002";
    public const string TENANT_SCOPED_REFS_GLOBAL = "TENANT003";
    public const string TENANT_COMPOSITION_MISMATCH = "TENANT004";
    public const string TENANT_REDUNDANT_GLOBAL_SCOPE = "TENANT005";
    public const string TENANT_INVALID_SCOPE = "TENANT006";
    public const string TENANT_MODULE_INCONSISTENCY = "TENANT007";
    /// <summary>Service exposes tenant-scoped entity without being tenant-scoped</summary>
    public const string TENANT_SERVICE_EXPOSES_SCOPED = "TENANT008";
    /// <summary>Service event handler references tenant-scoped event without service being tenant-scoped</summary>
    public const string TENANT_HANDLER_REFS_SCOPED = "TENANT009";
    #endregion

    #region Optimization (OPT)
    /// <summary>No model available for optimization</summary>
    public const string OPT_NO_MODEL = "OPT001";
    /// <summary>Inlined fields from aspects (info)</summary>
    public const string OPT_INLINED_FIELDS = "OPT010";
    /// <summary>Inlined behavioral rules/access controls from aspects (info)</summary>
    public const string OPT_INLINED_BEHAVIORS = "OPT011";
    /// <summary>Duplicate type detected (info)</summary>
    public const string OPT_DUPLICATE_TYPE = "OPT020";
    /// <summary>Generated cross-aspect views (info)</summary>
    public const string OPT_CROSS_ASPECT_VIEWS = "OPT030";
    /// <summary>Circular aspect inclusion detected</summary>
    public const string OPT_CIRCULAR_ASPECT = "OPT_CIRCULAR_ASPECT";
    #endregion

    #region Annotation Merge (ANN)
    /// <summary>No model available for annotation merge</summary>
    public const string ANN_NO_MODEL = "ANN001";
    /// <summary>Annotate target not found in model</summary>
    public const string ANN_TARGET_NOT_FOUND = "ANN002";
    /// <summary>Field not found on annotate target</summary>
    public const string ANN_FIELD_NOT_FOUND = "ANN003";
    /// <summary>Annotation merge summary (info)</summary>
    public const string ANN_SUMMARY = "ANN010";
    /// <summary>Annotation removed via null value convention</summary>
    public const string ANN_REMOVED = "ANN020";
    #endregion

    #region Extension (EXT)
    /// <summary>No model available for extension merge</summary>
    public const string EXT_NO_MODEL = "EXT001";
    /// <summary>Extension target kind not yet supported</summary>
    public const string EXT_UNSUPPORTED_KIND = "EXT002";
    /// <summary>Extension merge summary (info)</summary>
    public const string EXT_INFO = "EXT_INFO";
    /// <summary>Extension target entity not found</summary>
    public const string EXT_TARGET_NOT_FOUND = "EXT010";
    /// <summary>Extension adds duplicate field</summary>
    public const string EXT_DUPLICATE_FIELD = "EXT011";
    /// <summary>Extension cannot redefine key fields</summary>
    public const string EXT_KEY_REDEFINITION = "EXT012";
    /// <summary>Extension adds duplicate enum member</summary>
    public const string EXT_DUPLICATE_ENUM_MEMBER = "EXT013";
    #endregion

    #region Modification (MOD)
    /// <summary>Model build error or no model available</summary>
    public const string MOD_BUILD_ERROR = "MOD001";
    /// <summary>No model elements found or unsupported target kind</summary>
    public const string MOD_NO_ELEMENTS = "MOD002";
    /// <summary>Unsupported modify action type</summary>
    public const string MOD_UNSUPPORTED_ACTION = "MOD003";
    /// <summary>Modification summary (info)</summary>
    public const string MOD_SUMMARY = "MOD_INFO";
    /// <summary>Modification target entity not found</summary>
    public const string MOD_TARGET_NOT_FOUND = "MOD010";
    /// <summary>Field to remove not found</summary>
    public const string MOD_FIELD_NOT_FOUND = "MOD011";
    /// <summary>Field to rename not found</summary>
    public const string MOD_RENAME_NOT_FOUND = "MOD012";
    /// <summary>New field name already exists</summary>
    public const string MOD_RENAME_CONFLICT = "MOD013";
    /// <summary>Field to change type not found</summary>
    public const string MOD_CHANGE_TYPE_NOT_FOUND = "MOD014";
    /// <summary>Field to modify not found</summary>
    public const string MOD_MODIFY_NOT_FOUND = "MOD015";
    /// <summary>Field already exists (skipping add)</summary>
    public const string MOD_FIELD_EXISTS = "MOD016";
    /// <summary>Cannot remove a key field</summary>
    public const string MOD_CANNOT_REMOVE_KEY = "MOD020";
    /// <summary>Type change may cause data loss</summary>
    public const string MOD_TYPE_INCOMPATIBLE = "MOD021";
    /// <summary>Field is referenced by an association and cannot be removed</summary>
    public const string MOD_FIELD_IN_USE = "MOD022";
    /// <summary>Enum member to remove not found</summary>
    public const string MOD_ENUM_MEMBER_NOT_FOUND = "MOD030";
    /// <summary>Enum member already exists (add duplicate)</summary>
    public const string MOD_ENUM_MEMBER_EXISTS = "MOD031";
    #endregion

    #region Inheritance (INH)
    /// <summary>No model available for inheritance resolution</summary>
    public const string INH_NO_MODEL = "INH001";
    /// <summary>Inheritance resolution summary (info)</summary>
    public const string INH_SUMMARY = "INH_INFO";
    /// <summary>Parent entity not found</summary>
    public const string INH_PARENT_NOT_FOUND = "INH010";
    /// <summary>Circular inheritance detected</summary>
    public const string INH_CIRCULAR = "INH011";
    /// <summary>Cannot inherit from non-abstract entity</summary>
    public const string INH_PARENT_NOT_ABSTRACT = "INH012";
    /// <summary>Abstract entity cannot be instantiated directly</summary>
    public const string INH_ABSTRACT_INSTANTIATION = "INH013";
    /// <summary>Diamond inheritance detected (entity appears multiple times in inheritance chain)</summary>
    public const string INH_DIAMOND = "INH014";
    #endregion

    #region Binding & Path Navigation (BIND)
    /// <summary>Unresolved association in expression path navigation</summary>
    public const string BIND_UNRESOLVED_ASSOCIATION = "BIND001";
    /// <summary>Unresolved target entity in expression path navigation</summary>
    public const string BIND_UNRESOLVED_TARGET_ENTITY = "BIND002";
    /// <summary>Unresolved field at end of expression path navigation</summary>
    public const string BIND_UNRESOLVED_PATH_FIELD = "BIND003";
    #endregion

    #region Plugin Annotation Validation (PANN)
    /// <summary>No model available for plugin annotation validation</summary>
    public const string PANN_NO_MODEL = "PANN001";
    /// <summary>Required annotation property is missing</summary>
    public const string PANN_MISSING_REQUIRED = "PANN002";
    /// <summary>Annotation property value type does not match schema</summary>
    public const string PANN_TYPE_MISMATCH = "PANN003";
    /// <summary>Unknown annotation property (not declared in schema)</summary>
    public const string PANN_UNKNOWN_PROPERTY = "PANN004";
    /// <summary>Annotation applied to wrong target (e.g., field-only annotation on entity)</summary>
    public const string PANN_WRONG_TARGET = "PANN005";
    /// <summary>Annotation property value not in allowed set</summary>
    public const string PANN_VALUE_NOT_ALLOWED = "PANN006";
    /// <summary>Plugin annotation validation summary (info)</summary>
    public const string PANN_SUMMARY = "PANN010";
    #endregion

    #region Feature Contribution (FEAT)
    /// <summary>No model available for feature contribution</summary>
    public const string FEAT_NO_MODEL = "FEAT001";
    /// <summary>No feature metadata contributors registered (info)</summary>
    public const string FEAT_NO_CONTRIBUTORS = "FEAT002";
    /// <summary>Feature contributor threw an exception</summary>
    public const string FEAT_CONTRIBUTOR_ERROR = "FEAT003";
    /// <summary>Feature contribution summary (info)</summary>
    public const string FEAT_SUMMARY = "FEAT010";
    #endregion

    #region Temporal Validation (TEMP)
    /// <summary>@Temporal.ValidTime missing required 'from' property</summary>
    public const string TEMP_VALIDTIME_MISSING_FROM = "TEMP001";
    /// <summary>@Temporal.ValidTime missing required 'to' property</summary>
    public const string TEMP_VALIDTIME_MISSING_TO = "TEMP002";
    /// <summary>@Temporal.ValidTime 'from' column not found in entity fields</summary>
    public const string TEMP_VALIDTIME_FROM_NOT_FOUND = "TEMP003";
    /// <summary>@Temporal.ValidTime 'to' column not found in entity fields</summary>
    public const string TEMP_VALIDTIME_TO_NOT_FOUND = "TEMP004";
    /// <summary>@Temporal.ValidTime columns must be Date or DateTime/Timestamp type</summary>
    public const string TEMP_VALIDTIME_WRONG_TYPE = "TEMP005";
    /// <summary>Reserved column names conflict with temporal system columns</summary>
    public const string TEMP_RESERVED_COLUMN = "TEMP006";
    /// <summary>Invalid @Temporal strategy value (must be 'inline' or 'separate')</summary>
    public const string TEMP_INVALID_STRATEGY = "TEMP007";
    /// <summary>Temporal entity key field cannot have computed expression</summary>
    public const string TEMP_KEY_COMPUTED = "TEMP008";
    /// <summary>Bitemporal entity with SeparateTables may have complexity issues</summary>
    public const string TEMP_BITEMPORAL_SEPARATE_WARNING = "TEMP009";
    /// <summary>@Temporal.ValidTime columns should not be nullable</summary>
    public const string TEMP_VALIDTIME_NULLABLE = "TEMP010";
    /// <summary>Unique index on temporal entity should consider system time columns</summary>
    public const string TEMP_UNIQUE_INDEX_WARNING = "TEMP011";
    /// <summary>@Temporal.ValidTime from and to columns cannot be the same</summary>
    public const string TEMP_VALIDTIME_SAME_COLUMN = "TEMP012";
    /// <summary>SeparateTables strategy entity has no key fields for history table FK</summary>
    public const string TEMP_SEPARATE_NO_KEY = "TEMP013";
    #endregion
}

