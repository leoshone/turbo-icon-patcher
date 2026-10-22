# Turbo 图标一键补丁 - 使用文档

> 适用:Turbo / Cent Browser 等 Chromium 内核便携版浏览器
> 解决:浏览器升级后自定义图标回退的问题。一次替换三处图标源:
> ① `turbo.exe` 资源管理器图标 ② `User Data\<profile>\Google Profile.ico` 任务栏固定图标 ③ `<版本目录>\chrome.dll` 内嵌 PNG(**运行窗口/任务栏的真来源**)

---

## 一、两个包怎么选(只需下载一个)

| | turbo-icon-patcher-**portable**.zip(推荐) | turbo-icon-patcher-**py**.zip |
|---|---|---|
| 需要安装什么 | **什么都不装**(用 Win10/11 自带组件) | 需要电脑上有 Python 3.8+ |
| 首次运行 | 编译内嵌 C# 引擎,约 1 秒 | 自动创建 .venv 并装依赖,约 30 秒(需联网) |
| 入口文件 | `patch_turbo_icon_portable.bat` | `patch_turbo_icon.bat` |
| 适合场景 | 换新电脑、给别人的机器用、不想碰 Python | 已有 Python 环境、想改脚本逻辑 |

两包用法完全一样,下面以 portable 版为例(py 版把 `patch_turbo_icon_portable.bat` 换成 `patch_turbo_icon.bat` 即可)。

## 二、安装位置

把解压出的 `icon-patcher` 文件夹放到 **Turbo 安装目录下**:

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

## 三、日常使用(3 种命令)

| 操作 | 怎么做 |
|---|---|
| **打补丁(最常用)** | 双击 `patch_turbo_icon_portable.bat`。会自动:杀掉 Turbo 后台进程 → 替换三处图标 → 刷新系统图标缓存 |
| **只预览不写入** | 命令行运行 `patch_turbo_icon_portable.bat dry`(portable 版独有;py 版直接跑 `python scripts/patch_icon.py --new-icon assets/edited_256.png --root <Turbo目录>` 不加 `--apply`) |
| **验证运行窗口图标** | Turbo **开着**时,命令行运行 `patch_turbo_icon_portable.bat verify`(portable 版独有) |

打完补丁**重启 Turbo** 生效。如果任务栏图标没变:
1. 任务栏右键 Turbo → 取消固定 → 重新固定
2. 还不行:任务管理器重启 `Windows 资源管理器`
3. 顽固情况重启电脑(系统图标缓存极端顽固时)

## 四、浏览器升级后(图标回退)怎么办

直接**再双击一次 bat** 即可。工具按帧内容特征定位(不依赖硬编码偏移),已替换过的帧自动跳过,重复运行安全。

## 五、常见问题

| 现象 | 原因与处理 |
|---|---|
| `BeginUpdateResource failed` / 拒绝访问 | Turbo 没退净。任务管理器结束所有 `turbo.exe` 后重跑(bat 会自动杀,手动跑 ps1/py 时需自己杀) |
| 图标某处没变 | 九成是系统图标缓存:取消固定重固定 / 重启 explorer |
| 换了新电脑报 Python 相关错误 | 用 **portable 版**,或删除 `icon-patcher\.venv` 后重跑(py 版会自动重建环境) |
| exe 文件属性里签名失效 | 正常现象:改资源必然破坏签名,功能不受影响 |
| 想换成别的图标设计 | 用新的 256×256 PNG 替换 `assets\edited_256.png`(去黄竖线版已内置) |
| exe 是新图标但 chrome.dll 补不上 | 说明 exe 曾被改过、失去了旧帧基准。py 版用 `--reference-exe` 指向旧 exe 备份;portable 版直接找 AI 重新生成基准 |

## 六、安全说明

- 工具只写图标资源,不动浏览器任何功能代码;chrome.dll 采用**等长替换**,文件总大小与 PE 结构不变
- 全程本地运行,无网络请求(portable 版首次编译也不联网)
- 源码与文档:https://github.com/leoshone/turbo-icon-patcher
