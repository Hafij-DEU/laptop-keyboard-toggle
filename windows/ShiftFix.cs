// ShiftFix: Windows port of the stuck-Shift watchdog from laptop-keyboard-toggle.
//
// Some laptop keyboards lose the "Shift released" signal, so Windows keeps
// acting as if Shift is held. Twice a second this checks whether Windows still
// reports Left or Right Shift as down while no physical key event has arrived.
// A Shift you really hold auto-repeats, so it never looks silent. If both stay
// true for 4 seconds, a Shift key-up is sent to release it.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ShiftFix")]
[assembly: AssemblyProduct("ShiftFix")]
[assembly: AssemblyDescription("Releases a stuck Shift key")]
[assembly: AssemblyCompany("Hafij-BGE")]
[assembly: AssemblyVersion(ShiftFix.Version + ".0")]
[assembly: AssemblyFileVersion(ShiftFix.Version + ".0")]

static class ShiftFix
{
    public const string Version = "1.3.0";
    const double StuckAfter = 4.0;      // seconds Shift down and keyboard silent
    const double RepeatWindow = 60.0;   // stuck again within this -> warn
    const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
    const string AppName = "ShiftFix";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string SettingsKey = @"Software\ShiftFix";

    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static double lastPhysicalKey = 0;   // seconds on Clock
    static double downSince = -1;
    static double lastReset = -1;
    static int fixCount = 0;
    static bool enabled = true;

    static IntPtr hook = IntPtr.Zero;
    static LowLevelKeyboardProc hookProc = HookCallback;  // kept alive for the GC
    static NotifyIcon tray;
    static ToolStripMenuItem statusItem, enabledItem, startupItem;
    static string logPath;

    [STAThread]
    static void Main(string[] args)
    {
        bool created;
        using (var mutex = new Mutex(true, @"Local\ShiftFix-single-instance", out created))
        {
            if (!created) return;

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "log.txt");

            Application.EnableVisualStyles();
            FirstRunStartup();
            hook =SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, hookProc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                Log("could not install keyboard hook: error " + Marshal.GetLastWin32Error());

            BuildTray();
            Log("started");

            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) => Check();
            timer.Start();

            Application.Run();

            timer.Stop();
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            tray.Visible = false;
        }
    }

    // Any key event that came from real hardware (not injected) counts as activity.
    static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            if ((info.flags & 0x10 /* LLKHF_INJECTED */) == 0)
                lastPhysicalKey = Clock.Elapsed.TotalSeconds;
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    static bool IsDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    static void Check()
    {
        double now = Clock.Elapsed.TotalSeconds;
        bool left = IsDown(VK_LSHIFT), right = IsDown(VK_RSHIFT);

        if (!enabled || !(left || right))
        {
            downSince = -1;
            return;
        }
        if (downSince < 0)
        {
            downSince = now;
            return;
        }
        if (now - downSince < StuckAfter || now - lastPhysicalKey < StuckAfter)
            return;

        string names = left && right ? "Left Shift, Right Shift" : left ? "Left Shift" : "Right Shift";
        Release(left, right);
        fixCount++;
        UpdateStatus();

        if (lastReset >= 0 && now - lastReset < RepeatWindow)
        {
            Log(names + " stuck again within " + RepeatWindow + "s: released");
            Notify(names + " keeps getting stuck. It was released again; the key may be faulty.", ToolTipIcon.Warning);
        }
        else
        {
            Log(names + " stuck: released");
            Notify(names + " was stuck. It has been released.", ToolTipIcon.Info);
        }
        lastReset = now;
        downSince = -1;
    }

    static void Release(bool left, bool right)
    {
        var inputs = new System.Collections.Generic.List<INPUT>();
        if (left) inputs.Add(KeyUp(VK_LSHIFT, 0x2A));
        if (right) inputs.Add(KeyUp(VK_RSHIFT, 0x36));
        // Plain VK_SHIFT as well, so apps that only track the generic key see it go up.
        inputs.Add(KeyUp(0x10, 0x2A));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
    }

    static INPUT KeyUp(int vk, ushort scan)
    {
        var i = new INPUT { type = 1 /* INPUT_KEYBOARD */ };
        i.ki.wVk = (ushort)vk;
        i.ki.wScan = scan;
        i.ki.dwFlags = 0x0002; // KEYEVENTF_KEYUP
        return i;
    }

    // ---- tray ----

    static void BuildTray()
    {
        var menu = new ContextMenuStrip();
        statusItem = new ToolStripMenuItem { Enabled = false };
        enabledItem = new ToolStripMenuItem("Auto-fix stuck Shift") { Checked = true, CheckOnClick = true };
        enabledItem.CheckedChanged += (s, e) =>
        {
            enabled = enabledItem.Checked;
            Log("auto-fix " + (enabled ? "on" : "off"));
            UpdateStatus();
        };
        var releaseItem = new ToolStripMenuItem("Release Shift now");
        releaseItem.Click += (s, e) => { Release(true, true); Log("released by hand"); };
        startupItem = new ToolStripMenuItem("Start with Windows") { Checked = IsStartup(), CheckOnClick = true };
        startupItem.CheckedChanged += (s, e) => SetStartup(startupItem.Checked);
        var logItem = new ToolStripMenuItem("Open log");
        logItem.Click += (s, e) => { if (File.Exists(logPath)) Process.Start("notepad.exe", "\"" + logPath + "\""); };
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => Application.Exit();

        menu.Items.AddRange(new ToolStripItem[] {
            statusItem, new ToolStripSeparator(), enabledItem, releaseItem, startupItem,
            new ToolStripSeparator(), logItem, exitItem });

        tray = new NotifyIcon { Icon = MakeIcon(), ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (s, e) => enabledItem.Checked = !enabledItem.Checked;
        UpdateStatus();
    }

    static void UpdateStatus()
    {
        string state = enabled ? "watching" : "paused";
        statusItem.Text = "ShiftFix " + Version + ": " + state + " (" + fixCount + " fixed)";
        tray.Text = "ShiftFix: " + state;
    }

    static void Notify(string text, ToolTipIcon icon)
    {
        tray.ShowBalloonTip(4000, "ShiftFix", text, icon);
    }

    static Icon MakeIcon()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var bg = new SolidBrush(Color.FromArgb(53, 132, 228)))
                g.FillEllipse(bg, 1, 1, 30, 30);
            // Shift arrow
            var arrow = new[] {
                new Point(16, 5), new Point(27, 17), new Point(21, 17), new Point(21, 26),
                new Point(11, 26), new Point(11, 17), new Point(5, 17) };
            g.FillPolygon(Brushes.White, arrow);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---- start with Windows ----

    // Start with Windows is on by default: turned on at the first run, and kept
    // pointing at this exe if the app has moved since (e.g. reinstalled elsewhere).
    static void FirstRunStartup()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
        {
            if (k.GetValue("Configured") == null)
            {
                k.SetValue("Configured", 1);
                SetStartup(true);
            }
            else if (IsStartup())
                SetStartup(true);
        }
    }

    static bool IsStartup()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(AppName) != null;
    }

    static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(AppName, false);
        }
        Log("start with Windows " + (on ? "on" : "off"));
    }

    static void Log(string msg)
    {
        try { File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine); }
        catch (IOException) { }
    }

    // ---- Win32 ----

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    // Padding makes the union as large as MOUSEINPUT, which SendInput expects.
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public KEYBDINPUT ki; public long pad1; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
}
