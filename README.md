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

## 一、下载哪个包

到 [Releases](https://github.com/leoshone/turbo-icon-patcher/releases) 下载,**二选一**:

| | `turbo-icon-patcher.zip`(推荐) | `turbo-icon-patcher-py.zip` |
|---|---|---|
| 需要安装什么 | **什么都不装**(用 Win10/11 自带组件) | 电脑上需有 Python 3.8+ |
| 首次运行 | 编译内嵌 C# 引擎,约 1 秒 | 自动创建 .venv 并装依赖,约 30 秒(需联网) |
| 入口文件 | `patch_turbo_icon_portable.bat` | `patch_turbo_icon.bat` |
| 适合场景 | 换新电脑、给别人的机器用、不想碰 Python | 已有 Python 环境、想改脚本逻辑 |

两包用法完全一样。下文以 `turbo-icon-patcher.zip` 为例,py 版把入口文件名替换即可。

## 二、安装位置

解压出的 `icon-patcher` 文件夹必须放到 **Turbo 安装目录下**:

```
D:\Software\Turbo\            ← Turbo 安装目录(里面能看到 turbo.exe)
├── turbo.exe
├── 154.0.xxxx.xxx\           ← 版本目录
└── icon-patcher\             ← 补丁工具放这里
    ├── patch_turbo_icon_portable.bat
    ├── scripts\
    └── assets\               ← edited_256.png 是图标母版
```

> 工具自动以"自己所在文件夹的上一级"作为 Turbo 目录,放对位置即无需任何配置。
> ⚠️ 路径不能含中文(建议保持 ASCII)。

## 三、日常使用

| 操作 | 怎么做 |
|---|---|
| **打补丁(最常用)** | 双击 `patch_turbo_icon_portable.bat`。自动:杀掉 Turbo 后台进程 → 替换三处图标 → 刷新系统图标缓存 |
| **只预览不写入** | 命令行运行 `patch_turbo_icon_portable.bat dry`(py 版:命令行跑 `python scripts/patch_icon.py --new-icon assets/edited_256.png --root <Turbo目录>`,不加 `--apply` 即预览) |
| **验证运行窗口图标** | Turbo **开着**时,命令行运行 `patch_turbo_icon_portable.bat verify`(py 版:`patch_turbo_icon.bat verify`) |

打完补丁**重启 Turbo** 生效。如果任务栏图标没变,依次尝试:

1. 任务栏右键 Turbo → 取消固定 → 重新固定
2. 任务管理器重启 `Windows 资源管理器`
3. 顽固情况重启电脑(系统图标缓存极端顽固时)

## 四、浏览器升级后(图标回退)

直接**再双击一次 bat**。工具按帧内容特征定位(不依赖硬编码偏移),已替换过的帧自动跳过,重复运行安全。

## 五、常见问题

| 现象 | 原因与处理 |
|---|---|
| `BeginUpdateResource failed` / 拒绝访问 | Turbo 没退净。任务管理器结束所有 `turbo.exe` 后重跑(bat 会自动杀,手动跑 ps1/py 时需自己杀) |
| 图标某处没变 | 九成是系统图标缓存:取消固定重固定 / 重启 explorer |
| 换新电脑报 Python 相关错误 | 用 `turbo-icon-patcher.zip`(零依赖版);或删除 `icon-patcher\.venv` 后重跑(py 版自动重建环境) |
| exe 文件属性里签名失效 | 正常现象:改资源必然破坏签名,功能不受影响 |
| 想换成别的图标设计 | 用新的 256×256 PNG 替换 `assets\edited_256.png`(当前内置:去黄竖线版) |
| exe 是新图标但 chrome.dll 补不上 | exe 曾被改过、失去旧帧定位基准。py 版用 `--reference-exe` 指向旧 exe 备份;portable 版直接找 AI 重新生成基准 |

## 六、安全说明

- 工具只写图标资源,不动浏览器功能代码;chrome.dll 采用**等长替换**,文件总大小与 PE 结构不变
- 全程本地运行,无网络请求
- exe 数字签名因资源修改失效,属预期现象

---

## 给开发者/AI Agent

- **原理与方法论**:见 [`SKILL.md`](SKILL.md)(三图标源根因、坑位清单、可作为 AI Skill 安装)
- **排障细节**:见 [`references/pitfalls.md`](references/pitfalls.md) 与 [`references/rootcause.md`](references/rootcause.md)
- **两个版本的实现**:
  - `turbo-icon-patcher.zip` ← `scripts/IconPatcher.cs`(C# 引擎,PowerShell `Add-Type` 运行时用系统自带 csc 编译,C# 5 语法)+ `scripts/patch_turbo_icon_portable.ps1`(编排)
  - `turbo-icon-patcher-py.zip` ← `scripts/patch_icon.py`(PIL + pefile + numpy,venv 自愈式 bat 入口)
- **历史版本说明**:各版本变更见对应 Release 页面;README 始终对应当前最新版的用法

## 仓库结构

```
├── README.md                        # 本文件(完整使用文档)
├── SKILL.md                         # AI Agent Skill 定义(方法论沉淀)
├── patch_turbo_icon_portable.bat    # 入口:零依赖版
├── patch_turbo_icon.bat             # 入口:Python 版(本机安装形态)
├── scripts/
│   ├── IconPatcher.cs               # C# 引擎(Win32 资源 API / 等长 PNG / ICO 组装)
│   ├── patch_turbo_icon_portable.ps1# 零依赖版编排层
│   ├── patch_icon.py                # Python 版核心(三源一站式,dry-run/apply)
│   └── verify_live_icon.py          # 运行窗口图标提取验证(py)
├── assets/
│   ├── edited_256.png               # 图标母版(256×256,去黄竖线版)
│   ├── old_frames_turbo154.json     # 官方原版 8 帧基准(md5/定位特征)
│   └── old_frames_turbo154.md       # 偏移与几何基准记录
└── references/                      # 根因分析与坑位清单
```
