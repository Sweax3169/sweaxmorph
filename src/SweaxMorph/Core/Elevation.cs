using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SweaxMorph.Native;

namespace SweaxMorph.Core;

/// <summary>
/// "Also over administrator windows". Windows keeps the keys typed into an administrator window (Task Manager, an
/// installer, an elevated terminal, many games) away from ordinary apps, so Morph's keyboard hook never hears the
/// shortcut there. SweaxMorph itself stays an ordinary app — an administrator Morph could not take files dragged from
/// an ordinary Explorer — and a tiny helper (SweaxMorph.exe --keys-helper) with administrator rights listens instead,
/// only while such a window is in front, and passes the shortcut on. It starts from a sign-in task "with highest
/// privileges": Windows asks once, when the task is made, never again. Turning the option off removes the task.
/// </summary>
public static class Elevation
{
    public const string TaskName = "SweaxMorph Keys";

    public static bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SweaxMorph.exe");

    public static bool TaskExists() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>The helper is running (its mutex exists).</summary>
    public static bool HelperRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(KeysHelper.MutexName + Ipc.Suffix, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException) { return true; } // it exists, just not open to us
    }

    /// <summary>Makes the task (Windows asks for permission) and starts the helper. False if refused or it didn't start.</summary>
    public static async Task<(bool ok, bool refused)> Enable()
    {
        string xml = Path.Combine(Path.GetTempPath(), $"sweaxmorph-task-{Environment.ProcessId}.xml");
        File.WriteAllText(xml, TaskXml(admin: true), Encoding.Unicode);
        try
        {
            string args = $"/Create /TN \"{TaskName}\" /XML \"{xml}\" /F";
            int code;
            if (IsElevated) code = await Task.Run(() => Schtasks(args));
            else
            {
                var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using var p = Process.Start(psi)!;
                await p.WaitForExitAsync();
                code = p.ExitCode;
            }
            if (code != 0) { Paths.Log($"admin helper: schtasks /Create exited {code}"); return (false, false); }
        }
        catch (Win32Exception) { return (false, true); } // the permission prompt was declined
        catch (Exception ex) { Paths.Log("admin helper: " + ex.Message); return (false, false); }
        finally { try { File.Delete(xml); } catch { } }
        return (await StartHelper(), false);
    }

    /// <summary>Starts the helper through its task (no prompt: the task already has the rights) and waits for it.</summary>
    public static async Task<bool> StartHelper()
    {
        if (HelperRunning()) return true;
        if (Schtasks($"/Run /TN \"{TaskName}\"") != 0) { Paths.Log("admin helper: the task could not be started"); return false; }
        for (int i = 0; i < 40 && !HelperRunning(); i++) await Task.Delay(100);
        bool ok = HelperRunning();
        if (!ok) Paths.Log("admin helper: started, but no helper answered");
        return ok;
    }

    /// <summary>Stops the helper and removes the task (asking Windows only if the task can't be removed otherwise).</summary>
    public static async Task Disable()
    {
        StopHelper();
        if (!TaskExists()) return;
        if (await Task.Run(() => Schtasks($"/Delete /TN \"{TaskName}\" /F")) == 0) return;
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
        }
        catch (Exception ex) { Paths.Log("admin helper: removing the task: " + ex.Message); }
    }

    public static void StopHelper()
    {
        try { if (EventWaitHandle.TryOpenExisting(KeysHelper.QuitName + Ipc.Suffix, out var quit)) { quit.Set(); quit.Dispose(); } }
        catch (Exception ex) { Paths.Log("admin helper: stop: " + ex.Message); }
    }

    static int Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            return p.WaitForExit(8000) ? p.ExitCode : -1;
        }
        catch (Exception ex) { Paths.Log("schtasks: " + ex.Message); return -1; }
    }

    /// <summary>
    /// The task: at this user's sign-in, start the helper. Unlike schtasks' defaults it is never stopped after 72 hours,
    /// runs at normal priority (Task Scheduler otherwise starts programs below normal), and runs on battery too.
    /// </summary>
    internal static string TaskXml(bool admin, string? name = null)
    {
        string user = WindowsIdentity.GetCurrent().User!.Value;
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>SweaxMorph: hears the shortcut over administrator windows too and passes it to SweaxMorph.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>{(admin ? "HighestAvailable" : "LeastPrivilege")}</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(Exe)}</Command><Arguments>--keys-helper{(name != null ? " --instance " + name : "")}</Arguments></Exec></Actions>
            </Task>
            """;
    }
}

/// <summary>
/// The administrator helper's process: a keyboard hook and nothing else. It acts only while the window in front has
/// more rights than an ordinary app (where SweaxMorph's own hook is deaf), and lives as long as SweaxMorph does.
/// </summary>
public static class KeysHelper
{
    public const string MutexName = "SweaxMorph.Keys.3b9e";
    public const string QuitName = "SweaxMorph.Keys.Quit.3b9e";

    public static void Run(string[] args)
    {
        string suffix = Launch.Suffix(args);
        Ipc.Suffix = suffix;
        using var me = Shared.Create(MutexName + suffix, mutex: true, out bool first);
        if (!first) return;
        using var quit = Shared.Create(QuitName + suffix, mutex: false, out _);
        if (quit == null) return;
        string? portable = Path.Combine(AppContext.BaseDirectory, "sweaxmorph-data");
        Paths.Init(Launch.Arg(args, "--data") ?? (Directory.Exists(portable) ? portable : null));
        string file = Path.Combine(Paths.Data, "settings.json");

        using var hook = new KeyboardHook { DragTrigger = false, Gate = AboveOrdinary };
        hook.Pressed += () => ThreadPool.QueueUserWorkItem(_ =>
        {
            // this helper heard the key: a no-op input makes it the latest source of input, so it may let SweaxMorph come to the front
            Win32.Send(new Win32.INPUT { type = Win32.INPUT_MOUSE, U = new Win32.InputUnion { mi = new Win32.MOUSEINPUT { dwExtraInfo = Win32.SweaxMark } } });
            Ipc.Send("toggle-admin", []);
        });
        DateTime stamp = default;
        void Reload()
        {
            try
            {
                var t = File.GetLastWriteTimeUtc(file);
                if (t == stamp) return;
                stamp = t;
                var s = JsonRead(file);
                hook.Hotkey = Hotkey.Parse(s.Hotkey);
                hook.Enabled = s.HotkeyEnabled;
            }
            catch (Exception ex) { Paths.Log("admin helper: settings: " + ex.Message); }
        }
        Reload();
        Paths.Log($"admin helper running (rights: {(Elevation.IsElevated ? "administrator" : "ordinary")})");

        // at sign-in the helper may come before SweaxMorph: wait for it a while, then live exactly as long as it does
        long started = Environment.TickCount64;
        bool seen = false;
        int gone = 0, ticks = 0;
        while (!quit.WaitOne(1000))
        {
            bool alive = Mutex.TryOpenExisting(Launch.InstanceName + suffix, out var m);
            m?.Dispose();
            if (alive) { seen = true; gone = 0; }
            else if (seen) gone++;
            if (gone >= 3 || (!seen && Environment.TickCount64 - started > 120_000)) break;
            Reload();
            if (++ticks % 20 == 0) hook.Rearm();
        }
        Paths.Log("admin helper stopped");
    }

    /// <summary>The settings, read without writing anything back (the helper never changes them).</summary>
    static Settings JsonRead(string file) =>
        System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(file),
            new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) ?? new Settings();

    static uint _pid;
    static bool _above;
    static long _at;

    /// <summary>The window in front belongs to a process with more rights than an ordinary app: there SweaxMorph can't hear the shortcut.</summary>
    static bool AboveOrdinary()
    {
        Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out uint pid);
        long now = Environment.TickCount64;
        if (pid != _pid || now - _at > 2000)
        {
            _pid = pid;
            _at = now;
            int level = Win32.IntegrityOf(pid);
            _above = level == -1 || level > 0x2000; // unreadable means protected, which is above us too
        }
        return _above;
    }
}

/// <summary>
/// A named mutex or event the helper makes with administrator rights but leaves open to this user's ordinary
/// processes (by default an object made with administrator rights refuses them), so SweaxMorph can see it and stop it.
/// </summary>
internal static class Shared
{
    const uint ERROR_ALREADY_EXISTS = 183;

    [StructLayout(LayoutKind.Sequential)]
    struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr sd, out uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateMutex(ref SECURITY_ATTRIBUTES sa, bool initialOwner, string name);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateEvent(ref SECURITY_ATTRIBUTES sa, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr mem);

    /// <summary>Full access for the system, administrators and this user; medium integrity, so ordinary processes may open it.</summary>
    static string Sddl => $"D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{WindowsIdentity.GetCurrent().User!.Value})S:(ML;;NW;;;ME)";

    sealed class Native : WaitHandle
    {
        public Native(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, true);
    }

    public static WaitHandle? Create(string name, bool mutex, out bool first)
    {
        first = false;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(Sddl, 1, out IntPtr sd, out _)) return null;
        try
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            IntPtr h = mutex ? CreateMutex(ref sa, true, name) : CreateEvent(ref sa, false, false, name);
            if (h == IntPtr.Zero) return null;
            first = Marshal.GetLastWin32Error() != ERROR_ALREADY_EXISTS;
            return new Native(h);
        }
        finally { LocalFree(sd); }
    }
}
