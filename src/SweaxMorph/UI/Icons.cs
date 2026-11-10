using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Threading;

namespace SweaxMorph.UI;

/// <summary>
/// File icons for the result rows, asked of the shell on a thread of their own: the shell may load icon handlers or
/// wake a disk first, and on the UI thread that showed as a hitch right when Morph cheers.
/// </summary>
internal static class Icons
{
    static readonly BlockingCollection<(string path, Action<ImageSource?> done, Dispatcher ui)> Queue = [];
    static Thread? _worker;

    public static void Load(string path, Action<ImageSource?> done)
    {
        if (_worker == null)
        {
            _worker = new Thread(Work) { IsBackground = true, Name = "SweaxMorph.Icons" };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }
        Queue.Add((path, done, Dispatcher.CurrentDispatcher));
    }

    static void Work()
    {
        foreach (var (path, done, ui) in Queue.GetConsumingEnumerable())
        {
            ImageSource? icon = null;
            try { icon = Well.ShellIcon(path); } catch { }
            ui.BeginInvoke(() => done(icon), DispatcherPriority.Background);
        }
    }
}
