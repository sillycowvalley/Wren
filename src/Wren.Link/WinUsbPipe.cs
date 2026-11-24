using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Wren.Link;

/// <summary>An ST-LINK found on USB.</summary>
public sealed record UsbDeviceInfo(string Path, int ProductId) : ProgrammerInfo
{
    public StLinkModel Model => ProductId switch
    {
        0x3748 => StLinkModel.V2,
        0x374B => StLinkModel.V21,
        _ => StLinkModel.V3,
    };

    public override string ToString() => $"ST-LINK {Model} (PID {ProductId:X4})";
}

/// <summary>
/// An ST-LINK through WinUSB, on Windows. ST's ST-LINK driver is WinUSB-based, so this
/// works with it; an ST-LINK given WinUSB by Zadig works too. Devices are looked for
/// through ST's device interface and through the interface every USB device has.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinUsbPipe : IUsbPipe
{
    /// <summary>The device interface ST's WinUSB driver registers for the ST-LINK.</summary>
    public static readonly Guid StLinkInterface = new("DBCE1CD9-A320-4b51-A365-A0C3F3C5FB29");

    /// <summary>GUID_DEVINTERFACE_USB_DEVICE: every USB device has it.</summary>
    public static readonly Guid UsbDeviceInterface = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    public static readonly int[] ProductIds = { 0x3748, 0x374B, 0x374F, 0x3752, 0x3753 };

    private readonly SafeFileHandle file;
    private readonly IntPtr winUsb;
    private readonly byte outPipe;
    private readonly byte inPipe = 0x81;

    private WinUsbPipe(SafeFileHandle file, IntPtr winUsb, StLinkModel model)
    {
        this.file = file;
        this.winUsb = winUsb;
        outPipe = model == StLinkModel.V2 ? (byte)0x02 : (byte)0x01;
        SetTimeout(outPipe, 2000);
        SetTimeout(inPipe, 2000);
    }

    /// <summary>ST-LINKs on USB, by their device paths.</summary>
    public static IReadOnlyList<UsbDeviceInfo> Find()
    {
        var found = new Dictionary<string, UsbDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in new[] { StLinkInterface, UsbDeviceInterface })
        {
            foreach (string path in DevicePaths(guid))
            {
                string lower = path.ToLowerInvariant();
                if (!lower.Contains("vid_0483&pid_")) continue;
                int at = lower.IndexOf("pid_", StringComparison.Ordinal) + 4;
                if (at + 4 > lower.Length || !int.TryParse(lower.AsSpan(at, 4), System.Globalization.NumberStyles.HexNumber, null, out int pid)) continue;
                if (!ProductIds.Contains(pid)) continue;
                // The same device appears under both interfaces: the paths differ only after the
                // last '#' (the interface GUID), so keep the first path for each device.
                int last = lower.LastIndexOf('#');
                string key = last > 0 ? lower[..last] : lower;
                found.TryAdd(key, new UsbDeviceInfo(path, pid));
            }
        }
        return found.Values.ToList();
    }

    /// <summary>Opens an ST-LINK. Fails with a message saying what to do if it is not a WinUSB device.</summary>
    public static WinUsbPipe Open(UsbDeviceInfo device)
    {
        var file = CreateFile(device.Path, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, FileAttributeNormal | FileFlagOverlapped, IntPtr.Zero);
        if (file.IsInvalid)
            throw new LinkException($"Cannot open the ST-LINK: {new Win32Exception(Marshal.GetLastWin32Error()).Message} (is STVP or another program using it?)");
        if (!WinUsb_Initialize(file, out IntPtr handle))
        {
            int error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw new LinkException(
                $"The ST-LINK is not using a WinUSB driver ({new Win32Exception(error).Message}). " +
                "Install ST's ST-LINK USB driver (STSW-LINK009), or give it WinUSB with Zadig.");
        }
        return new WinUsbPipe(file, handle, device.Model);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        byte[] buffer = data.ToArray();
        if (!WinUsb_WritePipe(winUsb, outPipe, buffer, (uint)buffer.Length, out uint sent, IntPtr.Zero) || sent != buffer.Length)
            throw new LinkException($"USB write failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
    }

    public void Read(Span<byte> buffer)
    {
        var chunk = new byte[buffer.Length];
        int done = 0;
        while (done < buffer.Length)
        {
            if (!WinUsb_ReadPipe(winUsb, inPipe, chunk, (uint)(buffer.Length - done), out uint got, IntPtr.Zero))
                throw new LinkException($"USB read failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            if (got == 0) throw new LinkException("USB read returned nothing");
            chunk.AsSpan(0, (int)got).CopyTo(buffer[done..]);
            done += (int)got;
        }
    }

    public void Dispose()
    {
        WinUsb_Free(winUsb);
        file.Dispose();
    }

    private void SetTimeout(byte pipe, uint milliseconds) =>
        WinUsb_SetPipePolicy(winUsb, pipe, PipeTransferTimeout, 4, ref milliseconds);

    // ---- SetupAPI: device paths for an interface GUID ----

    private static IEnumerable<string> DevicePaths(Guid guid)
    {
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1)) yield break;
        try
        {
            var data = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint index = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data); index++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                if (size == 0) continue;
                IntPtr detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on 64-bit, 6 on 32-bit.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero))
                    {
                        string? path = Marshal.PtrToStringUni(detail + 4);
                        if (path != null) yield return path;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    // ---- Native ----

    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80, FileFlagOverlapped = 0x40000000;
    private const uint DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;
    private const uint PipeTransferTimeout = 0x03;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index,
        ref SpDeviceInterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data,
        IntPtr detail, uint size, out uint required, IntPtr info);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle device, out IntPtr handle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr handle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(IntPtr handle, byte pipe, byte[] buffer, uint length,
        out uint transferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(IntPtr handle, byte pipe, byte[] buffer, uint length,
        out uint transferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetPipePolicy(IntPtr handle, byte pipe, uint policy, uint length, ref uint value);
}
