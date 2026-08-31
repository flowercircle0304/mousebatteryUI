using HidSharp;

int VendorId = args.Length > 0 ? Convert.ToInt32(args[0], 16) : 0x1D57;
int ProductId = args.Length > 1 ? Convert.ToInt32(args[1], 16) : 0xFA60;

Console.WriteLine("Enumerating HID devices for VID_{0:X4} PID_{1:X4}...", VendorId, ProductId);

var candidates = DeviceList.Local.GetHidDevices(vendorID: VendorId)
    .Where(d => d.ProductID == ProductId)
    .ToList();

foreach (var d in candidates)
{
    Console.WriteLine($"- Path={d.DevicePath}  In={d.GetMaxInputReportLength()} Out={d.GetMaxOutputReportLength()} Feat={d.GetMaxFeatureReportLength()}");
}

var target = candidates.FirstOrDefault(d => d.GetMaxInputReportLength() == 5 && d.GetMaxOutputReportLength() == 0 && d.GetMaxFeatureReportLength() == 0);
if (target is null)
{
    Console.WriteLine("No collection with InputReportLength==5 found.");
    return;
}

Console.WriteLine($"\nOpening: {target.DevicePath}");
if (!target.TryOpen(out var stream))
{
    Console.WriteLine("Could not open (in use?).");
    return;
}

using (stream)
{
    stream.ReadTimeout = 20000;
    var buf = new byte[target.GetMaxInputReportLength()];
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.Elapsed < TimeSpan.FromSeconds(15))
    {
        try
        {
            int n = stream.Read(buf);
            Console.WriteLine($"[{sw.Elapsed:mm\\:ss\\.fff}] ({n} bytes) " + BitConverter.ToString(buf, 0, n) + $"   battery(byte4)={(n > 4 ? buf[4].ToString() : "?")}");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("  (timeout waiting for report)");
        }
    }
}

Console.WriteLine("Done.");
