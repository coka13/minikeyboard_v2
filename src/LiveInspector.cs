using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Foreground-only Raw Input, filtered to this macropad's USB VID/PID.
// This observes emitted input; it never reads flash, sends HID commands, installs
// a global hook, synthesizes input, or stores observations on disk.
internal sealed class LiveInspector : NativeWindow, IDisposable
{
    public event Action<string, string, bool> Changed;
    public bool Listening { get; private set; }
    readonly Timer timer = new Timer { Interval = 100 };
    readonly List<string> output = new List<string>();
    readonly Dictionary<IntPtr, HashSet<string>> modifiers = new Dictionary<IntPtr, HashSet<string>>();
    DateTime deadline;
    string controlTitle;
    string note;

    [StructLayout(LayoutKind.Sequential)]
    struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)]
    struct Header { public uint Type, Size; public IntPtr Device, WParam; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
    static extern uint DeviceName(IntPtr device, uint command, StringBuilder data, ref uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
    static extern uint DeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
    [DllImport("hid.dll")]
    static extern int HidP_GetUsages(int reportType, ushort page, ushort collection, [Out] ushort[] usages, ref uint count, IntPtr preparsed, byte[] report, uint reportLength);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetKeyNameText(int parameter, StringBuilder name, int size);

    public LiveInspector(Form form)
    {
        AssignHandle(form.Handle);
        timer.Tick += delegate {
            if (Listening && DateTime.UtcNow >= deadline)
                Finish(output.Count == 0 ? "No macropad output detected. Keep Studio focused and try again." : "Captured real device output · nothing saved or written to the keyboard.");
        };
    }

    static RawDevice[] Devices(IntPtr window, uint flags)
    {
        return new[] {
            new RawDevice { Page = 1, Usage = 6, Target = window, Flags = flags },
            new RawDevice { Page = 1, Usage = 2, Target = window, Flags = flags },
            new RawDevice { Page = 12, Usage = 1, Target = window, Flags = flags }
        };
    }

    public void Start(string title)
    {
        controlTitle = title;
        output.Clear();
        modifiers.Clear();
        note = "Press that physical key or dial. Keep Studio focused. Shortcuts still run in Windows.";
        // Flags=0 explicitly excludes background delivery and leaves normal input
        // processing alone. Register only for the duration of an inspection.
        if (!RegisterRawInputDevices(Devices(Handle, 0), 3, (uint)Marshal.SizeOf(typeof(RawDevice))))
        {
            Finish("Could not start inspection: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            return;
        }
        Listening = true;
        deadline = DateTime.UtcNow.AddSeconds(10);
        timer.Start();
        Publish("Waiting for " + title + "…", note);
    }

    public void Cancel()
    {
        if (Listening) Finish("Inspection stopped · nothing saved.");
    }

    void Finish(string detail)
    {
        Listening = false;
        timer.Stop();
        RegisterRawInputDevices(Devices(IntPtr.Zero, 1), 3, (uint)Marshal.SizeOf(typeof(RawDevice)));
        Publish(output.Count == 0 ? "No output captured" : String.Join(" → ", output.ToArray()), detail);
        modifiers.Clear();
    }

    void Publish(string value, string detail)
    {
        if (Changed != null) Changed(value, detail, Listening);
    }

    void Add(string value)
    {
        if (output.Count >= 32) { Finish("Capture limit reached · nothing saved."); return; }
        output.Add(value);
        // Leave time to collect a multi-step macro, rather than guessing that its
        // first emitted key represents the complete assignment.
        Publish(String.Join(" → ", output.ToArray()), controlTitle + " · live USB output · " + note);
    }

    internal static bool MatchesDevice(string name)
    {
        return name != null && name.IndexOf("VID_1189&PID_8890", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x00FF && Listening)
        {
            try { Read(message.LParam); }
            catch (Exception error) { Finish("Inspection failed: " + error.Message); }
        }
        // DefWindowProc must still receive foreground WM_INPUT for cleanup.
        base.WndProc(ref message);
    }

    void Read(IntPtr handle)
    {
        uint size = 0, headerSize = (uint)Marshal.SizeOf(typeof(Header));
        if (GetRawInputData(handle, 0x10000003, IntPtr.Zero, ref size, headerSize) == UInt32.MaxValue || size < headerSize || size > 65536) return;
        IntPtr packet = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(handle, 0x10000003, packet, ref size, headerSize) != size) return;
            Header header = (Header)Marshal.PtrToStructure(packet, typeof(Header));
            uint characters = 0;
            if (DeviceName(header.Device, 0x20000007, null, ref characters) == UInt32.MaxValue || characters == 0 || characters > 32768) return;
            StringBuilder name = new StringBuilder((int)characters + 1);
            if (DeviceName(header.Device, 0x20000007, name, ref characters) == UInt32.MaxValue || !MatchesDevice(name.ToString())) return;
            byte[] data = new byte[size - headerSize];
            Marshal.Copy(IntPtr.Add(packet, (int)headerSize), data, 0, data.Length);
            if (header.Type == 1) Keyboard(header.Device, data);
            else if (header.Type == 0) Mouse(data);
            else if (header.Type == 2) Consumer(header.Device, data);
        }
        finally { Marshal.FreeHGlobal(packet); }
    }

    void Keyboard(IntPtr device, byte[] data)
    {
        if (data.Length < 16) return;
        ushort scan = BitConverter.ToUInt16(data, 0), flags = BitConverter.ToUInt16(data, 2), key = BitConverter.ToUInt16(data, 6);
        if (key == 255 || scan == 255) return;
        HashSet<string> held;
        if (!modifiers.TryGetValue(device, out held)) modifiers.Add(device, held = new HashSet<string>());
        string modifier = Modifier(key, scan, flags);
        bool released = (flags & 1) != 0;
        if (modifier != null)
        {
            if (released) held.Remove(modifier); else held.Add(modifier);
            Add(modifier + (released ? " ↑" : " ↓"));
            return;
        }
        if (released) return;
        StringBuilder keyName = new StringBuilder(128);
        GetKeyNameText((scan << 16) | ((flags & 2) != 0 ? 1 << 24 : 0), keyName, keyName.Capacity);
        List<string> chord = new List<string>(held);
        chord.Sort(StringComparer.Ordinal);
        chord.Add(keyName.Length > 0 ? keyName.ToString() : ((Keys)key).ToString());
        Add(String.Join(" + ", chord.ToArray()));
    }

    internal static string Modifier(ushort key, ushort scan, ushort flags)
    {
        if (key == 0xA0 || key == 0xA1) return key == 0xA0 ? "Shift" : "Right Shift";
        if (key == 0xA2 || key == 0xA3) return key == 0xA2 ? "Ctrl" : "Right Ctrl";
        if (key == 0xA4 || key == 0xA5) return key == 0xA4 ? "Alt" : "Right Alt";
        if (key == 0x10) return scan == 0x36 ? "Right Shift" : "Shift";
        if (key == 0x11) return (flags & 2) != 0 ? "Right Ctrl" : "Ctrl";
        if (key == 0x12) return (flags & 2) != 0 ? "Right Alt" : "Alt";
        if (key == 0x5B || key == 0x5C) return key == 0x5B ? "Win" : "Right Win";
        return null;
    }

    void Mouse(byte[] data)
    {
        if (data.Length < 24) return;
        ushort flags = BitConverter.ToUInt16(data, 4);
        if ((flags & 1) != 0) Add("Left click");
        if ((flags & 4) != 0) Add("Right click");
        if ((flags & 16) != 0) Add("Middle click");
        if ((flags & 64) != 0) Add("Mouse X1");
        if ((flags & 256) != 0) Add("Mouse X2");
        if ((flags & 1024) != 0) Add("Wheel " + BitConverter.ToInt16(data, 6));
        if ((flags & 2048) != 0) Add("Horizontal wheel " + BitConverter.ToInt16(data, 6));
        int x = BitConverter.ToInt32(data, 12), y = BitConverter.ToInt32(data, 16);
        if (x != 0 || y != 0) Add("Mouse move " + x + ", " + y);
    }

    void Consumer(IntPtr device, byte[] data)
    {
        if (data.Length < 8) return;
        uint length = BitConverter.ToUInt32(data, 0), reports = BitConverter.ToUInt32(data, 4);
        if (length == 0 || length > 65536 || reports > (data.Length - 8) / length) return;
        uint size = 0;
        if (DeviceInfo(device, 0x20000005, IntPtr.Zero, ref size) == UInt32.MaxValue || size == 0 || size > 65536) return;
        IntPtr preparsed = Marshal.AllocHGlobal((int)size);
        try
        {
            if (DeviceInfo(device, 0x20000005, preparsed, ref size) == UInt32.MaxValue) return;
            for (int i = 0; i < reports; i++)
            {
                byte[] report = new byte[length];
                Array.Copy(data, 8 + i * length, report, 0, length);
                ushort[] usages = new ushort[128]; uint count = 128;
                int result = HidP_GetUsages(0, 12, 0, usages, ref count, preparsed, report, length);
                if (result == 0x110000)
                {
                    for (int j = 0; j < Math.Min(count, usages.Length); j++) if (usages[j] != 0) Add(ConsumerName(usages[j]));
                }
                else Add("HID report " + BitConverter.ToString(report));
            }
        }
        finally { Marshal.FreeHGlobal(preparsed); }
    }

    internal static string ConsumerName(ushort usage)
    {
        switch (usage) {
            case 0xB5: return "Next track"; case 0xB6: return "Previous track";
            case 0xB7: return "Stop"; case 0xCD: return "Play / pause";
            case 0xE2: return "Mute"; case 0xE9: return "Volume up"; case 0xEA: return "Volume down";
            default: return "Consumer usage 0x" + usage.ToString("X4");
        }
    }

    public void Dispose()
    {
        if (Listening) Cancel();
        timer.Dispose();
        ReleaseHandle();
    }
}
