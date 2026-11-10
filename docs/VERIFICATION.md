# 验证矩阵

本页区分本次源码整理后的重新验证，以及此前已保留日志的功能验证。通过测试表示所列输入和环境下的结果，不能直接推导现场检出率或长期可靠性。

## 2026-11-10 整理后重新验证

环境：Windows x64，VS 2022 Community，.NET Framework 4.7.2，官方 VisionMaster 4.3.0 与 MVS SDK。使用独立 Git 工作副本，未引用原工程 bin/obj 产物。

| 验证 | 入口 | 结果 |
| --- | --- | --- |
| 七个工程 Release/x64 全量重建 | `scripts/Build.ps1` | 通过 |
| 核心服务 | `scripts/Test-Core.ps1` | 11 项通过 |
| 通讯基础 | `src/VisionInspect.Communication/Tests/Run-Regression.ps1` | 8 个场景通过 |
| 源码提交内容 | `scripts/Check-Repository.ps1` | 排除路径、文件类型及常见凭据格式检查通过 |

核心回归包含暂停/停止/取消、保留已返回结果、图像行跨度与格式转换、有界归档、并发日志读取、损坏用户库处理、历史报表及内存预算。通讯基础使用本机测试端点，不连接真实 PLC。

GitHub Actions 配置执行仓库检查及两组基础回归。工作流已添加；远端执行结果应以 GitHub Actions 页面为准，不将本地成功表述为远端 CI 已通过。

## 2026-11-09 已有功能验证记录

| 范围 | 实际条件 | 记录结果 |
| --- | --- | --- |
| 主窗体与主题 | 真实 VM 控件，连续 20 次日夜切换，透明背景像素和原生句柄检查 | 通过 |
| 检测操作 | 官方 CircleFind 方案副本，本地图片；加载、单次、连续、暂停、继续、停止和释放 | 通过 |
| 布局与子窗体 | 1366/1560 客户区宽度；设置、方案管理、登录和报表两种主题 | 通过 |
| 通讯及热配置 | 本机模拟 Modbus FC03/FC16、TCP 主动/监听、真实设置窗体应用、通道隔离、保存失败回滚 | 通过 |
| 数据切换 | 真实归档文件、目录、日志、报表元数据、保留历史和清理时机 | 通过 |
| 独立 SDK | USB 授权，官方方案加载、注图、运行、结果、保存、并发卸载及释放 | 通过 |
| 授权方式脚本 | 模拟目录的确认保护、WhatIf、备份及歧义配置拒绝 | 通过；真实 VM 授权配置未改写 |

保留日志的独立 SDK 关键输出：

```text
PASS read-only diagnostics / soft selector is not activation / actionable errors preserve original codes
PASS real SDK load / image input / run / outputs
PASS independent SDK consumer without GUI executable / orderly disposal
```

20 次主题切换样本平均过渡 238.6 ms、最长 265.6 ms；单次配色更新最大 9.6 ms，UI 心跳最大间隔 90.6 ms。后两者口径不同，不能据此声称固定显示帧率或所有负载下零卡顿。

## 尚未完成的验收

- 实物工业相机、现场 PLC/串口设备的完整联调；当前串口仅有故障隔离验证。
- 用户全部配方、跨显示器 DPI 和 7×24 小时运行。
- 官方软授权及拔除 USB 后实际执行 `.sol`。
- 生产样本量、漏检率、误检率和测量不确定度的专项评估。

## 进一步复现

```powershell
.\scripts\Test-RuntimeSettings.ps1
.\scripts\Test-ThemeOverlay.ps1
.\scripts\Test-UiRedesign.ps1
.\scripts\Test-SdkRuntime.ps1
.\scripts\Test-DeploymentTools.ps1
```

上述集成入口需要安装本机官方 SDK；部分历史辅助脚本按 VS 2022 Community 编译器路径查找。算法验证另需有效授权。测试在隔离目录使用官方方案副本，生成结果在 artifacts 或系统临时目录，不纳入源码仓库。
