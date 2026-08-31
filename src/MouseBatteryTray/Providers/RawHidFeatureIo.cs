using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Direct Win32 HID Feature-report I/O, bypassing HidSharp.
///
/// Windows refuses to grant GENERIC_READ|GENERIC_WRITE on a HID collection it recognizes as the
/// system's active mouse or keyboard — this is a deliberate anti-keylogger protection, not a bug
/// or a competing-process lock. Feature reports are explicitly exempt from that protection though:
/// a handle opened with *no* read/write access at all (desired access = 0) still works fine for
/// HidD_SetFeature/HidD_GetFeature, because those go through IOCTLs that don't require the file
/// handle's generic read/write rights. This is exactly the two-step "try full access, then fall
/// back to zero access" dance libusb/hidapi's Windows backend does in hid_open_path (windows/hid.c)
/// — this class mirrors it directly, since it's what let a Razer mouse's control interface (which
/// lives on the same top-level collection as its primary "Mouse" usage) work at all.
/// </summary>
internal static class RawHidFeatureIo
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    public static SafeFileHandle? Open(string devicePath)
    {
        var handle = CreateFileW(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            // Protected input device: retry with no read/write access. Feature reports still work.
            handle = CreateFileW(devicePath, 0, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        }

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    public static bool SetFeature(SafeFileHandle handle, byte[] buffer) =>
        HidD_SetFeature(handle, buffer, (uint)buffer.Length);

    public static bool GetFeature(SafeFileHandle handle, byte[] buffer) =>
        HidD_GetFeature(handle, buffer, (uint)buffer.Length);
}
