# -*- coding: utf-8 -*-
"""
Turbo/Chromium 浏览器图标一站式替换工具。

用法:
  python patch_icon.py --new-icon edited_256.png [--root D:\\Software\\Turbo] [--apply]
  默认 dry-run，加 --apply 才写盘。

前置: Pillow (pip install pillow), pefile (pip install pefile), numpy
执行前必须完全退出浏览器（含后台进程 tasklist | findstr /i turbo）。

对 Chromium 系浏览器（以 TurboX 为例）的三处图标源做替换:
  1. <root>/turbo.exe                   RT_ICON 1-8 (PNG 帧, IDR_MAINFRAME)
  2. <root>/User Data/*/Google Profile.ico  任务栏固定项图标
  3. <root>/<ver>/chrome.dll            内嵌 PNG 资源块 (运行窗口图标的真来源)
"""
import argparse
import ctypes
import hashlib
import io
import os
import struct
import sys
import zlib

import numpy as np
from PIL import Image


def md5(data: bytes) -> str:
    return hashlib.md5(data).hexdigest()


def old_bar_score(im: Image.Image) -> int:
    """旧图标签名：右上角细长黄竖线。返回该区域最高黄像素列高（>=8 即视为旧图）。"""
    a = np.asarray(im.convert('RGBA')).astype(int)
    h, w = a.shape[:2]
    x0, y0 = int(w * 0.84), int(h * 0.33)
    yellow = (a[..., 0] > 190) & (a[..., 1] > 120) & (a[..., 2] < 110) & (a[..., 3] > 40)
    zone = yellow[:y0, x0:]
    if zone.size == 0 or zone.shape[0] == 0:
        return 0
    return int(zone.sum(axis=0).max())


# ---------- exe RT_ICON replacement (Win32 UpdateResource) ----------

def patch_exe(exe_path: str, frames: dict, apply: bool):
    import pefile
    pe = pefile.PE(exe_path, fast_load=True)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_RESOURCE']])
    ids = []
    for entry in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        if entry.id == pefile.RESOURCE_TYPE['RT_ICON']:
            for e in entry.directory.entries:
                lang = e.directory.entries[0]
                ids.append((e.id, lang.id))
    pe.close()
    if not ids:
        print('  [exe] no RT_ICON found, skip')
        return
    if not apply:
        print(f'  [exe] dry-run: would update {len(ids)} RT_ICON frames via UpdateResource')
        return
    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
    Begin = kernel32.BeginUpdateResourceW
    Update = kernel32.UpdateResourceW
    End = kernel32.EndUpdateResourceW
    Begin.argtypes = [ctypes.c_wchar_p, ctypes.c_bool]
    Begin.restype = ctypes.c_void_p
    Update.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                       ctypes.c_ushort, ctypes.c_void_p, ctypes.c_uint]
    Update.restype = ctypes.c_bool
    End.argtypes = [ctypes.c_void_p, ctypes.c_bool]
    h = Begin(exe_path, False)
    if not h:
        sys.exit(f'  [exe] BeginUpdateResource failed (进程未关净?) err={ctypes.get_last_error()}')
    RT_ICON = 3
    for icon_id, lang in ids:
        data = frames[icon_id]
        buf = ctypes.create_string_buffer(data, len(data))
        if not Update(h, RT_ICON, icon_id, lang, buf, len(data)):
            End(h, True)
            sys.exit(f'  [exe] UpdateResource id={icon_id} failed err={ctypes.get_last_error()}')
    if not End(h, False):
        sys.exit(f'  [exe] EndUpdateResource commit failed err={ctypes.get_last_error()}')
    print(f'  [exe] updated {len(ids)} RT_ICON frames')


# ---------- .ico rebuild ----------

def patch_profile_ico(ico_path: str, master: Image.Image, apply: bool):
    im = Image.open(ico_path)
    try:
        sizes = sorted(im.ico.sizes()) if hasattr(im, 'ico') else [(256, 256)]
    finally:
        im.close()
    if not apply:
        print(f'  [ico] {os.path.basename(os.path.dirname(ico_path))}: would rebuild {len(sizes)} sizes')
        return
    imgs = [master.resize(s, Image.LANCZOS) if s != (256, 256) else master for s in sizes]
    imgs[0].save(ico_path, format='ICO', sizes=sizes)
    print(f'  [ico] {os.path.basename(os.path.dirname(ico_path))}: rebuilt {len(sizes)} sizes')


# ---------- chrome.dll equal-size PNG patch ----------

def _chunk(t: bytes, d: bytes) -> bytes:
    c = struct.pack('>I', len(d)) + t + d
    return c + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)


def build_png_exact(im: Image.Image, target: int) -> bytes:
    """生成恰好 target 字节的等价 PNG（帧内容不变）。
    依次尝试: 原样 + tEXt 填充 / 原样 + IEND 后补零 / filter=0 重组压缩 + 上述填充。
    解码器忽略 IEND 后的尾部数据，tEXt 为合法块，均不影响渲染。"""
    orig = io.BytesIO()
    im.save(orig, 'PNG', compress_level=9)
    base = orig.getvalue()

    def text_pad(png: bytes):
        if len(png) > target - 12:
            return None
        payload_len = target - len(png) - 12
        payload = b'x\x00' + b' ' * (payload_len - 2)
        tchunk = struct.pack('>I', len(payload)) + b'tEXt' + payload
        tchunk += struct.pack('>I', zlib.crc32(b'tEXt' + payload) & 0xffffffff)
        iend = png.rfind(b'IEND') - 4
        out = png[:iend] + tchunk + png[iend:]
        return out if len(out) == target else None

    def iend_pad(png: bytes):
        if len(png) > target:
            return None
        return png + b'\x00' * (target - len(png))

    def manual() -> bytes:
        w, h = im.size
        raw = b''.join(b'\x00' + im.tobytes()[y * w * 4:(y + 1) * w * 4] for y in range(h))
        co = zlib.compressobj(9, zlib.DEFLATED, 15, 8, 0)
        z = co.compress(raw) + co.flush()
        ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)
        return (b'\x89PNG\r\n\x1a\n' + _chunk(b'IHDR', ihdr)
                + _chunk(b'IDAT', z) + _chunk(b'IEND', b''))

    for cand in (text_pad(base), iend_pad(base), text_pad(manual()), iend_pad(manual())):
        if cand is not None:
            img = Image.open(io.BytesIO(cand))
            img.load()
            assert img.size == im.size
            return cand
    raise ValueError(f'cannot fit PNG into {target} bytes')


def collect_frames(exe_path: str, max_id: int = 8) -> dict:
    """从 exe 读取 RT_ICON 1..max_id 原始字节。"""
    import pefile
    pe = pefile.PE(exe_path, fast_load=True)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_RESOURCE']])
    frames = {}
    for entry in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        if entry.id != pefile.RESOURCE_TYPE['RT_ICON']:
            continue
        for e in entry.directory.entries:
            if e.id <= max_id:
                lang = e.directory.entries[0]
                frames[e.id] = pe.get_data(lang.data.struct.OffsetToData, lang.data.struct.Size)
    pe.close()
    return frames


def patch_chrome_dll(dll_path: str, master: Image.Image, old_frames: dict, apply: bool):
    """old_frames: {icon_id: 旧帧原始字节}（来自未打补丁的 exe 备份），作为 dll 内定位基准。
    在 dll 中定位旧帧 → 等大小替换为新帧（DLL 总大小不变，PE 结构不动）。"""
    data = bytearray(open(dll_path, 'rb').read())
    orig_len = len(data)
    plan = []
    for i, old_bytes in sorted(old_frames.items()):
        marker = old_bytes[40:72]
        idx = data.find(marker)
        if idx == -1:
            print(f'  [dll] frame id={i}: old frame not found (可能已替换过), skip')
            continue
        start = idx - 40
        if bytes(data[start:start + len(old_bytes)]) != old_bytes:
            print(f'  [dll] frame id={i}: partial match only, skip for safety')
            continue
        size = master.size if i == 8 else (16, 24, 32, 48, 64, 96, 128)[i - 1]
        im = master if i == 8 else master.resize(size, Image.LANCZOS)
        plan.append((start, old_bytes, build_png_exact(im, len(old_bytes)), i))
    if not apply:
        print(f'  [dll] dry-run: {len(plan)} frames locatable and size-matched')
        return plan
    for start, old_bytes, new_bytes, i in plan:
        data[start:start + len(old_bytes)] = new_bytes
        print(f'  [dll] frame id={i} patched @ {hex(start)}')
    assert len(data) == orig_len, 'dll size must stay identical'
    with open(dll_path, 'wb') as f:
        f.write(bytes(data))
    print(f'  [dll] written {dll_path} ({orig_len}B, size preserved)')
    return plan


# ---------- main ----------

SIZES = (1, 16), (2, 24), (3, 32), (4, 48), (5, 64), (6, 96), (7, 128), (8, 256)


def main():
    ap = argparse.ArgumentParser(description='Turbo/Chromium 三处图标源一站式替换')
    ap.add_argument('--new-icon', required=True, help='新版 256px PNG (master)')
    ap.add_argument('--root', default=r'D:\Software\Turbo', help='浏览器安装根目录')
    ap.add_argument('--reference-exe',
                    help='含旧图标的 exe（chrome.dll 旧帧定位基准）。'
                         'exe 未改过时缺省用 root/turbo.exe；已改过则必须提供其备份')
    ap.add_argument('--all-profiles', action='store_true', help='处理所有 profile 的 Google Profile.ico')
    ap.add_argument('--apply', action='store_true', help='实际写盘（默认 dry-run）')
    args = ap.parse_args()

    master = Image.open(args.new_icon).convert('RGBA')
    assert master.size == (256, 256), 'master must be 256x256'

    root = args.root
    exe = os.path.join(root, 'turbo.exe')
    ref_exe = args.reference_exe or exe

    old_frames = collect_frames(ref_exe)
    new_frames = {}
    for i, size in SIZES:
        im = master if i == 8 else master.resize((size, size), Image.LANCZOS)
        buf = io.BytesIO()
        im.save(buf, 'PNG', compress_level=9)
        new_frames[i] = buf.getvalue()

    cur8 = collect_frames(exe, max_id=8).get(8)
    exe_is_new = cur8 is not None and md5(cur8) == md5(new_frames[8])
    ref_is_old = old_frames.get(8) is not None and not exe_is_new or ref_exe != exe
    print(f'[status] exe 256px: {"NEW 已改过" if exe_is_new else "OLD/未匹配"}; '
          f'chrome.dll 定位基准: {ref_exe}')

    print('[1/3] turbo.exe RT_ICON 1-8')
    patch_exe(exe, new_frames, args.apply)

    print('[2/3] Google Profile.ico')
    profiles = ['Default']
    if args.all_profiles:
        ud = os.path.join(root, 'User Data')
        if os.path.isdir(ud):
            profiles = [d for d in os.listdir(ud)
                        if os.path.exists(os.path.join(ud, d, 'Google Profile.ico'))]
    for p in profiles:
        ico = os.path.join(root, 'User Data', p, 'Google Profile.ico')
        if os.path.exists(ico):
            patch_profile_ico(ico, master, args.apply)

    print('[3/3] chrome.dll embedded PNG (运行窗口图标真来源)')
    ver_dirs = [d for d in os.listdir(root)
                if d[0:1].isdigit() and '.' in d and os.path.isdir(os.path.join(root, d))]
    for vd in ver_dirs:
        dll = os.path.join(root, vd, 'chrome.dll')
        if os.path.exists(dll):
            patch_chrome_dll(dll, master, old_frames, args.apply)

    print('\n完成提示: 重启浏览器生效；任务栏缓存顽固时 ie4uinit -Show + 重启 explorer。')


if __name__ == '__main__':
    main()
