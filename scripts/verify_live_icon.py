# -*- coding: utf-8 -*-
"""从运行中的 Chromium 浏览器窗口提取实际图标并验证是否为新版。

用法: python verify_live_icon.py --window-title Turbo
返回: 提取的图标保存为 live_icon.png，并打印旧图标签判定。
"""
import argparse
import ctypes
from ctypes import wintypes as wt

import numpy as np
from PIL import Image

user32 = ctypes.WinDLL('user32')
gdi32 = ctypes.WinDLL('gdi32')


def find_window(title_substr: str):
    hits = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
    def cb(hwnd, lp):
        cls = ctypes.create_unicode_buffer(64)
        user32.GetClassNameW(hwnd, cls, 64)
        if cls.value == 'Chrome_WidgetWin_1':
            buf = ctypes.create_unicode_buffer(128)
            user32.GetWindowTextW(hwnd, buf, 128)
            if title_substr.lower() in buf.value.lower() and user32.IsWindowVisible(hwnd):
                hits.append((hwnd, buf.value))
        return True
    user32.EnumWindows(cb, 0)
    return hits


def extract_icon(hwnd) -> Image.Image:
    # GCLP_HICON = -14, GCLP_HICONSM = -34
    hicon = user32.GetClassLongPtrW(ctypes.c_void_p(hwnd), wt.DWORD(0xFFFFFFF2)) or \
            user32.GetClassLongPtrW(ctypes.c_void_p(hwnd), wt.DWORD(0xFFFFFFE2)) or \
            user32.SendMessageW(ctypes.c_void_p(hwnd), 0x7F, 1, 0) or \
            user32.SendMessageW(ctypes.c_void_p(hwnd), 0x7F, 0, 0)
    if not hicon:
        raise RuntimeError('window has no icon')

    class ICONINFO(ctypes.Structure):
        _fields_ = [("fIcon", wt.BOOL), ("xHotspot", wt.DWORD), ("yHotspot", wt.DWORD),
                    ("hbmMask", wt.HBITMAP), ("hbmColor", wt.HBITMAP)]

    ii = ICONINFO()
    if not user32.GetIconInfo(ctypes.c_void_p(hicon), ctypes.byref(ii)):
        raise RuntimeError('GetIconInfo failed')

    class BM(ctypes.Structure):
        _fields_ = [("bmType", wt.LONG), ("bmWidth", wt.LONG), ("bmHeight", wt.LONG),
                    ("bmWidthBytes", wt.LONG), ("bmPlanes", wt.WORD),
                    ("bmBitsPixel", wt.WORD), ("bmBits", ctypes.c_void_p)]

    bm = BM()
    gdi32.GetObjectW(ctypes.c_void_p(ii.hbmColor), ctypes.sizeof(BM), ctypes.byref(bm))
    w, h = bm.bmWidth, bm.bmHeight

    class BMI(ctypes.Structure):
        _fields_ = [("biSize", wt.DWORD), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
                    ("biPlanes", wt.WORD), ("biBitCount", wt.WORD), ("biCompression", wt.DWORD),
                    ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", ctypes.c_long),
                    ("biYPelsPerMeter", ctypes.c_long), ("biClrUsed", wt.DWORD),
                    ("biClrImportant", wt.DWORD)]

    bmi = BMI()
    bmi.biSize = ctypes.sizeof(BMI)
    bmi.biWidth, bmi.biHeight = w, -h
    bmi.biPlanes, bmi.biBitCount, bmi.biCompression = 1, 32, 0
    buf = ctypes.create_string_buffer(w * h * 4)
    hdc = user32.GetDC(None)
    gdi32.GetDIBits(hdc, ctypes.c_void_p(ii.hbmColor), 0, h, buf, ctypes.byref(bmi), 0)
    user32.ReleaseDC(None, hdc)
    return Image.frombuffer('RGBA', (w, h), buf.raw, 'raw', 'BGRA', 0, 1)


def old_bar_score(im: Image.Image) -> int:
    a = np.asarray(im.convert('RGBA')).astype(int)
    h, w = a.shape[:2]
    x0, y0 = int(w * 0.84), int(h * 0.33)
    yellow = (a[..., 0] > 190) & (a[..., 1] > 120) & (a[..., 2] < 110) & (a[..., 3] > 40)
    zone = yellow[:y0, x0:]
    return int(zone.sum(axis=0).max()) if zone.size and zone.shape[0] else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--window-title', default='Turbo')
    ap.add_argument('--out', default='live_icon.png')
    args = ap.parse_args()

    hits = find_window(args.window_title)
    if not hits:
        raise SystemExit(f'no visible window matching "{args.window_title}"')
    hwnd, title = hits[0]
    print(f'window: {title!r} hwnd={hwnd}')

    im = extract_icon(hwnd)
    im.save(args.out)
    print(f'saved {args.out} ({im.size[0]}x{im.size[1]})')

    score = old_bar_score(im)
    verdict = 'OLD icon (yellow bar present)' if score >= 8 else 'NEW icon confirmed'
    print(f'old-bar column-max={score} -> {verdict}')
    return 0 if score < 8 else 1


if __name__ == '__main__':
    raise SystemExit(main())
