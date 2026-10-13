# Yohuke Danmu Neo

Yohuke Danmu Neo 是一个面向 B 站直播同传工作的桌面工具，基于 .NET、Avalonia 和 WebView 构建。它把直播监听、同传发送、歌词发送、弹幕转发、屏蔽词替换和多工作区管理集中在一个可配置的工作台里。


## 功能

- 多工作区与多直播间标签页管理。
- 按主播昵称搜索直播间，直接添加到指定工作区。
- B 站账号管理，支持全局、工作区、直播间和转发规则级账号选择。
- 直播弹幕监听、同传输入、发送历史记录和 Excel 导出。
- 弹幕在用户名前显示粉丝牌与等级，牌名最多展示三个字；点击 SC 或弹幕用户名可复制完整名字，点击正文可添加到同传输入框；有房管或主播权限时可右键禁言用户。
- 下翻历史弹幕时暂停自动回顶并保持阅读位置，返回顶部后恢复跟随。
- SC 列表以单行显示上舰记录，舰长为蓝色、提督为紫色、总督为橙色，点击上舰记录可复制完整用户名。
- 歌词库管理、歌词时间轴发送、快进快退微调和已发送去重。
- 直播播放器追帧、静音自动播放、音量控制和画面健康检查。
- 弹幕转发规则，支持来源直播间、发送人 UID、内容正则、开闭符号组和发送账号覆盖。
- 屏蔽词替换库，可同时作用于同传输入和歌词发送。
- 可配置快捷键、自动备份保留数量、缓存清理和配置导出。
- 默认按 Tab 轮切当前工作区的同传样式预设，在同传输入框内同样生效。

![](./Screenshots/1.png)  
![](./Screenshots/2.png)

## 环境

- .NET SDK 10.0 或更高版本。
- macOS、Windows 或 Linux 桌面环境。
- B 站账号 Cookie 用于发送弹幕和监听需要登录态的直播间。

## 开发

还原依赖：

```bash
dotnet restore Sources/Yohuke.DanmuNeo.sln
```

运行测试：

```bash
dotnet test Sources/Yohuke.DanmuNeo.sln --no-restore
```

启动应用：

```bash
dotnet run --project Sources/Yohuke.DanmuNeo/Yohuke.DanmuNeo.csproj
```

发布当前平台版本：

```bash
dotnet publish Sources/Yohuke.DanmuNeo/Yohuke.DanmuNeo.csproj -c Release
```

生成 Windows x64 EXE、macOS ARM64 App 和 DMG：

```powershell
pwsh -NoProfile -File ./Publish.ps1 -Clean
```

两端发布文件使用统一的 `Yohuke-Danmu-Neo-{版本}-{平台架构}` 命名格式，默认输出路径为：

- Windows：`Artifacts/Publish/win-x64/Yohuke-Danmu-Neo-{版本}-win-x64.exe`
- macOS：`Artifacts/Publish/Yohuke-Danmu-Neo-{版本}-osx-arm64.dmg`

版本默认读取项目的 `AssemblyVersion`，可通过 `-Version` 覆盖。

DMG 默认使用 ad-hoc 签名，内含应用、`Applications` 快捷方式和中文首次打开说明。如需使用 Developer ID 证书签名，可执行：

```powershell
pwsh -NoProfile -File ./Publish.ps1 -Clean -MacCodeSignIdentity "Developer ID Application: Example Company (TEAMID)"
```

脚本不会自动提交 Apple 公证。未公证版本在其他 Mac 上首次打开时，请按 DMG 内的《首次打开说明》处理。

## 数据目录

应用配置默认写入系统 ApplicationData 下的 `Yohuke.DanmuNeo` 目录。主要文件包括：

- `settings.json`：全局设置、快捷键、屏蔽词替换库和符号组。
- `accounts.json`：B 站账号配置。
- `workspaces.json`：工作区、直播间和转发规则。
- `lyric-library.json`：本地歌词库。
- `Backups/`：自动备份文件。
- `Corrupt/`：损坏配置的留存副本。

也可以在应用的“杂项”设置页打开配置目录、缓存目录或导出全部备份。

## 测试

测试项目位于 `Sources/Yohuke.DanmuNeo.Tests`，覆盖状态迁移、账号选择、播放器 HTML、歌词时间轴、屏蔽词替换、快捷键绑定、转发规则和历史导出等核心逻辑。提交前请至少执行：

```bash
dotnet test Sources/Yohuke.DanmuNeo.sln --no-restore
```
