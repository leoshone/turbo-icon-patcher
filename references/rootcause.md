# Turbo 154 图标源根因分析（2026-10-13 实战全记录）

## 需求

Turbo 浏览器（154.0.8037.99，`D:\Software\Turbo`；注：exe 版本资源里的 ProductName 是 "TurboX"，对外品牌名为 Turbo）图标右上角有黄色竖线（涡轮出风口），远看像通知数"1"。要求：红色管道向右伸长一点、出风口上下对称加高（短于原黄竖线）、去掉黄竖线。

## 三轮排查链（每轮解决一类症状，最终三轮才全通）

### 第 1 轮：turbo.exe RT_ICON（解决：资源管理器里的 exe 图标）

- pefile 提取 RT_ICON：IDR_MAINFRAME 组 id 1-8（16/24/32/48/64/96/128/256），全 PNG 帧，lang 1033
- PIL 像素级编辑 256px 主帧（几何测量 + 复用旧黄竖线圆角端帽重着色，保证抗锯齿风格一致）
- LANCZOS 重建 8 尺寸 → Win32 UpdateResourceW 写回
- **结果**：exe 图标变了，但任务栏运行图标不变

关键坑：UpdateResourceW 的 argtypes 必须把 type/name 声明为 c_void_p 且直接传 int（MAKEINTRESOURCE 语义）；写 8 参签名或用 c_wchar_p 都会 TypeError/写错。

### 第 2 轮：Google Profile.ico（解决：任务栏固定项未运行时的图标）

- 用户报告任务栏图标没刷新；重定向 IconCache、重启 explorer 均无效
- 枚举 `User Pinned\TaskBar` 未找到 Turbo → 深挖 `ImplicitAppShortcuts\73b8bf6262abf3d8\Turbo.lnk`
- 解析 .lnk 二进制（LinkFlags=0xff）→ ICON_LOCATION 指向 `User Data\Default\Google Profile.ico`（Chromium 为 profile 生成的快捷方式图标）
- 重写该 ico 全部 13 尺寸
- **结果**：固定项（未运行）新图标正确，运行窗口仍旧

### 第 3 轮：chrome.dll 内嵌 PNG（真根因，解决：运行窗口/任务栏运行图标）

- 现象矛盾：进程 19:27 启动（exe 18:23 已改），映射映像已验证为新（ReadProcessMemory 逐字节 md5 相同），但 GCLP_HICON 提取的 48px 图标与备份 exe 旧帧 diff=0.0（逐字节相同）
- 结论：窗口图标**不是**通过 LoadIcon(exe) 从映像资源读取，另有来源
- 以旧帧 PNG 特征字节（帧数据 [40:72]）全盘搜索 → `154.0.8037.99/chrome.dll` 8 帧全部命中（0x11F64420 起连续分布）
- **Chromium 的窗口图标来自 chrome.dll 内嵌 PNG 资源块**

### chrome.dll 等大小补丁技术

约束：DLL 被 PE 校验/映射依赖，**文件总大小与各帧偏移不能变**。

新 PNG 与旧 PNG 字节数天然不同（870 vs 890 等），三种凑大小手段（按帧择优）：
1. **tEXt 块填充**（slack ≥ 12B）：在 IEND 前插入合法 tEXt 块，payload 补齐差值
2. **IEND 后补零**（任意 slack ≥ 0）：解码器忽略 IEND 后数据
3. **filter=0 重组压缩**（新 PNG 比旧的大时）：手动组装 PNG（逐行 \x00 前缀 + zlib level9/memLevel8/strategy0），96px 帧 9668→7707B 反而更小

补丁后逐帧解码验证 + 全文件搜旧帧特征（0 命中）→ 换入 → 重启 → GCLP_HICON 提取 = 新图（col-max=1）。

## 教训汇总

1. **Chromium 图标三源**：exe RT_ICON（文件管理器）/ profile ico（固定项）/ chrome.dll 内嵌 PNG（运行窗口）——改一漏二必返工
2. **运行时驻留**：窗口图标在内存，改磁盘不重启无效；进程启动时间 vs 文件修改时间要秒级对比（曾差 15 秒导致误判）
3. **验证要原子**：PrintWindow 抓 Shell_TrayWnd 绕过锁屏；GCLP_HICON+GetIconInfo+GetDIBits 提取真实位图；像素坐标判定（旧竖线顶到 y=0，新蜗壳 y≥8）防"看着像"
4. **内存取证**：EnumProcessModulesEx + ReadProcessMemory 验证映像一致性，排除"换错文件/映射旧页"类假设
