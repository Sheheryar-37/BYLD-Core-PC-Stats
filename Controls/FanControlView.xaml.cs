using System.Windows;
using System.Windows.Controls;
using PcStatsMonitor.ViewModels;

namespace PcStatsMonitor.Controls;

/// <summary>
/// Fan control settings panel with card-based layout for Controls and Curves.
/// Handles +/- button clicks to increment/decrement fan parameters.
/// </summary>
public partial class FanControlView : UserControl
{
    public FanControlView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Increments a fan parameter by 1 based on the Button.Tag property name.
    /// </summary>
    private void BtnParamPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not FanItemViewModel vm) return;
        AdjustFanParam(vm, btn.Tag?.ToString(), 1);
    }

    /// <summary>
    /// Decrements a fan parameter by 1 based on the Button.Tag property name.
    /// </summary>
    private void BtnParamMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not FanItemViewModel vm) return;
        AdjustFanParam(vm, btn.Tag?.ToString(), -1);
    }

    /// <summary>
    /// Increments a curve parameter by the appropriate step.
    /// </summary>
    private void BtnCurveParamPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not CurveItemViewModel vm) return;
        AdjustCurveParam(vm, btn.Tag?.ToString(), 1);
    }

    /// <summary>
    /// Decrements a curve parameter by the appropriate step.
    /// </summary>
    private void BtnCurveParamMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not CurveItemViewModel vm) return;
        AdjustCurveParam(vm, btn.Tag?.ToString(), -1);
    }

    /// <summary>
    /// Applies a delta to the specified FanItemViewModel property.
    /// Ensures values don't go below zero.
    /// </summary>
    private static void AdjustFanParam(FanItemViewModel vm, string? propName, int delta)
    {
        switch (propName)
        {
            case "StepUp": vm.StepUp = System.Math.Max(0, vm.StepUp + delta); break;
            case "StepDown": vm.StepDown = System.Math.Max(0, vm.StepDown + delta); break;
            case "StartPercentage": vm.StartPercentage = System.Math.Max(0, vm.StartPercentage + delta); break;
            case "StopPercentage": vm.StopPercentage = System.Math.Max(0, vm.StopPercentage + delta); break;
            case "Offset": vm.Offset = vm.Offset + delta; break;
            case "MinimumPercentage": vm.MinimumPercentage = System.Math.Max(0, vm.MinimumPercentage + delta); break;
        }
    }

    /// <summary>
    /// Applies a delta to the specified CurveItemViewModel property.
    /// Temperature adjusts by 1°C, speed by 50 RPM steps.
    /// </summary>
    private static void AdjustCurveParam(CurveItemViewModel vm, string? propName, int delta)
    {
        switch (propName)
        {
            case "MinTemp": vm.MinTemp = System.Math.Max(0, vm.MinTemp + delta); break;
            case "MaxTemp": vm.MaxTemp = System.Math.Max(0, vm.MaxTemp + delta); break;
            case "MinSpeed": vm.MinSpeed = System.Math.Max(0, vm.MinSpeed + (delta * 50)); break;
            case "MaxSpeed": vm.MaxSpeed = System.Math.Max(0, vm.MaxSpeed + (delta * 50)); break;
        }
    }
}
