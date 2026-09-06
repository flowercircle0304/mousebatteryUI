using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Win32.SafeHandles;

const int VendorId = 0x1D57;
const int ProductId = 0xFA60;

var devices = DeviceList.Local.GetHidDevices(vendorID: VendorId, productID: ProductId).ToList();
Console.WriteLine($"Found {devices.Count} collection(s) for VID=0x{VendorId:X4} PID=0x{ProductId:X4}.");

foreach (var d in devices)
{
    Console.WriteLine();
    Console.WriteLine($"Path: {d.DevicePath}");
    Console.WriteLine($"  MaxFeatureReportLength={d.GetMaxFeatureReportLength()}  MaxInputReportLength={d.GetMaxInputReportLength()}  MaxOutputReportLength={d.GetMaxOutputReportLength()}");
    try
    {
        var descriptor = d.GetReportDescriptor();
        foreach (var item in descriptor.DeviceItems)
        {
            foreach (uint usage in item.Usages.GetAllValues())
            {
                Console.WriteLine($"  Usage: page=0x{(usage >> 16):X4} usage=0x{(usage & 0xFFFF):X4}");
            }
        }
    }
    catch (Exception ex) { Console.WriteLine("  (descriptor read failed: " + ex.Message + ")"); }
}

// Attack Shark X11 / FURYCUBE F1 (per dressedinblack5/attack-shark-x11-electron docs): DPI (report
// 0x04) and polling rate (report 0x06) are documented as SET_REPORT-only — no GET_REPORT/read-back
// is documented anywhere in that project (nor for battery, which is a pure push, matching what this
// app already implements). So this is READ-ONLY: GetFeature alone, no SetFeature — never mutate the
// mouse's actual settings just to probe it. If the firmware happens to answer a plain read despite
// the community project never finding one, this will show it; if not, that's the answer too.
Console.WriteLine();
Console.WriteLine("=== Trying GetFeature only (no writes) ===");

TryGetFeature(devices, reportId: 0x06, totalLen: 10);
TryGetFeature(devices, reportId: 0x04, totalLen: 57);

static void TryGetFeature(List<HidDevice> devices, byte reportId, int totalLen)
{
    foreach (var d in devices)
    {
        var handle = RawHidFeatureIo.Open(d.DevicePath);
        if (handle is null) continue;

        using (handle)
        {
            var getBuf = new byte[totalLen];
            getBuf[0] = reportId;
            bool getOk = RawHidFeatureIo.GetFeature(handle, getBuf);
            Console.WriteLine($"--- report 0x{reportId:X2} on {d.DevicePath}: GetFeature result: " + getOk
                + (getOk ? "  data: " + BitConverter.ToString(getBuf) : ""));
        }
    }
}

// Copied inline (scratch tool, no project reference) from
// src/MouseBatteryTray/Providers/RawHidFeatureIo.cs — see that file for why the
// zero-access fallback is needed (Windows blocks GENERIC_READ|WRITE on the system's
// active mouse/keyboard collection, but Feature-report IOCTLs are exempt).
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
