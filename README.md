# Turbo 图标定制备份（turbo-icon-patcher）

为 Turbo 浏览器（Chromium 内核便携版）定制应用图标，并解决"三处图标源"问题的完整工具链。沉淀自 2026-10-13 实战：去除图标右上角黄色竖线（形似通知"1"），红管延长、出风口对称加高。

## 为什么需要这个仓库

Chromium 浏览器图标有**三个独立来源**，只改 exe 是不够的：

1. `turbo.exe` RT_ICON —— 资源管理器里的 exe 图标
2. `User Data/<profile>/Google Profile.ico` —— 任务栏固定项（未运行时）
3. `<版本目录>/chrome.dll` 内嵌 PNG —— **运行中窗口/任务栏图标（真来源）**

升级浏览器或换电脑后图标回退，跑一遍本工具即可全部恢复。

## 快速开始

```bash
# 依赖
pip install pillow pefile numpy

# 确认浏览器完全退出
tasklist | findstr /i turbo   # 必须 0 个

# dry-run 预览
python scripts/patch_icon.py --new-icon assets/edited_256.png --root "D:\Software\Turbo"

# 实际替换
python scripts/patch_icon.py --new-icon assets/edited_256.png --root "D:\Software\Turbo" --apply --all-profiles

# 重启浏览器后验证运行窗口图标
python scripts/verify_live_icon.py --window-title Turbo
```

完整方法论与坑位清单见 `SKILL.md`（可直接作为 AI Agent 的 Skill 安装到 `.workbuddy/skills/` 或 `~/.workbuddy/skills/`）。

## 仓库结构

```
├── SKILL.md                        # AI Agent Skill 定义（含完整方法论）
├── scripts/
│   ├── patch_icon.py               # 三源一站式替换（dry-run/apply）
│   └── verify_live_icon.py         # 运行窗口图标提取验证
├── assets/
│   ├── edited_256.png              # 当前新版 master（去黄竖线改造版）
│   ├── before_after.png            # 前后对比
│   ├── old_frames_turbo154.json    # 官方原版 8 帧基准（md5/定位特征）
│   └── old_frames_turbo154.md      # 偏移与几何基准记录
└── references/
    ├── rootcause.md                # 三图标源根因分析全记录
    └── pitfalls.md                 # 排障坑位清单
```

## AI Agent 使用提示

- 升级后回退：`patch_icon.py --apply` 直接重跑（新版本目录偏移会变，但工具按内容特征定位，无需改代码）
- exe 已是新图标但 chrome.dll 回退：`--reference-exe` 指向 `turbo.exe.bak` 或含旧帧的任意备份
- 换新电脑：克隆本仓库 → 改 `--root` 为新机浏览器路径 → 同上执行
