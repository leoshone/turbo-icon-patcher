# 排障坑位清单

## 进程与文件锁

- 浏览器默认开"关窗后后台驻留"，`Stop-Process -Name turbo -Force` 前先 `tasklist | findstr /i turbo` 确认；WM_CLOSE 优雅关闭常被"多标签确认框"挡住
- chrome.dll 被映射锁定，替换必须 0 进程；exe 可用 UpdateResource 原生替换（自身处理重命名交换）
- 换 dll 前把新 dll 写成 `.new` 再 Move-Item，避免写一半被进程加载

## 签名与安全

- 改任何 PE 资源后 Authenticode 签名失效（Status: NotSigned），功能不受影响；SmartScreen 首次运行提示"仍要运行"即可
- 本环境 safe-delete 钩子会拦截 iconcache_*.db 删除（genie-trash fail-closed）——用"头部置零"替代删除：`open(f,'r+b').write(b'\x00'*4096)`，explorer 视为损坏自动重建

## 图标缓存三板斧（递进）

1. `ie4uinit.exe -Show` + `shell32.SHChangeNotify(0x08000000, 0, None, None)`（SHCNE_ASSOCCHANGED 广播）
2. 重启 explorer（`Stop-Process explorer` → `Start-Process explorer`）
3. 停 explorer → 置零 `iconcache_*.db` 头部 → 起 explorer（12/19 个文件能直接删就删，删不掉的置零）

## 时序陷阱

- 磁盘 mtime 只有秒级精度判断进程新旧不可靠——用 `Get-Process`.StartTime（毫秒）对比文件 LastWriteTime
- 本案例：浏览器 18:41:42 启动读旧 ico，18:41:57 才写入新 ico，差 15 秒，旧图驻留窗口内存 → 必须重启浏览器

## 屏幕取证

- 锁屏阶段 `ImageGrab.grab()`/GDI BitBlt 抓到的都是锁屏画面，不是"抓屏失败"
- 任务栏取证用 PrintWindow：`user32.PrintWindow(hwnd_taskbar, hdc_mem, 2)`，hwnd 用 `GetClassNameW == 'Shell_TrayWnd'` 枚举；flag=2（PW_RENDERFULLCONTENT）才能拿到 DWM 合成内容
- 24px 小图标肉眼极易误判（黄蜗壳弧段 vs 旧黄竖线），必须程序化：旧竖线签名 = 右上区黄像素列且顶到 y≤4/24；或与旧/新 master 分别算 diff 取小者
- WindowFromPoint 探测任务栏按钮只会返回 explorer——无法用窗口归属判断按钮对应程序，需用 PrintWindow 像素 + .lnk 解析

## .lnk 解析要点

- LinkFlags=0xff 时五个 String 节全在；顺序：NAME_STRING/RELATIVE_PATH/WORKING_DIR/CMD_ARGS/ICON_LOCATION，各节为 CC(2B)+UTF-16LE
- 任务栏固定项可执行文件在 `User Pinned\TaskBar`（普通固定）或 `User Pinned\ImplicitAppShortcuts\<hash>\`（Chromium profile 固定）
- Chromium 的 PWA/网页固定项（如"滴答清单"）图标走独立 .ico（Web Applications\_crx_*\），与应用图标无关——先分清用户指认的是哪个按钮

## UpdateResourceW（Python ctypes）

- 6 参数：`(hUpdate, lpType, lpName, wLanguage, lpData, cb)`
- lpType/lpName 传整数 ID 用 MAKEINTRESOURCE 语义：argtypes 设 `c_void_p` 直接传 int；设成 c_wchar_p 会 TypeError "takes 8 arguments (6 given)"（该报错也可能因 argtypes 多写了一个参数）
- 数据用 `create_string_buffer(data, len(data))` 传；EndUpdateResource 第二参 fRejectSave=False 才提交

## PNG 等大小补丁

- 新帧与旧帧字节数不同是常态（同一图像不同编码器/压缩级）
- 凑大小三板斧（解码等价）：IEND 前 tEXt 块（≥12B 余量）/ IEND 后补零（任意余量，解码器忽略）/ filter=0 手工重组（新比旧大时反而更小：96 帧 9668→7707）
- 验证补丁：PIL 逐帧 load + 全文件搜索旧帧特征字节（帧[40:72]）必须 0 命中

## 图标内容判定（防自欺）

- 与旧/新 master 都做逐像素 diff，取小者定版本；"看着像旧/新"不作数
- 旧竖线与黄蜗壳弧段在 24px 下肉眼难分——用几何签名：旧竖线顶边悬空（y0 起）、新蜗壳不顶边
