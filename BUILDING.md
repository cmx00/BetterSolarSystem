# Building / 编译

Requires PowerShell 7, a local Dyson Sphere Program installation, BepInEx 5, and GalacticScale. The script uses Roslyn bundled with PowerShell; no separate .NET SDK is required. Game and dependency assemblies are referenced locally and are not included in this repository.

需要 PowerShell 7、本机《戴森球计划》、BepInEx 5 和 GalacticScale。编译器使用 PowerShell 自带的 Roslyn，无需单独安装 .NET SDK。游戏及依赖程序集由本地安装提供，仓库不包含这些文件。

```powershell
pwsh -NoProfile -File ./build.ps1 `
  -GameDir 'D:/Games/Dyson Sphere Program' `
  -BepInExDir 'D:/ModProfiles/Default/BepInEx' `
  -GalacticScaleDir 'D:/ModProfiles/Default/BepInEx/plugins/Galactic_Scale-GalacticScale'
```

Replace the three paths with your own installation locations. `GameDir` contains `DSPGAME_Data`; `BepInExDir` contains `core` and `plugins`; `GalacticScaleDir` contains both `GalacticScale.dll` and `GSUI.dll`. A mod manager profile's BepInEx directory may be separate from the game directory.

请替换为自己的安装路径：`GameDir` 内应有 `DSPGAME_Data`，`BepInExDir` 内应有 `core`、`plugins`，`GalacticScaleDir` 内应有 `GalacticScale.dll` 和 `GSUI.dll`。使用模组管理器时，BepInEx 目录可能位于游戏目录之外。

Outputs / 输出：

| File | Install location / 安装位置 |
| --- | --- |
| `artifacts/BetterSolarSystem.dll` | `BepInEx/plugins/BetterSolarSystem/` |
| `artifacts/BetterSolarSystem.Compat.dll` | `BepInEx/patchers/BetterSolarSystem/` |

The build only writes to `artifacts`. Close the game before copying the outputs into a profile. The compatibility DLL belongs in `patchers`, not `plugins`.

编译仅写入 `artifacts`。手动安装前先关闭游戏；兼容补丁 DLL 应放入 `patchers`。
