# Tutorial: Creating a "Simple System Clock" Plugin

This tutorial walks you through creating a simple clock widget that displays the current time on the BYLD Core dashboard.

### Step 1: Create the Project
1. Create a new **WPF Class Library** in Visual Studio.
2. Name it `PcStatsMonitor.Plugins.Clock`.
3. Set the target framework to **.NET 9.0-windows**.
4. Add a reference to `PcStatsMonitor.PluginApi.dll`.

### Step 2: Create the UI
Create a new `UserControl` called `ClockWidget.xaml`:

```xml
<UserControl x:Class="PcStatsMonitor.Plugins.Clock.ClockWidget"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="192" Height="60">
    <Border Background="#11FFFFFF" CornerRadius="12" BorderBrush="#3b82f6" BorderThickness="1">
        <Grid>
            <TextBlock x:Name="TxtTime" Text="00:00:00" Foreground="White" 
                       FontSize="20" FontWeight="Bold" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            <TextBlock Text="SYSTEM TIME" Foreground="#3b82f6" Opacity="0.5" 
                       FontSize="8" HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,5,0,0"/>
        </Grid>
    </Border>
</UserControl>
```

Update its codebehind `ClockWidget.xaml.cs` to add a public method to set the time:
```csharp
public void UpdateTime(string time) => TxtTime.Text = time;
```

### Step 3: Implement the Plugin Entry Point
Create a class named `ClockPlugin.cs`:

```csharp
using PcStatsMonitor.PluginApi;
using System.Windows.Controls;

namespace PcStatsMonitor.Plugins.Clock
{
    public class ClockPlugin : IWidgetPlugin
    {
        public string Name => "Simple Clock";
        public string Version => "1.0.0";
        public string Author => "BYLD Dev";

        private ClockWidget _ui;

        public void Initialize(dynamic timerService)
        {
            _ui = new ClockWidget();
        }

        public void Update(dynamic hardwareData)
        {
            // We ignore hardwareData since we just want the time
            string timeString = System.DateTime.Now.ToString("HH:mm:ss");
            
            // Dispatch to UI thread
            _ui.Dispatcher.Invoke(() => _ui.UpdateTime(timeString));
        }

        public UserControl GetUI() => _ui;
    }
}
```

### Step 4: Build and Install
1. **Build** your project.
2. Locate the `PcStatsMonitor.Plugins.Clock.dll` in your `bin\Debug` folder.
3. Copy it to the `\Plugins` folder of the main application.
4. Launch **BYLD Core**. Your clock will now appear instantly!
