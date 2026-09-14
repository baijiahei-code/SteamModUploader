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
  （可指定 VDF 输出目录，默认写临时文件并在上传后自动清理）
- 📶 **上传进度**：实时显示 steamcmd 的上传百分比，长传不再“看不出在不在动”
- 🧾 **完整 VDF 字段**：包含 `appid / publishedfileid / contentfolder / previewfile /
  visibility / title / description / changenote`；留空的字段一律不写入 VDF，
  所以不会把你在 Steam 网页上单独改过的简介覆盖成空白
- ✅ **明确的上传结论**：不只看退出码，根据输出判定“成功 / 失败 / 结果不明”，
  并在失败时自动附上 steamcmd 的 `depot_build_<appid>.log` 末尾内容（错误原因常写在那里）
- 💾 **成功后自动保存配置**：识别到的 PublishedFileID 会立即落盘，不怕忘了点保存
- 🔓 **新建项目引导**：首次上传成功后可直接打开创意工坊项目页面，完成法律协议确认与简介/标签补充
- ⏱️ **卡死自动结束**：steamcmd 长时间无任何输出会自动结束进程并提示，不会一直挂着
- 🛡️ **上传前校验**：标题 / AppID / 更新说明必填、内容文件夹非空、预览图格式（jpg/png）
  与大小（≤1MB）校验，并显示上传内容文件数与大小
- 🔄 **自动识别 PublishedFileID**：首次上传成功后自动填入，之后即可直接更新
- 🔐 **Steam Guard 支持**：登录需要验证码时自动弹出输入框
- 📜 **实时日志 + 落盘**：显示 steamcmd 完整输出与进度；日志自动写入
  `%APPDATA%\SteamModUploader\logs\`，可一键导出
- 🩺 **启动环境体检**：启动时提示 steamcmd 路径 / MOD 根目录是否有效
- 🔍 **VDF 智能解析**：导入已有 `mod.vdf` 时支持转义引号，且保留路径中的反斜杠
  （`D:\new` 不会被错误还原成 `D:new`）
- 📜 **日志自动清理**：`%APPDATA%\SteamModUploader\logs\` 下的日志按天分文件，超过 30 天自动删除

## 使用步骤

1. **构建**：
   ```
   dotnet build SteamModUploader.slnx
   ```
   程序输出到 `SteamModUploader\bin\Debug\net9.0-windows\SteamModUploader.exe`

2. **首次配置**：
   - 软件会**自动探测**常见的 steamcmd 安装位置（绿色版同目录、各盘符下的 `steamcmd\`）；
     没探测到就在底部设置栏手动填写 `steamcmd.exe` 路径、Steam 用户名和密码，点击「保存设置」
   - 密码使用 **Windows DPAPI（当前用户）加密**后保存在
     `%APPDATA%\SteamModUploader\settings.json`，磁盘上不保存明文，且只有当前 Windows
     用户能解密
   - 设置栏的 **「清除缓存」** 按钮：清除 steamcmd 的缓存登录凭据（`config.vdf`）。
     当**修改 Steam 密码后上传报 `Access Denied`** 时使用；清除前自动备份为
     `config.vdf.bak`，清除后下次上传需重新登录（可能要求输入 Steam Guard 令牌码）
   - 设置栏的 **「修复路径」** 按钮：移动项目文件夹（如把 `D:\SteamMOD` 挪到其他盘）
     后，旧路径会失效。点击它 → 软件检测失效路径 → 选择新的 MOD 根目录 →
     一键批量替换所有根目录/内容文件夹/预览图/VDF 输出目录（并顺带更新已生成 VDF 内的路径）

3. **新建或导入 MOD**：
   - 点击「新建」手动填写，或「导入」直接读取已有的 `mod.vdf` 文件
   - 已设置 MOD 文件根目录时，新建会**自动**建立 `content / preview / backup / output` 并填好：
     内容文件夹、**VDF 输出目录（`output`）**、**预览图路径**（有图就用已有的，没有则先填好
     `<MOD>\preview\preview.png`，把图放进去即自动生效；若你放的是别的文件名，
     重新选中该 MOD 会自动识别并纠正为实际文件）

4. **填写 MOD 信息**：
   - 标题、AppID（新建时留空，上传前填写）、可见性、版本/更新说明
   - 内容文件夹：选择 MOD 文件所在目录
   - 预览图：可选，选择一张图片作为创意工坊封面（界面实时预览）
   - PublishedFileID：**首次上传留空**；更新已有 MOD 时填写

5. **上传**：
   - 点击「🚀 立即上传」，等待 steamcmd 完成（进度条会显示上传百分比）
   - 若弹出 Steam Guard 验证码输入框，输入手机令牌 / 邮箱验证码即可
   - 首次上传成功后，软件会自动识别并填入 PublishedFileID 并**立即保存配置**，
     之后可继续用同一配置更新
   - 新建的项目会询问是否打开创意工坊项目页面：在那里同意《创意工坊法律协议》、
     补充简介/标签/截图（未同意协议的项目对其他人不可见）
   - 「创意工坊简介」留空时不会写入 VDF，因此**不会覆盖**你在网页上修改过的简介；
     想用本地内容覆盖时，填入内容再上传即可
   - 失败时会自动附上 steamcmd 构建日志（`depot_build_<appid>.log`）的末尾内容；
     修改过 Steam 密码后报 `Access Denied` 时，用设置栏的「清除缓存」

## 全局文件管理

在主窗口右上角点击 **「📁 文件管理（全局）」** 打开独立的全局文件管理窗口。
在这里设置 **MOD 文件根目录**（全局设置），左侧列出根目录下所有 MOD，右侧管理其文件：

```
<根目录>/
└── <MOD名称>/
    ├── content/    # 上传内容（对应 contentfolder）
    ├── preview/    # 预览图（导入后自动填入 previewfile）
    ├── backup/     # 版本备份（zip）
    └── output/     # 发布的成品包（zip，见下方「打包发布版 zip」）
```

- **新建 MOD**：在主窗口左侧列表的「新建」按钮创建（会自动建好标准目录结构）；
  文件管理窗口顶部按钮也可以刷新列表，但配置的增删请在主窗口进行
- **创建标准目录结构**：对选中的 MOD 补全缺失的目录（已完整时会明确提示）
- **导入内容文件**：多选文件复制到 `content/`
- **导入预览图**：复制到 `preview/`，界面实时预览，并自动写入配置的 `previewfile`
- **迁移向导**：把已有 MOD 文件夹整体复制/移动到 `content/`，
  可选一并导入预览图
- **打包发布版 zip**：把 `content/` 打包到 `output/`，文件名自动带版本号
  （如 `MOD名_v1.2.0.zip`），用于分发
- **上传前自动备份**（全局开关，默认开启）：每次上传前把当前内容打包到 `backup/`
- **版本备份**：手动创建 / 恢复 / 删除备份 zip；恢复前会先校验压缩包完整性，
  校验不通过不会动现有内容
- **上传前校验**：内容文件夹为空会阻止上传并提示；
  内容文件夹若包含了 `backup` / `output`（即误选了整个 MOD 目录）会额外确认
- **忽略系统垃圾文件**：`Thumbs.db`、`desktop.ini`、`.DS_Store` 不会被上传、备份或迁移
- **耗时操作不卡界面**：导入 / 迁移 / 打包 / 备份 / 恢复都在后台线程执行，顶部会显示进度条
- **上传后清理**：临时 VDF 自动删除（指定了「VDF 输出目录」则保留该目录下的 `workshopitem.vdf`）
- **VDF 输出目录**：可选，**选的是文件夹**（默认填 MOD 的 `output`）；留空则用系统临时目录，
  且上传后自动删除，不会残留文件

## 项目结构

```
SteamModUploader/                   # 主程序
├── App.xaml / App.xaml.cs          # 应用入口、全局共享样式、未处理异常兜底
├── app.ico                         # 应用图标（可用 installer\make-icon.ps1 重新生成）
├── Views/                          # 所有窗口 / 对话框
│   ├── MainWindow.xaml(.cs)        # 主界面与逻辑
│   ├── FileManagerWindow.xaml(.cs) # 全局文件管理窗口
│   ├── LogPanel.cs                 # 日志框封装（追加/滚动/限行数）
│   ├── MigrateDialog.xaml(.cs)     # 迁移向导（导入已有文件夹）
│   └── PromptDialog.xaml(.cs)      # 通用输入对话框（也用于 Steam Guard 验证码）
├── Models/
│   ├── ModProfile.cs               # 单个 MOD 配置模型（含可见性枚举）
│   └── AppSettings.cs              # 全局设置模型
└── Services/
    ├── SettingsService.cs          # 配置持久化（JSON + DPAPI，原子写入 + .prev 备份）
    ├── WorkshopUploader.cs         # 上传流程封装（参数 / 验证码 / 结果解析）
    ├── WorkshopOutputParser.cs     # 从 steamcmd 输出解析 PublishedFileID
    ├── SteamCmdRunner.cs           # 启动 steamcmd、捕获输出、按需注入密码
    ├── SteamCmdLocator.cs          # 自动探测 steamcmd 安装位置
    ├── VdfGenerator.cs             # 生成 workshopitem VDF
    ├── VdfParser.cs                # 解析/导入已有 VDF
    ├── FileManager.cs              # 统一文件管理（目录结构/导入/备份/打包/安全解压）
    └── Logger.cs                   # 日志落盘（缓冲 + 自动清理）

SteamModUploader.Tests/             # 单元测试（xunit）
├── VdfTests.cs                     # VDF 生成/解析、上传输出解析
├── FileManagerTests.cs             # 命名清洗、备份/恢复、zip-slip 防护
└── SettingsServiceTests.cs         # 配置加密与脏数据容错
```

## 开发

```
dotnet build SteamModUploader.slnx          # 编译（含测试项目）
dotnet test  SteamModUploader.Tests         # 运行单元测试
```

- 代码风格与静态分析规则见 `.editorconfig` 与 `Directory.Build.props`（当前编译零告警）。
- 应用图标由 `installer\make-icon.ps1` 生成（多尺寸 PNG 打包进 `app.ico`）。
- 版本号只在 `SteamModUploader.csproj` 的 `<Version>` 里维护：主界面标题栏读取程序集版本，
  `一键打包.bat` 也会把该版本号传给 Inno Setup。

## 发布 / 打包

0. **一键完成**：直接运行根目录的 `一键打包.bat`，会自动（使用 csproj 中的版本号，
   并先清理旧的 `publish\win-x64` 以免残留过期文件）：

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
  磁盘上无明文；换用户或换机器后需重新输入。
- **密码不出现于命令行**：调用 `steamcmd` 时密码通过**标准输入**按需传递
  （仅在 steamcmd 确实询问密码时写入；若命中缓存登录则完全不写，
  避免残留的密码被后续的 Steam Guard 提示误读），`+login <用户名>` 不再附带密码参数，
  避免被任务管理器 / `wmic` 等工具读取进程命令行。
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

## 许可证

本项目使用 [GNU General Public License v3.0](LICENSE) 开源。你可以自由使用、修改和分发
本软件，但任何衍生作品也必须以 GPL v3 协议开源。
