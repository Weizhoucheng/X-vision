# 构建与部署

## 环境

Windows x64；VS 2022 .NET 桌面开发；.NET Framework 4.7.2 开发包；官方 VisionMaster 4.3.0 SDK；匹配的 MVS SDK/相机驱动。VM 运行必须具备覆盖目标版本和算法的官方许可。

项目通过 `Directory.Build.props` 使用 `VisionMasterRoot` 和 `MvsSdkDir`，没有将第三方 DLL 提交到 Git。使用 SDK 安装目录，而不是自行从网络寻找单个 DLL。

```powershell
.\scripts\Check-Environment.ps1
.\scripts\Build.ps1 -Configuration Release -VisionMasterRoot 'D:\Program Files\VisionMaster4.3.0'
```

如使用其他相机 SDK 路径，传入 `-MvsSdkDir`；该路径可以是包含 `Algorithms` / `Common` 的 ReferencedAssemblies，也可以是三个托管库所在的同一目录。解决方案强制 x64，目标框架为 v4.7.2。

## 首次运行

```powershell
& '.\src\VisionInspect.App\bin\Release\XVision.exe' '--data=D:\XVisionData'
```

默认数据位置为 `%ProgramData%\VisionInspect`。指定 `--data` 可隔离测试数据。应用从该目录的 `config` 子目录读取或创建 `appsettings.json`；需要预设时复制 `config/appsettings.example.json` 并调整 VM 与存储路径。

配置示例中的地址仅为示例，全部通讯默认关闭。不要将现场配置覆盖进仓库示例。演示账号及当前密码保存机制见仓库根目录 `SECURITY.md`。

## 发布应用及 SDK

```powershell
.\scripts\Publish.ps1 -Configuration Release -OutputDirectory 'D:\XVisionDelivery\App'
.\scripts\Pack-XVisionSdk.ps1 -OutputDirectory 'D:\XVisionDelivery\SDK'
```

输出必须为新目录；脚本不会清空已有数据。生成的运行交付包可能含随构建复制的第三方依赖，供内部部署验证使用；发布或分发前应核对供应商再分发条款，不要直接作为本仓库公开 Release 附件。

## 运行环境与授权

主界面“运行环境”或 `XVision.exe --runtime-check` 进行只读诊断。`deployment/Install-XVisionRuntime.ps1` 默认检查依赖，显式安装仅使用官方安装文件。诊断文件齐全不代表许可有效。

`Set-VmAuthorizationMode.ps1` 只选择官方已有授权的校验方式，必须已获得许可并显式确认。它不会生成授权或转换 USB 许可；不要因本项目是二次开发而删除授权驱动。

## 故障排查

加载 `.sol` 失败时保留原始错误码，并检查 VM 版本、方案依赖算法和有效授权。图像与结果不一致时先确认图像源、渲染模块和输出判定配置。通讯连接失败时查看 RX/TX 与连接状态，核对协议、地址、端口、防火墙和串口占用。更换存储路径后旧报表仍在原目录，软件不会自动合并历史。

现场验收至少覆盖目标方案 OK/NG 样本、参数保存回读、设备掉线重连、真实相机触发、PLC 寄存器含义、磁盘与归档容量、长时间运行以及重启恢复。
