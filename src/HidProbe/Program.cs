using System.Runtime.InteropServices;
using HidSharp;
using Microsoft.Win32.SafeHandles;

const int VendorId = 0x36A7;
const int ProductId = 0xA872;
const int FeatLen = 65;

[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
[DllImport("hid.dll", SetLastError = true)]
static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, uint len);
[DllImport("hid.dll", SetLastError = true)]
static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, uint len);

SafeFileHandle? Open(string path)
{
    var h = CreateFileW(path, 0x80000000 | 0x40000000, 1 | 2, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
    if (h.IsInvalid) { h.Dispose(); h = CreateFileW(path, 0, 1 | 2, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero); }
    return h.IsInvalid ? null : h;
}

var candidates = DeviceList.Local.GetHidDevices(vendorID: VendorId)
    .Where(d => d.ProductID == ProductId && d.GetMaxFeatureReportLength() == FeatLen)
    .ToList();

Console.WriteLine($"Found {candidates.Count} collection(s) with Feat={FeatLen}.");
foreach (var dev in candidates)
{
    Console.WriteLine($"Path: {dev.DevicePath}");
    var handle = Open(dev.DevicePath);
    if (handle is null) { Console.WriteLine("  open failed"); continue; }
    using (handle)
    {
        var request = new byte[FeatLen];
        request[0] = 0;   // report id
        request[3] = 2;   // n[2]
        request[4] = 2;   // n[3]
        request[6] = 131; // n[5] = 0x83

        bool setOk = HidD_SetFeature(handle, request, (uint)request.Length);
        Console.WriteLine($"  SetFeature: {setOk} (err={Marshal.GetLastWin32Error()})");
        Thread.Sleep(100);

        for (int attempt = 0; attempt < 30; attempt++)
        {
            var response = new byte[FeatLen];
            bool getOk = HidD_GetFeature(handle, response, (uint)response.Length);
            if (attempt < 5 || attempt % 5 == 0)
                Console.WriteLine($"  [{attempt}] GetFeature: {getOk}  Response: {BitConverter.ToString(response, 0, 12)}");
            if (response[1] == 0xA1) { Console.WriteLine("  ==> status byte 0xA1 at attempt " + attempt + ", battery bytes: " + response[7] + ", " + response[8]); break; }
            Thread.Sleep(30);
        }
    }
}
