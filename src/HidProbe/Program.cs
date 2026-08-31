using HidSharp;

int VendorId = args.Length > 0 ? Convert.ToInt32(args[0], 16) : 0x1532;
int ProductId = args.Length > 1 ? Convert.ToInt32(args[1], 16) : 0x00C1;

Console.WriteLine("Enumerating HID devices for VID_{0:X4} PID_{1:X4}...", VendorId, ProductId);

var candidates = DeviceList.Local.GetHidDevices(vendorID: VendorId)
    .Where(d => d.ProductID == ProductId)
    .ToList();

Console.WriteLine($"Found {candidates.Count} collection(s).");
foreach (var d in candidates)
{
    Console.WriteLine($"- Path={d.DevicePath}  In={d.GetMaxInputReportLength()} Out={d.GetMaxOutputReportLength()} Feat={d.GetMaxFeatureReportLength()}");
}

// Also list ALL Razer VID devices, in case the PID differs from what we expect.
Console.WriteLine("\nAll VID_1532 devices on this system:");
foreach (var d in DeviceList.Local.GetHidDevices(vendorID: 0x1532))
{
    string name = "?";
    try { name = d.GetProductName(); } catch { }
    Console.WriteLine($"- PID_{d.ProductID:X4} \"{name}\"  In={d.GetMaxInputReportLength()} Out={d.GetMaxOutputReportLength()} Feat={d.GetMaxFeatureReportLength()}  Path={d.DevicePath}");
}
