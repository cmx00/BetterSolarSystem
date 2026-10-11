#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$BepInExDir,
    [Parameter(Mandatory)][string]$GalacticScaleDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Reference the user's installation. Game and dependency assemblies are not bundled.
$managedDir = Join-Path $GameDir 'DSPGAME_Data/Managed'
$coreDir = Join-Path $BepInExDir 'core'
$outputDir = Join-Path $PSScriptRoot 'artifacts'
$commonPaths = @(
    (Join-Path $managedDir 'mscorlib.dll'),
    (Join-Path $managedDir 'netstandard.dll'),
    (Join-Path $managedDir 'System.dll'),
    (Join-Path $managedDir 'System.Core.dll'),
    (Join-Path $managedDir 'Assembly-CSharp.dll'),
    (Join-Path $managedDir 'UnityEngine.dll'),
    (Join-Path $managedDir 'UnityEngine.CoreModule.dll'),
    (Join-Path $coreDir 'BepInEx.dll'),
    (Join-Path $coreDir '0Harmony.dll')
)
$pluginPaths = $commonPaths + @(
    (Join-Path $GalacticScaleDir 'GalacticScale.dll'),
    (Join-Path $GalacticScaleDir 'GSUI.dll')
)
$patcherPaths = $commonPaths + (Join-Path $coreDir 'Mono.Cecil.dll')
$compilerPaths = @(
    (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'),
    (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
)
foreach ($path in @($pluginPaths + $patcherPaths + $compilerPaths | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required assembly not found: $path"
    }
}
Add-Type -Path $compilerPaths

function Build-Library {
    param([string]$Name, [string]$SourceDir, [string[]]$ReferencePaths)

    $references = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
    foreach ($path in $ReferencePaths) {
        $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path))
    }
    $trees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $SourceDir -Filter '*.cs' -File -Recurse | Sort-Object FullName) {
        $relativePath = [System.IO.Path]::GetRelativePath($PSScriptRoot, $file.FullName).Replace('\', '/')
        $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
            [System.IO.File]::ReadAllText($file.FullName), $null, $relativePath))
    }
    if ($trees.Count -eq 0) { throw "No C# source files found: $SourceDir" }
    $options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
        [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary
    ).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithDeterministic($true)
    $compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
        $Name, $trees, $references, $options)
    $stream = [System.IO.MemoryStream]::new()
    try {
        $result = $compilation.Emit($stream)
        if (-not $result.Success) {
            $result.Diagnostics | Where-Object Severity -eq Error | ForEach-Object { Write-Host $_.ToString() }
            throw "Compilation failed: $Name"
        }
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
        $target = Join-Path $outputDir "$Name.dll"
        [System.IO.File]::WriteAllBytes($target, $stream.ToArray())
        Write-Host "Built $target ($((Get-Item -LiteralPath $target).Length) bytes)"
    } finally {
        $stream.Dispose()
    }
}

Build-Library 'BetterSolarSystem' (Join-Path $PSScriptRoot 'src/BetterSolarSystem') $pluginPaths
Build-Library 'BetterSolarSystem.Compat' (Join-Path $PSScriptRoot 'src/BetterSolarSystem.Compat') $patcherPaths
