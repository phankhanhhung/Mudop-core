using BMMDL.Compiler.Pipeline;
using BMMDL.MetaModel.Types;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// Validation pass for FileReference storage configuration.
/// Ensures all FileReference fields have required storage annotations
/// and validates storage provider settings.
///
/// Error Codes:
/// - FILE001: Missing @Storage.Provider annotation
/// - FILE002: Invalid storage provider (must be S3, GCS, MinIO, AzureBlob, Local)
/// - FILE003: Missing @Storage.Bucket annotation
/// - FILE004: Invalid @Storage.MaxSize (must be positive integer)
/// - FILE005: Invalid @Storage.AllowedTypes (must be array of MIME type strings)
/// - FILE006: FileReference field cannot be marked as key
/// - FILE007: FileReference field cannot have computed expression
/// </summary>
public class FileStorageValidationPass : ValidationPassBase
{
    public override string Name => "File Storage Validation";
    public override string Description => "Validates FileReference fields have required storage annotations and valid configuration";
    public override int Order => 49;  // After TenantIsolationPass (48), before TemporalValidation (50)

    public FileStorageValidationPass(ILogger? logger = null) : base(logger) { }

    protected override bool ExecuteValidation(CompilationContext context)
    {
        var errorCount = 0;

        foreach (var entity in context.Model!.Entities)
        {
            foreach (var field in entity.Fields)
            {
                if (field.TypeRef is BmFileReferenceType fileRefType)
                {
                    errorCount += ValidateFileReferenceField(context, entity.QualifiedName, field, fileRefType);
                }
            }
        }

        return errorCount == 0;
    }

    private int ValidateFileReferenceField(
        CompilationContext context,
        string entityName,
        MetaModel.Structure.BmField field,
        BmFileReferenceType fileRefType)
    {
        var errorCount = 0;

        // FILE006: FileReference cannot be a key field
        if (field.IsKey)
        {
            context.AddError(
                ErrorCodes.FILE_CANNOT_BE_KEY,
                $"FileReference field '{field.Name}' in entity '{entityName}' cannot be marked as key",
                field.SourceFile,
                field.StartLine
            );
            errorCount++;
        }

        // FILE007: FileReference cannot have computed expression
        if (field.IsComputed && field.ComputedExpr != null)
        {
            context.AddError(
                ErrorCodes.FILE_CANNOT_BE_COMPUTED,
                $"FileReference field '{field.Name}' in entity '{entityName}' cannot have computed expression",
                field.SourceFile,
                field.StartLine
            );
            errorCount++;
        }

        // FILE001: Check for @Storage.Provider annotation
        if (!field.HasAnnotation("Storage.Provider"))
        {
            context.AddError(
                ErrorCodes.FILE_MISSING_PROVIDER,
                $"FileReference field '{field.Name}' in entity '{entityName}' must have @Storage.Provider annotation",
                field.SourceFile,
                field.StartLine
            );
            errorCount++;
        }
        else
        {
            // FILE002: Validate provider value — warn (not error) for unknown providers
            // to allow extensibility with custom storage backends
            var provider = fileRefType.Provider;
            if (!BmFileReferenceType.IsValidProvider(provider))
            {
                context.AddWarning(
                    ErrorCodes.FILE_INVALID_PROVIDER,
                    $"Unknown storage provider '{provider}' for field '{field.Name}' in entity '{entityName}'. " +
                    "Known providers are: S3, GCS, MinIO, AzureBlob, Local",
                    field.SourceFile,
                    field.StartLine
                );
            }
        }

        // FILE003: Check for @Storage.Bucket annotation
        if (!field.HasAnnotation("Storage.Bucket"))
        {
            context.AddError(
                ErrorCodes.FILE_MISSING_BUCKET,
                $"FileReference field '{field.Name}' in entity '{entityName}' must have @Storage.Bucket annotation",
                field.SourceFile,
                field.StartLine
            );
            errorCount++;
        }
        else if (string.IsNullOrWhiteSpace(fileRefType.BucketName))
        {
            context.AddError(
                ErrorCodes.FILE_MISSING_BUCKET,
                $"@Storage.Bucket value for field '{field.Name}' in entity '{entityName}' cannot be empty",
                field.SourceFile,
                field.StartLine
            );
            errorCount++;
        }

        // FILE004: Validate @Storage.MaxSize if present
        var maxSizeAnnot = field.GetAnnotation("Storage.MaxSize");
        if (maxSizeAnnot != null)
        {
            if (fileRefType.MaxSizeBytes == null || fileRefType.MaxSizeBytes <= 0)
            {
                context.AddError(
                    ErrorCodes.FILE_INVALID_MAX_SIZE,
                    $"@Storage.MaxSize for field '{field.Name}' in entity '{entityName}' must be a positive integer",
                    field.SourceFile,
                    field.StartLine
                );
                errorCount++;
            }
        }

        // FILE005: Validate @Storage.AllowedTypes if present
        var allowedTypesAnnot = field.GetAnnotation("Storage.AllowedTypes");
        if (allowedTypesAnnot != null)
        {
            if (fileRefType.AllowedMimeTypes == null || fileRefType.AllowedMimeTypes.Count == 0)
            {
                context.AddWarning(
                    ErrorCodes.FILE_INVALID_MIME_TYPES,
                    $"@Storage.AllowedTypes for field '{field.Name}' in entity '{entityName}' is empty or invalid",
                    field.SourceFile,
                    field.StartLine
                );
            }
            else
            {
                // Validate each MIME type format
                foreach (var mimeType in fileRefType.AllowedMimeTypes)
                {
                    if (!BmFileReferenceType.IsValidMimeType(mimeType))
                    {
                        context.AddWarning(
                            ErrorCodes.FILE_INVALID_MIME_TYPES,
                            $"Invalid MIME type '{mimeType}' in @Storage.AllowedTypes for field '{field.Name}' in entity '{entityName}'",
                            field.SourceFile,
                            field.StartLine
                        );
                    }
                }
            }
        }

        return errorCount;
    }
}
