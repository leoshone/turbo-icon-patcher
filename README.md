# Turbo 图标定制备份(turbo-icon-patcher)

为 Turbo 浏览器(Chromium 内核便携版)定制应用图标,并解决"三处图标源"问题的完整工具链。沉淀自 2026-10-13 实战:去除图标右上角黄色竖线(形似通知"1"),红管延长、出风口对称加高。

**给普通用户**:不用看懂原理,下载 Release 里的 zip,解压到 Turbo 目录,双击 bat 即可。本 README 即完整使用文档。

## 它解决什么问题

Chromium 浏览器图标有**三个独立来源**,只改 exe 是不够的:

1. `turbo.exe` RT_ICON —— 资源管理器里的 exe 图标
2. `User Data/<profile>/Google Profile.ico` —— 任务栏固定项(未运行时)
3. `<版本目录>/chrome.dll` 内嵌 PNG —— **运行中窗口/任务栏图标(真来源)**

升级浏览器或换电脑后图标回退,跑一遍本工具即可三处全部恢复。

---

## 一、下载

到 [Releases](https://github.com/leoshone/turbo-icon-patcher/releases) 下载 **`turbo-icon-patcher.zip`**。

- **无需安装任何东西**:只用 Windows 10/11 自带组件(PowerShell 5.1 + 系统自带编译器)
- 首次运行编译内嵌 C# 引擎,约 1 秒
- 入口文件:`patch_turbo_icon.bat`,双击即用

## 二、安装位置

解压出的 `icon-patcher` 文件夹必须放到 **Turbo 安装目录下**:

```
D:\Software\Turbo\            ← Turbo 安装目录(里面能看到 turbo.exe)
├── turbo.exe
├── 154.0.xxxx.xxx\           ← 版本目录
└── icon-patcher\             ← 补丁工具放这里
    ├── patch_turbo_icon.bat
    ├── scripts\
    └── assets\               ← edited_256.png 是图标母版
```

> 工具自动以"自己所在文件夹的上一级"作为 Turbo 目录,放对位置即无需任何配置。
> ⚠️ 路径不能含中文(建议保持 ASCII)。

## 三、日常使用

| 操作 | 怎么做 |
|---|---|
| **打补丁(最常用)** | 双击 `patch_turbo_icon.bat`。自动:杀掉 Turbo 后台进程 → 替换三处图标 → 刷新系统图标缓存 |
| **只预览不写入** | 命令行运行 `patch_turbo_icon.bat dry` |
| **验证运行窗口图标** | Turbo **开着**时,命令行运行 `patch_turbo_icon.bat verify` |

打完补丁**重启 Turbo** 生效。如果任务栏图标没变,依次尝试:

1. 任务栏右键 Turbo → 取消固定 → 重新固定
2. 任务管理器重启 `Windows 资源管理器`
3. 顽固情况重启电脑(系统图标缓存极端顽固时)

## 四、浏览器升级后(图标回退)

直接**再双击一次 bat**。工具按帧内容特征定位(不依赖硬编码偏移),已替换过的帧自动跳过,重复运行安全。

## 五、常见问题

| 现象 | 原因与处理 |
|---|---|
| `BeginUpdateResource failed` / 拒绝访问 | Turbo 没退净。任务管理器结束所有 `turbo.exe` 后重跑(bat 会自动杀,手动跑 ps1 时需自己杀) |
| 图标某处没变 | 九成是系统图标缓存:取消固定重固定 / 重启 explorer |
| exe 文件属性里签名失效 | 正常现象:改资源必然破坏签名,功能不受影响 |
| 想换成别的图标设计 | 用新的 256×256 PNG 替换 `assets\edited_256.png`(当前内置:去黄竖线版) |
| exe 是新图标但 chrome.dll 补不上 | exe 曾被改过、失去旧帧定位基准,直接找 AI 重新生成基准 |

## 六、安全说明

- 工具只写图标资源,不动浏览器功能代码;chrome.dll 采用**等长替换**,文件总大小与 PE 结构不变
- 全程本地运行,无网络请求
- exe 数字签名因资源修改失效,属预期现象

---

## 给开发者/AI Agent

- **原理与方法论**:见 [`SKILL.md`](SKILL.md)(三图标源根因、坑位清单、可作为 AI Skill 安装)
- **排障细节**:见 [`references/pitfalls.md`](references/pitfalls.md) 与 [`references/rootcause.md`](references/rootcause.md)
- **实现**:`scripts/IconPatcher.cs`(C# 引擎,PowerShell `Add-Type` 运行时用系统自带 csc 编译,C# 5 语法)+ `scripts/patch_turbo_icon.ps1`(编排)。`scripts/patch_icon.py` 与 `verify_live_icon.py` 为早期 Python 实现,保留作开发测试参考,发行包不包含
- **历史版本说明**:各版本变更见对应 Release 页面;README 始终对应当前最新版的用法

## 仓库结构

```
├── README.md                        # 本文件(完整使用文档)
├── SKILL.md                         # AI Agent Skill 定义(方法论沉淀)
├── patch_turbo_icon.bat             # 入口(即发行包内同名文件)
├── scripts/
│   ├── IconPatcher.cs               # C# 引擎(Win32 资源 API / 等长 PNG / ICO 组装)
│   ├── patch_turbo_icon.ps1         # 编排层
│   ├── patch_icon.py                # 早期 Python 实现(开发测试参考,发行包不含)
│   └── verify_live_icon.py          # 运行窗口图标验证(Python 版,同上)
├── assets/                          # 工具运行所需
│   ├── edited_256.png               # 图标母版(256×256,去黄竖线版)
│   ├── old_frames_turbo154.json     # 官方原版 8 帧基准(md5/定位特征)
│   ├── old_frames_turbo154.md       # 偏移与几何基准记录
│   └── upstream/                    # 官方原版各尺寸图标(对照用)
└── references/                      # 开发者资料(运行不需要)
    ├── rootcause.md                 # 三图标源根因分析全记录
    ├── pitfalls.md                  # 排障坑位清单
    └── evidence/                    # 历次实测取证图(前后对比/活窗口验证)
```
