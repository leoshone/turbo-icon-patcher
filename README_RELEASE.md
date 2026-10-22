# Turbo Icon Patcher - Release Packages

Two editions of the same tool. Pick ONE.

## Which one?

| | `turbo-icon-patcher-py` | `turbo-icon-patcher` |
|---|---|---|
| Runtime | Python 3.8+ (auto venv in folder) | **None** - Windows 10/11 built-ins only |
| Entry | `patch_turbo_icon.bat` | `patch_turbo_icon_portable.bat` |
| Internals | Python + PIL/pefile/numpy | bat + PowerShell 5.1 + inbox csc.exe (Add-Type) |
| First run | creates `.venv`, installs deps (~30s) | compiles embedded C# (~1s) |
| New PC | needs any local Python | copy folder, double-click, done |

## Usage (both editions identical)

1. Keep the folder inside the Turbo install dir, e.g. `D:\Software\Turbo\icon-patcher\`
2. Double-click the `.bat`:
   - `xxx.bat` - apply the patch (kills Turbo background processes first)
   - `xxx.bat dry` - preview only (portable edition; py edition: run the ps1/py without `--apply`)
   - `xxx.bat verify` - verify the live window icon while Turbo is running
3. Restart Turbo. If the taskbar is stubborn: unpin + re-pin, or restart explorer.exe.

## Re-apply after a Turbo upgrade

Just run the same `.bat` again. Frame locating is content-based (no hardcoded
offsets), already-patched frames are skipped safely, and the dll size is preserved.

## Notes

- Keep the folder on an ASCII path (no Chinese characters).
- Patching invalidates the turbo.exe digital signature (expected, harmless).
- If the exe already has the custom icon but chrome.dll was reverted AND the
  dll frames cannot be located, the py edition accepts
  `--reference-exe turbo.exe.bak`; the portable edition infers from the live exe.
- Icon master: `assets\edited_256.png`. Replace it to ship a different design.

## Source

https://github.com/leoshone/turbo-icon-patcher
