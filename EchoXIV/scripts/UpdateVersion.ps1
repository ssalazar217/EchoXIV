param (
    [string]$ProjectFile,
    [string]$ManifestFile
)

Write-Host "Auto-Incrementing Version..."

# 1. Update .csproj
if (Test-Path $ProjectFile) {
    $content = Get-Content $ProjectFile -Raw
    $versionPattern = "<Version>(.*?)</Version>"
    
    if ($content -match $versionPattern) {
        $currentVersion = $matches[1]
        $parts = $currentVersion.Split('.')
        
        # Ensure we have at least 3 parts (Major.Minor.Patch)
        # If user used 1.0, treat as 1.0.0
        while ($parts.Length -lt 3) { $parts += "0" }
        
        # Increment Patch
        $parts[$parts.Length - 1] = [int]$parts[$parts.Length - 1] + 1
        $newVersion = $parts -join '.'
        
        $content = $content -replace $versionPattern, "<Version>$newVersion</Version>"
        Set-Content $ProjectFile $content -NoNewline
        Write-Host "Updated .csproj Version to $newVersion"
        
        # 2. Update Manifest (.json)
        # Only if csproj was successfully parsed/updated, use that new version
        if (Test-Path $ManifestFile) {
            $jsonContent = Get-Content $ManifestFile -Raw
            
            # Update AssemblyVersion
            if ($jsonContent -match '"AssemblyVersion":\s*".*?"') {
                $jsonContent = $jsonContent -replace '"AssemblyVersion":\s*".*?"', """AssemblyVersion"": ""$newVersion"""
            }
            else {
                # Add if missing (naive insert before last brace)
                $jsonContent = $jsonContent.TrimEnd().TrimEnd('}') + ",`n  ""AssemblyVersion"": ""$newVersion""`n}"
            }
            
            # Optionally update description/changelog usage if needed, but AssemblyVersion is key for logic.
            # Also update "1.0.1" in Changelog if you wanted, but that's complex text replacement.
            
            Set-Content $ManifestFile $jsonContent -NoNewline
            Write-Host "Updated Manifest Version to $newVersion"
        }
    }
    else {
        Write-Warning "Could not find <Version> tag in $ProjectFile"
    }
}
else {
    Write-Error "Project file not found: $ProjectFile"
}
