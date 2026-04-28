namespace PcStatsMonitor.Models;

public class HardwareMetrics
{
    public double CpuTemp { get; set; }
    public double CpuLoad { get; set; }
    public double CpuClock { get; set; }

    public double GpuTemp { get; set; }
    public double GpuLoad { get; set; }
    public double GpuClock { get; set; }

    public double RamLoad { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }

    public double MotherboardTemp { get; set; }
    public double FanSpeed { get; set; }

    public double NetworkUp { get; set; }
    public double NetworkDown { get; set; }

    public List<DriveMetrics> Drives { get; set; } = new();
    public List<FanMetric> Fans { get; set; } = new();
}

public class FanMetric
{
    public string Name { get; set; } = string.Empty;
    public double Speed { get; set; }
}

public class DriveMetrics
{
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SizeString { get; set; } = string.Empty;
    public double Load { get; set; }
    public double UsedSpaceGb { get; set; }
    public double TotalSpaceGb { get; set; }
}
