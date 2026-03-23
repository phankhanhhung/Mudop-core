<#
.SYNOPSIS
    Migrates BMMDL files from flat syntax to new nested syntax.

.DESCRIPTION
    Converts old flat BMMDL syntax to new nested syntax with namespace blocks.

.EXAMPLE
    .\migrate-bmmdl-syntax.ps1 -Path .\erp_modules -Recurse
    .\migrate-bmmdl-syntax.ps1 -Path .\module.bmmdl -DryRun
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    
    [switch]$Recurse,
    [switch]$DryRun,
    [string]$BackupExtension = ".bak"
)

function Convert-BmmdlToNestedSyntax {
    param([string]$Content)
    
    # Extract module declaration
    $moduleMatch = [regex]::Match($Content, "(?ms)^(module\s+\w+\s+(version\s+'[^']+')?)\s*\{([^}]*)\}")
    if (-not $moduleMatch.Success) {
        return $null  # No module block found
    }
    
    $moduleHeader = $moduleMatch.Groups[1].Value
    $moduleProps = $moduleMatch.Groups[3].Value.Trim()
    
    # Extract namespace
    $nsMatch = [regex]::Match($Content, "namespace\s+([\w\.]+)\s*;")
    if (-not $nsMatch.Success) {
        return $null  # No namespace found
    }
    $namespace = $nsMatch.Groups[1].Value
    
    # Get everything before module (leading comments)
    $beforeModule = ""
    $moduleStart = $Content.IndexOf("module ")
    if ($moduleStart -gt 0) {
        $beforeModule = $Content.Substring(0, $moduleStart)
    }
    
    # Get everything after namespace; statement (definitions)
    $nsEnd = $nsMatch.Index + $nsMatch.Length
    $definitions = $Content.Substring($nsEnd).TrimStart()
    
    # Build new content
    $sb = [System.Text.StringBuilder]::new()
    
    # Leading comments
    if ($beforeModule.Trim()) {
        [void]$sb.AppendLine($beforeModule.TrimEnd())
        [void]$sb.AppendLine()
    }
    
    # Module with nested namespace
    [void]$sb.AppendLine("$moduleHeader {")
    
    # Module properties (indented)
    if ($moduleProps) {
        $propsLines = $moduleProps -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
        foreach ($prop in $propsLines) {
            [void]$sb.AppendLine("    $prop")
        }
        [void]$sb.AppendLine()
    }
    
    # Namespace block
    [void]$sb.AppendLine("    namespace $namespace {")
    
    # Definitions (indented 2 levels)
    $defLines = $definitions -split "`r?`n"
    foreach ($line in $defLines) {
        if ($line.Trim() -eq "") {
            [void]$sb.AppendLine()
        }
        else {
            [void]$sb.AppendLine("        $line")
        }
    }
    
    [void]$sb.AppendLine("    }")
    [void]$sb.AppendLine("}")
    
    return $sb.ToString()
}

# Main execution
$files = @()

if (Test-Path $Path -PathType Leaf) {
    $files = @(Get-Item $Path)
}
elseif (Test-Path $Path -PathType Container) {
    $params = @{ Path = $Path; Filter = "*.bmmdl" }
    if ($Recurse) { $params.Recurse = $true }
    $files = Get-ChildItem @params
}
else {
    Write-Error "Path not found: $Path"
    exit 1
}

Write-Host "Found $($files.Count) BMMDL file(s) to process" -ForegroundColor Cyan

$migrated = 0
$skipped = 0
$errors = 0

foreach ($file in $files) {
    Write-Host "`nProcessing: $($file.FullName)" -ForegroundColor Yellow
    
    try {
        $content = Get-Content $file.FullName -Raw
        
        # Skip if already uses nested namespace syntax
        if ($content -match "namespace\s+[\w\.]+\s*\{") {
            Write-Host "  [SKIP] Already uses nested syntax" -ForegroundColor Gray
            $skipped++
            continue
        }
        
        $newContent = Convert-BmmdlToNestedSyntax -Content $content
        
        if (-not $newContent) {
            Write-Host "  [SKIP] Could not parse (no module or namespace)" -ForegroundColor Gray
            $skipped++
            continue
        }
        
        if ($DryRun) {
            Write-Host "  [DRY-RUN] Would migrate to:" -ForegroundColor Magenta
            Write-Host "----------------------------------------"
            Write-Host $newContent
            Write-Host "----------------------------------------"
        }
        else {
            # Backup original
            $backupPath = "$($file.FullName)$BackupExtension"
            Copy-Item $file.FullName $backupPath -Force
            
            # Write migrated content  
            $newContent | Set-Content $file.FullName -NoNewline -Encoding UTF8
            
            Write-Host "  [MIGRATED] Backup: $backupPath" -ForegroundColor Green
        }
        
        $migrated++
    }
    catch {
        Write-Host "  [ERROR] $($_.Exception.Message)" -ForegroundColor Red
        $errors++
    }
}

Write-Host "`n==============================" -ForegroundColor Cyan
Write-Host "Summary: Migrated=$migrated, Skipped=$skipped, Errors=$errors" -ForegroundColor Cyan
Write-Host "==============================" -ForegroundColor Cyan
