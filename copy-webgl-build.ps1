# WebGL Build Copy Script
# Copies Unity WebGL build files to the project's deploy folder

$Source = "C:\Users\epick\OneDrive\Documents\Github\webgl-build"
$Dest = "C:\Users\epick\OneDrive\Documents\Github\Mental_Break_AlphaV2.0\Mental_Break_AlphaV2.0\webgl-build"

Write-Host "=== WebGL Build Copy Script ===" -ForegroundColor Cyan
Write-Host ""

# Check if source exists
if (-not (Test-Path $Source)) {
    Write-Host "ERROR: Source folder not found: $Source" -ForegroundColor Red
    exit 1
}

# Folders to copy
$FoldersToCopy = @("Build", "StreamingAssets")

foreach ($Folder in $FoldersToCopy) {
    $SourcePath = Join-Path $Source $Folder
    $DestPath = Join-Path $Dest $Folder
    
    if (Test-Path $SourcePath) {
        Write-Host "Copying $Folder..." -ForegroundColor Yellow
        
        # Remove old destination folder if it exists
        if (Test-Path $DestPath) {
            Remove-Item -Path $DestPath -Recurse -Force
        }
        
        # Copy the folder
        Copy-Item -Path $SourcePath -Destination $DestPath -Recurse -Force
        
        # Count files copied
        $FileCount = (Get-ChildItem -Path $DestPath -Recurse -File).Count
        Write-Host "  Copied $FileCount files to $Folder" -ForegroundColor Green
    } else {
        Write-Host "SKIP: $Folder not found in source" -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "=== Copy Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  1. Commit changes in GitHub Desktop" -ForegroundColor Gray
Write-Host "  2. Push to trigger Vercel rebuild" -ForegroundColor Gray
Write-Host ""

