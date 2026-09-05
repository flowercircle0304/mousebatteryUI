using HidSharp;

const int VendorId = 0x373B;
const int ProductId = 0x1031;
const int ReportLength = 17;
const byte OutputReportId = 8;

var target = DeviceList.Local.GetHidDevices(vendorID: VendorId)
    .FirstOrDefault(d => d.ProductID == ProductId
        && d.GetMaxOutputReportLength() == ReportLength
        && d.GetMaxInputReportLength() == ReportLength);

if (target is null) { Console.WriteLine("Target collection not found."); return; }

Console.WriteLine($"Target: {target.DevicePath}");
if (!target.TryOpen(out var stream)) { Console.WriteLine("TryOpen failed."); return; }

using (stream)
{
    stream.ReadTimeout = 700;
    stream.WriteTimeout = 1000;

    // Sweep several candidate commands: battery(4), read-eeprom(8) reading the
    // "system" register (addr 0, len 6) per OpenMouse's ATK layout, get-connect(3),
    // get-cur-profile(0x0e), get-version(0x12), plus a few neighbors.
    byte[] commandsToTry = { 4, 8, 3, 0x0e, 0x12, 1, 2, 5, 6, 7 };

    foreach (var cmd in commandsToTry)
    {
        var outBuf = new byte[ReportLength];
        outBuf[0] = OutputReportId;
        outBuf[1] = cmd;
        if (cmd == 8) // ReadEEPROM body: [0, hi, lo, len, ...] after the command byte
        {
            outBuf[2] = 0; outBuf[3] = 0; outBuf[4] = 0; outBuf[5] = 6;
        }
        int sum = OutputReportId;
        for (int i = 1; i < ReportLength - 1; i++) sum += outBuf[i];
        outBuf[ReportLength - 1] = unchecked((byte)(0x55 - sum));

        Console.WriteLine($"--- cmd=0x{cmd:X2} Write: " + BitConverter.ToString(outBuf));
        try { stream.Write(outBuf); }
        catch (Exception ex) { Console.WriteLine("  Write failed: " + ex.Message); continue; }

        bool gotReply = false;
        for (int attempt = 0; attempt < 3 && !gotReply; attempt++)
        {
            try
            {
                var inBuf = new byte[ReportLength];
                int n = stream.Read(inBuf);
                Console.WriteLine($"  [{attempt}] Read {n} bytes: " + BitConverter.ToString(inBuf, 0, n));
                gotReply = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{attempt}] Read failed: {ex.Message}");
            }
        }
    }
}
