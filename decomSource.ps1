$gameManaged = "D:\SteamLibrary\steamapps\common\Apocalypter\Apocalypter_Data\Managed"
$outputDir   = "D:\projects\ApocalypterSource"

# Get DLLs, skipping massive standard .NET/Mono libraries
Get-ChildItem -Path $gameManaged -Filter "*.dll" | Where-Object {
    $_.Name -notmatch '^(System\.|mscorlib|Mono\.|netstandard|WindowsBase|I18N)'
} | ForEach-Object {
    $projectName = $_.BaseName
    $targetPath = Join-Path $outputDir $projectName
    Write-Host "==> Decompiling $projectName..." -ForegroundColor Cyan
    ilspycmd -p -o $targetPath $_.FullName
}

Write-Host "All game assemblies successfully decompiled!" -ForegroundColor Green