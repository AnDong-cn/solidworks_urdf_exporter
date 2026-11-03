# 本机验证记录（2026-11-03）

- 上游：`882169e28952f0d17c87d7eab98826454421aabf`。
- 插件源码及安装包：`37ac0b0`，产品版本 `1.6.1-16-g37ac0b0-step.1`。
- 环境：Windows x64、SolidWorks 2025 SP0.0、VS2022 Community 17.14.31、.NET Framework 4.8，Release x64。
- 已补齐 VS2022 的 .NET 桌面开发、.NET Framework 4.8 SDK 和目标包，并安装系统 .NET SDK 9.0.313。强制使用系统 4.8 目标包重新编译插件成功，系统 SDK 编译测试运行器成功；此验证使用独立输出目录，未改变已安装的插件 DLL。

## 结果

| 检查 | 结果 |
| --- | --- |
| 未修改上游的 SolidWorks 连接及三个示例打开测试 | 4 项通过 |
| 最终 Release 的 STEP 集成及上游回归测试 | 13 项通过，0 失败、0 跳过 |
| 安装后的插件加载及 DLL 路径 | 成功，指向 Program Files 中的 URDFExporter |
| 使用安装版本导出并重新导入三个示例的 STEP | 3 项通过，0 失败、0 跳过 |
| 没有测试匹配筛选条件 | 测试运行器返回退出码 1，避免误报成功 |

非对称单零件覆盖两种朝向；STEP/STL 最大包围盒差值为 `5.01×10⁻⁹ m`。平移、旋转、重复实例、多实体及子装配体组成的 link 重新导入后恰好有 6 个实体，最大包围盒差值为 `2.51×10⁻⁸ m`。两类平面模型的全部 CAD 角点均能在 STL 顶点中找到，误差不超过 `10⁻⁵ m`。

三个曲面示例使用上游粗 STL 网格，STEP/STL 最大包围盒差值约 `1.71×10⁻⁴ m`，低于曲面示例的 `5×10⁻⁴ m` 容差。此差异来自 STL 离散；STEP 仍保留原始 CAD 曲面。

与未修改上游比较，三个示例的全部 STL 三角形在按几何排序后差值为 0。CSV 和 URDF 的文本/结构一致，数值通过 `10⁻¹⁰×max(1, |原值|)` 的比较，覆盖质量、惯量和关节变换；只排除 URDF 顶部版本注释。

文件被占用的故障测试确认旧 STEP 保留、临时文件清理、其他 link 继续导出、CSV/URDF 正常生成、一次集中提示、失败写入日志，以及设置和可见性恢复。另验证了选择列表恢复、3DXML、不导出 mesh、参考 frame 不生成 STEP。

## 安装与交付文件

安装程序正常退出（退出码 0），64 位 COM 注册成功。安装 DLL 与经过测试的 Release DLL 的 SHA256 一致：

```text
40A529DE14513EE44DBCCE31D3C90F4E06E5124282F7BF9847ABFDD9C8E95635
```

安装包 `artifacts/installer/sw2urdfSetup-step.exe` 的 SHA256：

```text
48A75D41F8A73102821355A13302008EFA08DB2F142C4FCB425C151FA6CB1C94
```

原版本 `1.6.0-4-g7f85cfe` 的目录与注册信息备份位于 `artifacts/backup/20261103-144140`，备份文件逐一核对了哈希；回滚方法见 [STEP-EXPORT.md](STEP-EXPORT.md)。未实际执行回滚，以保留已安装的新版本。

本机完整日志保存在 `artifacts/final-validation.log`、`artifacts/plugin-install.log`、`artifacts/installed-validation.log`，三个示例输出在 `artifacts/test-output`，非对称验证模型在 `artifacts/validation-model`。这些本地二进制和日志不进入源码仓库。

安装后测试同时启动插件和外部测试进程，二者使用上游同一个日志路径，因此该轮原始日志有 log4net 文件争用信息；几何检查全部通过。单进程的最终 Release 测试验证了失败日志和集中提示。
