param([string]$GameDir, [string]$BepInExDir, [string]$GalacticScaleDir, [string]$ArtifactDir)
$ErrorActionPreference = 'Stop'
$roots = @((Join-Path $GameDir 'DSPGAME_Data/Managed'), (Join-Path $BepInExDir 'core'), $GalacticScaleDir, $ArtifactDir)
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Reflection;
public static class CheckResolver {
    static string[] roots;
    public static void Install(string[] paths) { roots=paths; AppDomain.CurrentDomain.AssemblyResolve+=Resolve; }
    static Assembly Resolve(object sender,ResolveEventArgs args) {
        string name=new AssemblyName(args.Name).Name;
        foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies()) if(a.GetName().Name==name) return a;
        foreach(string directory in roots) { string path=Path.Combine(directory,name+".dll"); if(File.Exists(path)) return Assembly.Load(File.ReadAllBytes(path)); }
        return null;
    }
}
'@
[CheckResolver]::Install($roots)
foreach ($name in @('GSUI.dll', 'GalacticScale.dll')) { [void][Reflection.Assembly]::LoadFrom((Join-Path $GalacticScaleDir $name)) }
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $ArtifactDir 'BetterSolarSystem.Checks.dll')))
$scratch = Join-Path $ArtifactDir ('checks-' + [Guid]::NewGuid().ToString('N'))
try { [PolishChecks]::Run((Join-Path $scratch 'presets')) }
catch { Write-Host $_.Exception.GetBaseException().ToString(); throw }
[DefenseRewriteChecks]::Geometry()
[DefenseRewriteChecks]::Native((Join-Path $scratch 'legacy'))
