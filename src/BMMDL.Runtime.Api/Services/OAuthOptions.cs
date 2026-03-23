namespace BMMDL.Runtime.Api.Services;

/// <summary>
/// OAuth configuration options from appsettings.json.
/// </summary>
public class OAuthOptions
{
    public const string SectionName = "OAuth";
    
    public GoogleOAuthOptions Google { get; set; } = new();
    public MicrosoftOAuthOptions Microsoft { get; set; } = new();
    public AppleOAuthOptions Apple { get; set; } = new();
    
    /// <summary>
    /// Allow TEST_ tokens for development/testing.
    /// Should be false in production.
    /// </summary>
    public bool AllowTestTokens { get; set; } = false;
}

public class GoogleOAuthOptions
{
    /// <summary>
    /// Google OAuth Client ID from Google Cloud Console.
    /// Format: xxx.apps.googleusercontent.com
    /// </summary>
    public string ClientId { get; set; } = "";
    
    /// <summary>
    /// Enable Google OAuth validation.
    /// </summary>
    public bool Enabled { get; set; } = false;
}

public class MicrosoftOAuthOptions
{
    /// <summary>
    /// Microsoft/Azure AD Client ID.
    /// </summary>
    public string ClientId { get; set; } = "";
    
    /// <summary>
    /// Azure AD Tenant ID (or "common" for multi-tenant).
    /// </summary>
    public string TenantId { get; set; } = "common";
    
    /// <summary>
    /// Enable Microsoft OAuth validation.
    /// </summary>
    public bool Enabled { get; set; } = false;
}

public class AppleOAuthOptions
{
    /// <summary>
    /// Apple Service ID.
    /// </summary>
    public string ServiceId { get; set; } = "";
    
    /// <summary>
    /// Enable Apple OAuth validation.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
