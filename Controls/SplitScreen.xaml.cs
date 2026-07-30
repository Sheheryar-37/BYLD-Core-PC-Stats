using System.Windows.Controls;

namespace PcStatsMonitor.Controls;

/// <summary>
/// A combined 7" screen showing system stats in the upper half and cooling in the lower half,
/// for users who would rather see both at once than wait for the rotation (client round 18,
/// item 16). Binds to the same MainViewModel state as the dedicated screens, so the fan list
/// honours the user's per-fan visibility and ordering choices.
/// </summary>
public partial class SplitScreen : UserControl
{
    public SplitScreen()
    {
        InitializeComponent();
    }
}
