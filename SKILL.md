---
name: chromium-icon-patcher
description: 替换 Turbo/Chromium 系浏览器（TurboX、Cent Browser 等便携版）的应用图标。当用户要求"换浏览器图标"、"升级后图标变了/回退了"、"换电脑后恢复自定义图标"时使用。核心认知：Chromium 运行窗口图标真来源是 chrome.dll 内嵌 PNG（不是 exe 的 RT_ICON，也不是 profile ico），三处必须同时替换。本 skill 提供 dry-run/apply 两阶段一站式脚本，支持版本升级后重打。
---

# Chromium 浏览器图标替换器（TurboX 实战沉淀）

## 概述

替换便携版 Chromium 浏览器（案例：TurboX 154.0.8037.99，`D:\Software\Turbo`）的应用图标。图标修改不是"改一个文件"，而是**三处图标源同时替换**——漏一处就会出现"这里变了那里没变"的排查困境。本 skill 沉淀自 2026-10-13 Turbo 图标改造实战（去除右上角黄竖线误认"通知1"问题，三轮排查定位三处图标源）。

**典型触发场景**：
- 浏览器大版本升级后图标回退（版本目录被整个替换）
- 换电脑/重装后恢复自定义图标
- 修改任务栏/资源管理器/运行窗口任一处图标不一致的问题

## 关键认知（必读，避免踩坑）

Chromium 浏览器图标有**三个独立来源**，各有用途：

| # | 位置 | 什么时候生效 | 不改它的症状 |
|---|---|---|---|
| 1 | `<root>/turbo.exe` 的 RT_ICON 1-8 | 资源管理器直接看 exe | 文件管理器里图标不变 |
| 2 | `<root>/User Data/<profile>/Google Profile.ico` | 任务栏**固定但未运行**时（经 ImplicitAppShortcuts 的 .lnk IconLocation 引用） | 固定图标不变 |
| 3 | `<root>/<版本目录>/chrome.dll` 内嵌 PNG | **运行中窗口/任务栏**（真来源！） | 运行时任务栏图标不变 |

**最大陷阱**：直觉以为图标在 exe 里，改完 exe 后运行时图标纹丝不动——因为 Chromium 用的是 chrome.dll 里嵌入的 PNG 资源块。**必须三处全改**。

其它易踩坑：
- 浏览器进程有后台驻留（设置里"关闭浏览器后继续运行后台应用"），`tasklist | findstr /i turbo` 确认清零再动文件
- 运行中的进程即使磁盘文件已换，窗口图标仍驻留内存 → 必须重启浏览器
- exe 有数字签名，改资源后签名失效（功能不受影响，SmartScreen 可能提示）
- `Google Profile.ico` 是 Chromium 按需再生的，浏览器更新 profile 时可能覆盖回去
- Windows 图标缓存顽固时：`ie4uinit -Show` → 广播 `SHChangeNotify(SHCNE_ASSOCCHANGED)` → 重启 explorer；仍不行则停 explorer 后置零 `%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache_*.db` 头部再重启
- 屏幕取证用 PrintWindow 抓 `Shell_TrayWnd`（`PrintWindow(hwnd, hdc, 2)`），可绕过锁屏/合成器问题；锁屏阶段普通截屏抓到的是锁屏画面

## 使用方法

### 第 1 步：安装 skill 到项目

让 AI 先把本 skill 安装到项目下（用户说"安装这个 skill"时）：

```bash
# 从 gh 私有库克隆到用户级技能目录（跨项目可用）
git clone https://github.com/leoshone/turbo-icon-patcher.git "$env:USERPROFILE\.workbuddy\skills\chromium-icon-patcher"
# 或项目级（仅当前项目）
git clone https://github.com/leoshone/turbo-icon-patcher.git .workbuddy/skills/chromium-icon-patcher
```

依赖：Python 3.8+，`pip install pillow pefile numpy`

### 第 2 步：准备新版图标 master

一张 **256×256 RGBA PNG**（`edited_256.png`）。可以来自：
- 上一轮修改的产物（升级后图标没变，直接复用）
- 让 AI 用 PIL 按几何描述修改原图（本轮案例：去黄竖线、延长红管、对称出风口）
- 用户提供的新设计稿

### 第 3 步：执行替换（先 dry-run 再 apply）

```bash
# 1. 确认浏览器完全退出（含后台）
tasklist | findstr /i turbo   # 必须 0 个进程

# 2. dry-run 看计划（不写盘）
python scripts/patch_icon.py --new-icon edited_256.png --root "D:\Software\Turbo"

# 3. 确认无误后 apply
python scripts/patch_icon.py --new-icon edited_256.png --root "D:\Software\Turbo" --apply --all-profiles

# 4. 重启浏览器，验证
```

**exe 已改过的情况（升级后场景）**：chrome.dll 旧帧定位需要"旧图标基准帧"。若 turbo.exe 已是新图标但 chrome.dll 还是旧的，直接跑即可（基准取 exe 现状=dll 现状）；若两者都已改过但 dll 回退了（升级场景），需要用**仓库里留存的旧帧基准** `assets/old_frames_turbo154.json`（含各帧 md5 与定位特征），或从 `turbo.exe.bak` 提取：

```bash
python scripts/patch_icon.py --new-icon edited_256.png --root "D:\Software\Turbo" \
  --reference-exe "D:\Software\Turbo\turbo.exe.bak" --apply
```

### 第 4 步：验证（AI 自动执行）

```python
# 从运行窗口提取 GCLP_HICON 实际图标并做旧图标签检测
# 完整脚本见 references/verify_live_icon.py
# 判定标准：右上角旧竖线区域（x>=84%, y<=33%）黄像素最高列 < 8 → 新图标
```

同时检查三处：
1. exe：pefile 提取 RT_ICON id=8 → 旧图签名应为 0
2. ico：PIL 打开 64px 帧 → 同上
3. 运行窗口：GCLP_HICON → 提取位图 → 同上

## 升级后图标回退的处理流程

浏览器大版本升级会重装版本目录（`<新版本号>/chrome.dll`），图标回退到官方版：

```
1. 找到新版本目录（数字.数字.数字.数字 命名）
2. 从上一轮备份/本仓库 assets 找到旧帧基准与新版 master
3. 跑 patch_icon.py --apply（exe 和 ico 通常也要重打，因为 exe 也可能被更新）
4. 重启浏览器验证
```

## 文件结构

```
chromium-icon-patcher/
├── SKILL.md                      # 本文件
├── scripts/
│   ├── patch_icon.py             # 一站式替换工具（dry-run/apply）
│   └── verify_live_icon.py       # 运行窗口图标提取与验证
├── assets/
│   ├── edited_256.png            # 当前采用的新版 master（Turbo 图标改造版）
│   ├── old_frames_turbo154.md    # Turbo 154 旧帧定位特征记录（md5/偏移）
│   └── before_after.png          # 前后对比图（识别参考）
└── references/
    ├── rootcause.md              # 三图标源根因分析全记录
    └── pitfalls.md               # 排障坑位清单（缓存/签名/后台进程/PrintWindow）
```

## 排障速查

| 症状 | 原因 | 处理 |
|---|---|---|
| 改了 exe 运行图标不变 | 真来源是 chrome.dll | 跑 patch_icon.py 第 3 步 |
| 固定图标不变 | profile ico 未改 / ImplicitAppShortcuts 缓存 | 改 ico；顽固时取消固定重固定 |
| 全改了还不变 | 浏览器没重启（窗口图标驻留内存） | 重启浏览器 |
| 重启了还不变 | 图标缓存 | ie4uinit -Show → SHChangeNotify → 重启 explorer → 置零 iconcache_*.db |
| 资源管理器里 exe 图标不变 | 同上缓存 | 同上 |
| 一段时间后又变回去了 | 浏览器自动升级重装版本目录 | 重跑本 skill |
| UpdateResource 失败 | 浏览器/后台进程未退净 | tasklist 查净、taskkill /F |

## 案例数据（Turbo 154.0.8037.99 实测）

- exe RT_ICON：id 1-8（16/24/32/48/64/96/128/256），全 PNG 帧，lang 1033，组名 IDR_MAINFRAME
- 品牌色：红 (235,69,55)、黄 (252,187,24)、蓝 (40,120,240 附近)、绿 (96,200,120 附近)
- 旧黄竖线（256px 基准）：x 234-246, y 5-80；管道：y 24-66（中心 45），右端 x≈220
- chrome.dll 8 帧偏移（154.0.8037.99）：0x11F64420 起，帧间有序
- UpdateResourceW 写整数资源 ID 的 argtypes 必须用 c_void_p 直接传 int（MAKEINTRESOURCE 语义）
