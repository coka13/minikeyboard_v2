using System;
using System.Reflection;
using System.Windows.Forms;

class LiveInspectorTests
{
    static int assertions;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        assertions++;
    }
    [STAThread]
    static void Main()
    {
        Check(LiveInspector.MatchesDevice(@"\\?\HID#VID_1189&PID_8890&MI_00#test"), "Accept the macropad.");
        Check(LiveInspector.MatchesDevice("hid#vid_1189&pid_8890"), "Ignore device-path casing.");
        Check(!LiveInspector.MatchesDevice("hid#vid_046d&pid_c31c"), "Exclude other keyboards.");
        Check(!LiveInspector.MatchesDevice(null), "Exclude missing device identities.");
        Check(LiveInspector.Modifier(0x11, 0x1d, 0) == "Ctrl", "Decode left Ctrl.");
        Check(LiveInspector.Modifier(0x11, 0x1d, 2) == "Right Ctrl", "Preserve extended modifiers.");
        Check(LiveInspector.Modifier(0x10, 0x36, 0) == "Right Shift", "Preserve right Shift.");
        Check(LiveInspector.ConsumerName(0xcd) == "Play / pause", "Decode a consumer media usage.");
        Check(LiveInspector.ConsumerName(0x999) == "Consumer usage 0x0999", "Expose unknown consumer usages accurately.");
        using (Form form = new Form())
        using (LiveInspector inspector = new LiveInspector(form))
        {
            string latest = null;
            inspector.Changed += delegate(string value, string detail, bool listening) { latest = value; };
            MethodInfo keyboard = typeof(LiveInspector).GetMethod("Keyboard", BindingFlags.Instance | BindingFlags.NonPublic);
            byte[] ctrl = Packet(0x1d, 0, 0x11);
            keyboard.Invoke(inspector, new object[] { new IntPtr(1), ctrl });
            keyboard.Invoke(inspector, new object[] { new IntPtr(1), Packet(0x2e, 0, 0x43) });
            Check(latest.Contains("Ctrl + C"), "Capture the emitted Ctrl+C chord.");
            keyboard.Invoke(inspector, new object[] { new IntPtr(1), Packet(0x1d, 1, 0x11) });
            keyboard.Invoke(inspector, new object[] { new IntPtr(1), Packet(0x2e, 0, 0x43) });
            Check(latest.EndsWith(" → C"), "Release modifiers before the next key.");
            string before = latest;
            keyboard.Invoke(inspector, new object[] { new IntPtr(1), new byte[2] });
            Check(latest == before, "Ignore truncated raw keyboard data.");
            keyboard.Invoke(inspector, new object[] { new IntPtr(2), Packet(0x2e, 0, 0x43) });
            Check(latest.EndsWith(" → C"), "Keep modifier state separate for each device.");
        }
        Console.WriteLine(assertions + " assertions passed.");
    }
    static byte[] Packet(ushort scan, ushort flags, ushort key)
    {
        byte[] data = new byte[16];
        Array.Copy(BitConverter.GetBytes(scan), 0, data, 0, 2);
        Array.Copy(BitConverter.GetBytes(flags), 0, data, 2, 2);
        Array.Copy(BitConverter.GetBytes(key), 0, data, 6, 2);
        return data;
    }
}
