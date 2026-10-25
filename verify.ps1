#Requires -Version 7.0
param([Parameter(Mandatory)][string]$GameDir, [Parameter(Mandatory)][string]$BepInExDir,
    [Parameter(Mandatory)][string]$GalacticScaleDir)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build.ps1') -GameDir $GameDir -BepInExDir $BepInExDir -GalacticScaleDir $GalacticScaleDir
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($path in @($pluginPaths + (Join-Path $outputDir 'BetterSolarSystem.dll') | Select-Object -Unique)) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path))
}
$checkTrees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($path in @((Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs').FullName) +
    (Join-Path $PSScriptRoot 'src/BetterSolarSystem/EnemyCandidateIndex.cs')) {
    $checkTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($path)))
}
$checkOptions = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$checks = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('BetterSolarSystem.Checks', $checkTrees, $refs, $checkOptions)
$stream = [IO.MemoryStream]::new()
try {
    $result = $checks.Emit($stream)
    if (!$result.Success) { $result.Diagnostics | Where-Object Severity -eq Error | ForEach-Object ToString; throw 'Checks did not compile.' }
    [IO.File]::WriteAllBytes((Join-Path $outputDir 'BetterSolarSystem.Checks.dll'), $stream.ToArray())
} finally { $stream.Dispose() }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests/run-native.ps1') `
    -GameDir $GameDir -BepInExDir $BepInExDir -GalacticScaleDir $GalacticScaleDir -ArtifactDir $outputDir
if ($LASTEXITCODE -ne 0) { throw 'Targeted checks failed.' }
