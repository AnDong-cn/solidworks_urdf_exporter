# SolidWorks 2025：每个 link 同时导出 STEP

此分支基于上游 `882169e28952f0d17c87d7eab98826454421aabf`，目标环境为 Windows x64、SolidWorks 2025、Visual Studio 2022 和 .NET Framework 4.8。

正常导出 STL 时，在同一 `meshes` 目录生成同名 `.STEP`。STEP 使用 SolidWorks 原生 CAD 实体和曲面，复用该次 STL 实际生效的输出坐标系。URDF 仍引用 STL。3DXML、不导出 mesh、参考 frame、惯量和关节逻辑沿用上游。

沿用当前 STEP 格式选项，只在保存期间临时关闭“按组件分文件”，之后恢复。某个 STEP 失败不会中止原导出；结束后一次提示失败 link、目标路径和原因，并记录到包内 `export.log`。目标文件已有旧版本时，只有新临时文件成功保存后才替换它；替换失败会保留旧文件并明确提示。

## 编译

VS2022 安装 `.vsconfig` 中的 .NET 桌面开发及 .NET Framework 4.8 SDK/目标包，另安装 .NET 8 SDK（测试运行器使用 SDK 格式工程）。SolidWorks API 默认从 `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS` 引用；自定义安装位置时传入 MSBuild 属性 `SolidWorksDir`。NuGet 依赖版本保持上游不变。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build.ps1 -Test -Installer
```

输出为 `SW2URDF\bin\x64\Release` 和 `artifacts\installer\sw2urdfSetup-step.exe`。构建不执行 COM 注册，无需管理员权限；安装阶段才注册。安装程序保留上游安装标识和插件 COM GUID，并阻止在 SolidWorks 运行时替换文件。产品版本后缀为 `-step.1`。

若暂时不能提升权限安装 VS 目标包，可将官方 NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3 解压到 `artifacts\tools\net48`；工程会自动使用其中的 `build\.NETFramework\v4.8`。本机验证使用了此方式，测试 SDK 放在 `artifacts\tools\dotnet`，Inno Setup 6 放在 `artifacts\tools\inno`。这些工具和构建产物不会提交到 Git。

## 验证

测试直接驱动本机 SolidWorks COM API，需要有效许可、标准 `gb_part.prtdot` / `gb_assembly.asmdot` 模板，并会关闭测试会话中的文档。请在空闲 SolidWorks 会话运行。测试不保存仓库示例的修改。

新增 `TestSTEPExport` 覆盖三个仓库示例、两种单零件朝向、平移/旋转输出坐标系、子装配体、重复组件实例、多实体零件、多个零件组成同一 link、组件去重和不属于该 link 的实体排除。还覆盖 3DXML、不导出 mesh、参考 frame、旧 STEP 文件被占用、临时文件清理、后续 link 继续导出、设置和选择状态恢复。

将 STEP 原生重新导入，SolidWorks API 按米返回坐标，再与二进制 STL 比较。非对称平面模型检查包围盒、全部角点和实体数量，容差 `10⁻⁵ m`；曲面示例使用上游粗 STL 网格，包围盒容差 `5×10⁻⁴ m`，不能将网格离散误差当作坐标误差。

可选的上游二进制回归比较需先编译未改动的上游版本，并设置：

```powershell
$env:SW2URDF_BASELINE_DLL = '完整路径\baseline\SW2URDF.dll'
powershell -ExecutionPolicy Bypass -File scripts\Build.ps1 -Test
```

比较三个示例的 STL（三角形数量及所有浮点数据）、CSV、URDF 所有 robot 元素，包含质量、惯量和关节变换。STL 浮点差值上限为 `10⁻⁷`，CSV/URDF 数值容差为 `10⁻¹⁰×max(1, |原值|)`，其余文本须一致；忽略 URDF 顶部版本注释。这些容差允许 SolidWorks 重开模型时产生的数值舍入。未设置该变量时明确输出未执行可选比较的提示。

子装配体中的坐标系如果上游没有成功设置为导出坐标系，本功能沿用 STL 实际生效的坐标系；不修改原 STL 或 URDF 来补偿。这保留了上游行为，并保证新增 STEP 与现有 STL 一致。

## 备份、安装与回滚

安装前执行 `scripts\Backup-InstalledPlugin.ps1`，备份安装目录、COM 注册、SolidWorks 插件注册和原卸载信息到 `artifacts\backup\时间戳`，并核对 SHA256。

关闭 SolidWorks 后运行安装程序，接受 Windows UAC 提升。安装程序从 Release 输出复制 DLL/资源并使用 64 位 Framework RegAsm 注册。安装后启动 SolidWorks，在“工具 → 插件”加载 SolidWorks to URDF Exporter，再导出装配体，确认每个 STL 旁存在 STEP。

如需回滚，关闭 SolidWorks，用管理员 PowerShell 执行备份目录内的 `Restore-InstalledPlugin.ps1`。它先核对备份哈希，再恢复原文件和注册信息，包括原卸载程序。请保留整个备份目录。
