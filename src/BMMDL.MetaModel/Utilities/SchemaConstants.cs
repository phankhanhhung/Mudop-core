namespace BMMDL.MetaModel.Utilities;

/// <summary>
/// Central constants for database schema names, well-known column names, etc.
/// Eliminates magic strings scattered across Runtime and Runtime.Api.
/// </summary>
public static class SchemaConstants
{
    public const string TenantIdColumn = "tenant_id";
    public const string PlatformSchema = "platform";
    public const string PrimaryKeyColumn = "id";
    public const string CoreSchema = "core";

    // Platform table names
    public const string AuditLogsTable = "platform.audit_logs";
    public const string EventOutboxTable = "platform.event_outbox";
    public const string UserPreferencesTable = "platform.user_preferences";
    public const string ReportTemplateTable = "platform.report_template";
    public const string RefreshTokenTable = "platform.refresh_token";
    public const string WebhookConfigsTable = "platform.webhook_configs";
    public const string WebhookDeliveryLogTable = "platform.webhook_delivery_log";
    public const string CommentTable = "platform.comment";
    public const string ChangeRequestTable = "platform.change_request";

    // Column names
    public const string CreatedAtColumn = "created_at";
    public const string DiscriminatorColumn = "_discriminator";
}
