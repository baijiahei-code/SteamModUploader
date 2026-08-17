# Steam MOD 上传工具

[![Release](https://img.shields.io/github/v/release/baijiahei-code/SteamModUploader?style=flat-square&label=Release&color=blue)](https://github.com/baijiahei-code/SteamModUploader/releases)
[![Downloads](https://img.shields.io/github/downloads/baijiahei-code/SteamModUploader/total?style=flat-square&label=Downloads)](https://github.com/baijiahei-code/SteamModUploader/releases)
[![License](https://img.shields.io/github/license/baijiahei-code/SteamModUploader?style=flat-square&label=License&color=green)](https://github.com/baijiahei-code/SteamModUploader/blob/main/LICENSE)

一个基于 WPF（.NET 9）的图形化工具，用于自动生成 `mod.vdf` 并调用 `steamcmd` 上传 / 更新
Steam 创意工坊 MOD，替代手动编写 VDF 和批处理脚本。

> 本项目以 **GNU GPL v3** 协议开源，详见 [LICENSE](LICENSE)。

## 功能

- 📝 **MOD 信息管理**：标题、AppID、可见性、版本 / 更新说明等一键填写
- 🗂️ **多 MOD 项目管理**：可保存多个 MOD 配置，支持新建 / 复制 / 删除 / 导入
  （复制时自动清空内容/预览路径，避免两个 MOD 误传同一内容）
- 📁 **全局文件管理**：独立窗口，以根目录为视角统一管理所有 MOD 文件
  （content / preview / backup / output），内置导入、备份恢复、迁移、打包
- 🖼️ **预览图实时预览**：填写或导入预览图路径后立即预览；文件管理导入预览图
  会自动同步到配置的 PreviewFile
- 🧳 **迁移向导**：一键把已有 MOD 文件夹迁移到统一目录结构（复制或移动）
- 📦 **一键打包发布版 zip**：把内容打包为可分发 zip（自动带版本号）
- 🚀 **一键上传 / 更新**：自动生成 `mod.vdf` 并调用 `steamcmd +workshop_build_item`
- 🛡️ **上传前校验**：标题 / AppID / 更新说明必填、内容文件夹非空、预览图格式（jpg/png）
  与大小（≤1MB）校验，并显示上传内容文件数与大小
- 🔄 **自动识别 PublishedFileID**：首次上传成功后自动填入，之后即可直接更新
- 🔐 **Steam Guard 支持**：登录需要验证码时自动弹出输入框
- 📜 **实时日志 + 落盘**：显示 steamcmd 完整输出与进度；日志自动写入
  `%APPDATA%\SteamModUploader\logs\`，可一键导出
- 🩺 **启动环境体检**：启动时提示 steamcmd 路径 / MOD 根目录是否有效
- 🔍 **VDF 智能解析**：导入已有 `mod.vdf` 时支持转义引号

## 使用步骤

1. **构建**：
   ```
   dotnet build SteamModUploader.slnx
   ```
   程序输出到 `SteamModUploader\bin\Debug\net9.0-windows\SteamModUploader.exe`

2. **首次配置**：
   - 在底部设置栏填写 `steamcmd.exe` 路径、Steam 用户名和密码，点击「保存设置」
   - 密码使用 **Windows DPAPI（当前用户）加密**后保存在
     `%APPDATA%\SteamModUploader\settings.json`，磁盘上不保存明文，且只有当前 Windows
     用户能解密
   - 设置栏的 **「清除缓存」** 按钮：清除 steamcmd 的缓存登录凭据（`config.vdf`）。
     当**修改 Steam 密码后上传报 `Access Denied`** 时使用；清除前自动备份为
     `config.vdf.bak`，清除后下次上传需重新登录（可能要求输入 Steam Guard 令牌码）
   - 设置栏的 **「修复路径」** 按钮：移动项目文件夹（如把 `D:\SteamMOD` 挪到其他盘）
     后，旧路径会失效。点击它 → 软件检测失效路径 → 选择新的 MOD 根目录 →
     一键批量替换所有根目录/内容文件夹/预览图/VDF 路径（并顺带更新 VDF 文件内路径）

3. **新建或导入 MOD**：
   - 点击「新建」手动填写，或「导入」直接读取已有的 `mod.vdf`（例如 `Sample\mod.vdf`）

4. **填写 MOD 信息**：
   - 标题、AppID（默认 2868840）、可见性、版本/更新说明
   - 内容文件夹：选择 MOD 文件所在目录（如 `Sample\paks`）
   - 预览图：可选，选择一张图片作为创意工坊封面（界面实时预览）
   - PublishedFileID：**首次上传留空**；更新已有 MOD 时填写

5. **上传**：
   - 点击「🚀 立即上传」，等待 steamcmd 完成
   - 若弹出 Steam Guard 验证码输入框，输入手机令牌 / 邮箱验证码即可
   - 首次上传成功后，软件会自动识别并填入 PublishedFileID，之后可继续用同一配置更新

## 全局文件管理

在主窗口右上角点击 **「📁 文件管理（全局）」** 打开独立的全局文件管理窗口。
在这里设置 **MOD 文件根目录**（全局设置），左侧列出根目录下所有 MOD，右侧管理其文件：

```
<根目录>/
└── <MOD名称>/
    ├── content/    # 上传内容（对应 contentfolder）
    ├── preview/    # 预览图（导入后自动填入 previewfile）
    ├── backup/     # 版本备份（zip）
    └── output/     # 生成的 workshopitem.vdf 与发布版 zip
```

- **新建 MOD…**：左侧列表顶部按钮，输入名称即创建文件夹并自动建好标准目录结构
- **创建标准目录结构**：对选中的 MOD 补全缺失的目录（已完整时会明确提示）
- **导入内容文件**：多选文件复制到 `content/`
- **导入预览图**：复制到 `preview/`，界面实时预览
- **迁移向导**：把已有 MOD 文件夹（如 `Sample/paks`）整体复制/移动到 `content/`，
  可选一并导入预览图
- **打包发布版 zip**：把 `content/` 打包到 `output/`，文件名自动带版本号
  （如 `MOD名_v1.2.0.zip`），用于分发
- **上传前自动备份**（全局开关，默认开启）：每次上传前把当前内容打包到 `backup/`
- **版本备份**：手动创建 / 恢复 / 删除备份 zip
- **上传前校验**：内容文件夹为空会阻止上传并提示
- **上传后清理**：临时 VDF 自动删除（自定义 VDF 路径则保留）

## 项目结构

```
SteamModUploader/
├── App.xaml / App.xaml.cs          # 应用入口
├── Views/                          # 所有窗口 / 对话框
│   ├── MainWindow.xaml(.cs)        # 主界面与逻辑
│   ├── FileManagerWindow.xaml(.cs) # 全局文件管理窗口
│   ├── GuardCodeDialog.xaml(.cs)   # Steam Guard 验证码输入框
│   ├── MigrateDialog.xaml(.cs)     # 迁移向导（导入已有文件夹）
│   └── PromptDialog.xaml(.cs)      # 通用输入对话框
├── Models/
│   ├── ModProfile.cs               # 单个 MOD 配置模型
│   └── AppSettings.cs              # 全局设置模型
└── Services/
    ├── SettingsService.cs          # 配置持久化（JSON + DPAPI）
    ├── VdfGenerator.cs             # 生成 workshopitem VDF
    ├── VdfParser.cs                # 解析/导入已有 VDF
    ├── FileManager.cs              # 统一文件管理（目录结构/导入/备份/打包）
    └── SteamCmdRunner.cs           # 启动 steamcmd 并捕获输出
```

## 发布 / 打包

**1. 单文件绿色版（免安装，双击即用）**
```
dotnet publish SteamModUploader\SteamModUploader.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o publish\win-x64
```
生成 `publish\win-x64\SteamModUploader.exe`（约 60MB，含 .NET 运行时，无需安装依赖）。

**2. 安装程序（setup.exe，带开始菜单/桌面快捷方式/卸载）**
- 需要先安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)
- 运行 `installer\编译安装程序.bat`，或手动：`ISCC.exe installer\installer.iss`
- 生成 `publish\SteamModUploader-Setup.exe`（中文安装向导）
- 若报缺少中文语言文件，从 issrc 仓库下载 `ChineseSimplified.isl` 放入
  Inno Setup 的 `Languages\` 目录后重试

## 安全说明

- **密码落盘加密**：密码使用 Windows DPAPI（当前用户）+ **应用专属熵**加密后保存，
  磁盘上无明文；换用户或换机器后需重新输入。不兼容旧版本（无熵）保存的密文，
  升级后旧密码需重新填写并保存一次。
- **密码不出现于命令行**：调用 `steamcmd` 时密码通过**标准输入**传递，
  `+login <用户名>` 不再附带密码参数，避免被任务管理器 / `wmic` 等工具读取进程命令行。
- **日志脱敏**：日志输出会隐藏密码（替换为 `***`），并按“独立词边界”匹配，
  即使密码很短（如 `1`）也不会误伤正常文本；带引号 / 空格包裹的密码形式同样会被隐藏。
- **DPAPI 局限性**：加密只防止“直接读文件”看到密码；同一 Windows 用户下的恶意程序
  仍可能解密——这是 Windows 平台的固有边界（应用专属熵只能提高门槛，不能根除）。
- **清除缓存后的备份含凭据**：「清除缓存」会把旧 `config.vdf` 备份为 `config.vdf.bak`
  以防误删；该备份**仍含旧登录凭据**，清除成功后会询问是否一并删除以彻底清除。
- **steamcmd 自身**会在安装目录缓存登录信息（`config.vdf`、`logs/` 等），本工具无法控制；
  这些文件同样可能包含登录相关凭据，请勿公开或分享 steamcmd 安装目录。
- **内容文件夹会整体上传**：`contentfolder` 下的所有文件会递归上传到创意工坊（公开时
  即公之于众）。请务必只放 MOD 相关文件，不要放入私密文件。
- **不要分享配置**：`%APPDATA%\SteamModUploader\settings.json` 含用户名与加密密码，
  不要分享或提交到代码仓库。

## 说明

- 本工具只负责调用 `steamcmd`，账号密码等信息请通过官方渠道确认安全性。
- 上传需要先拥有对应游戏的开发者 / 创意工坊权限。
- `Sample/` 目录包含一个示例 MOD 结构，可参考其 `mod.vdf` 的字段含义。

## 许可证

本项目使用 [GNU General Public License v3.0](LICENSE) 开源。你可以自由使用、修改和分发
本软件，但任何衍生作品也必须以 GPL v3 协议开源。
