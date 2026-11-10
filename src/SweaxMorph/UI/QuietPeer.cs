using System.Windows;
using System.Windows.Automation.Peers;

namespace SweaxMorph.UI;

/// <summary>
/// UI Automation view of the Well that shows only the window itself. This PC (like many) runs programs that listen to
/// UI Automation — the touch keyboard and emoji panel, PowerToys, screen tools — and once one of them looks at a WPF
/// window, WPF walks every element of it after each layout pass to report changes. Learned in SweaxClip, where that
/// walk cost tens to hundreds of milliseconds per frame.
/// </summary>
public sealed class QuietPeer(FrameworkElement owner, string name) : FrameworkElementAutomationPeer(owner)
{
    protected override List<AutomationPeer>? GetChildrenCore() => null;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Window;
    protected override string GetClassNameCore() => "SweaxMorph";
    protected override string GetNameCore() => name;
}
