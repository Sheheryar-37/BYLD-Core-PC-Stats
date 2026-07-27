using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PcStatsMonitor.ViewModels;

namespace PcStatsMonitor.Controls;

/// <summary>
/// Converts <see cref="CurveType"/> to bool for the ToggleButton.
/// Unchecked = Linear, Checked = Custom.
/// </summary>
public class CurveTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is CurveType ct && ct == CurveType.Custom;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? CurveType.Custom : CurveType.Linear;
    }
}

/// <summary>
/// Visible when the bound bool is FALSE, Collapsed when true — the inverse of the
/// built-in BooleanToVisibilityConverter. Used to swap a fan's name label for its
/// inline rename box (client round 17, item 3).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}

/// <summary>
/// Fan control settings panel with card-based layout for Controls and Curves.
/// Handles +/- button clicks to increment/decrement fan parameters,
/// and supports direct click-to-type editing via inline TextBoxes.
/// </summary>
public partial class FanControlView : UserControl
{
    public FanControlView()
    {
        // Register the CurveType converter as a resource before InitializeComponent
        Resources.Add("CurveTypeConverter", new CurveTypeConverter());
        Resources.Add("InverseBoolToVisibilityConverter", new InverseBoolToVisibilityConverter());
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
    /// Speed now uses 1% steps instead of 50 RPM.
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
    /// Handles the LostFocus event for inline-editable TextBoxes.
    /// Selects-all text when the user clicks into the field for easy replacement.
    /// </summary>
    private void InlineEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        // Force binding update on lost focus (belt-and-suspenders)
        if (sender is TextBox tb)
        {
            var binding = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
            binding?.UpdateSource();
        }
    }

    /// <summary>
    /// Applies a delta to the specified FanItemViewModel property.
    /// Ensures values don't go below zero.
    /// </summary>
    private static void AdjustFanParam(FanItemViewModel vm, string? propName, int delta)
    {
        switch (propName)
        {
            case "StepUp": vm.StepUp = Math.Max(0, vm.StepUp + delta); break;
            case "StepDown": vm.StepDown = Math.Max(0, vm.StepDown + delta); break;
            case "StartPercentage": vm.StartPercentage = Math.Max(0, vm.StartPercentage + delta); break;
            case "StopPercentage": vm.StopPercentage = Math.Max(0, vm.StopPercentage + delta); break;
            case "Offset": vm.Offset = vm.Offset + delta; break;
            case "MinimumPercentage": vm.MinimumPercentage = Math.Max(0, vm.MinimumPercentage + delta); break;
            // Static manual speed adjusts in 5% steps and pushes straight to hardware.
            case "SpeedPercentage": vm.SpeedPercentage = Math.Clamp(vm.SpeedPercentage + delta * 5, 0, 100); break;
        }
    }

    /// <summary>
    /// Applies a delta to the specified CurveItemViewModel property.
    /// Temperature adjusts by 1°C, speed by 1% steps.
    /// </summary>
    private static void AdjustCurveParam(CurveItemViewModel vm, string? propName, int delta)
    {
        switch (propName)
        {
            case "MinTemp": vm.MinTemp = Math.Max(0, vm.MinTemp + delta); break;
            case "MaxTemp": vm.MaxTemp = Math.Max(0, vm.MaxTemp + delta); break;
            case "MinSpeed": vm.MinSpeed = Math.Clamp(vm.MinSpeed + delta, 0, 100); break;
            case "MaxSpeed": vm.MaxSpeed = Math.Clamp(vm.MaxSpeed + delta, 0, 100); break;
        }
    }

    #region ══════ Context Menu Handlers ══════

    /// <summary>
    /// Opens the ContextMenu attached to a ⋮ Button on left-click.
    /// </summary>
    private void BtnContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>Opens the colour picker for a single fan and persists the chosen colour,
    /// which the 7" System Cooling screen uses for that fan's spinning icon.</summary>
    private void BtnFanColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not FanItemViewModel fan) return;
        if (DataContext is not FanControlViewModel vm) return;

        var picker = new ColorPickerWindow(fan.IconColorHex) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true)
            vm.SetFanColor(fan, picker.SelectedHex);
    }


    /// <summary>
    /// Resolves a MenuItem's DataContext by walking up to the ContextMenu's placement target.
    /// </summary>
    private static T? GetContextDataContext<T>(object sender) where T : class
    {
        if (sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe)
            return fe.DataContext as T;
        return null;
    }

    /// <summary>Fan ⋮ → Reset to Auto.</summary>
    private void FanContextReset_Click(object sender, RoutedEventArgs e)
    {
        var fan = GetContextDataContext<FanItemViewModel>(sender);
        if (fan != null) fan.IsAuto = true;
    }

    /// <summary>Fan ⋮ → Set Manual at 50%.</summary>
    private void FanContextManual_Click(object sender, RoutedEventArgs e)
    {
        var fan = GetContextDataContext<FanItemViewModel>(sender);
        if (fan != null)
        {
            fan.IsManual = true;
            fan.SpeedPercentage = 50f;
        }
    }

    // ── Fan rename (inline) ─────────────────────────────────────────────────

    /// <summary>Double-click a fan's name to rename it inline.</summary>
    private void FanName_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is FanItemViewModel fan)
            fan.IsEditingName = true;
    }

    /// <summary>Fan ⋮ → Rename… enters inline edit mode.</summary>
    private void FanContextRename_Click(object sender, RoutedEventArgs e)
    {
        var fan = GetContextDataContext<FanItemViewModel>(sender);
        if (fan != null) fan.IsEditingName = true;
    }

    /// <summary>Selects the whole name as soon as the inline rename box appears.</summary>
    private void FanNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsVisible) return;
        box.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Enter commits the new name; Escape cancels without saving.</summary>
    private void FanNameEdit_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) CommitFanName(sender);
        else if (e.Key == System.Windows.Input.Key.Escape) CancelFanNameEdit(sender);
    }

    /// <summary>Commits the rename when focus leaves the box (e.g. clicking elsewhere).</summary>
    private void FanNameEdit_LostFocus(object sender, RoutedEventArgs e) => CommitFanName(sender);

    private void CommitFanName(object sender)
    {
        // LostFocus fires again after Enter/Escape already closed the editor — the guard
        // stops a second, redundant save.
        if (sender is not TextBox box || box.DataContext is not FanItemViewModel fan) return;
        if (!fan.IsEditingName) return;
        (DataContext as FanControlViewModel)?.SetFanName(fan, box.Text);
    }

    private static void CancelFanNameEdit(object sender)
    {
        if (sender is TextBox box && box.DataContext is FanItemViewModel fan)
            fan.IsEditingName = false; // restores DisplayName; nothing persisted
    }

    /// <summary>Curve ⋮ → Duplicate this curve.</summary>
    private void CurveContextDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var curve = GetContextDataContext<CurveItemViewModel>(sender);
        if (curve == null) return;

        var vm = DataContext as FanControlViewModel;
        vm?.DuplicateCurve(curve);
    }

    /// <summary>Curve ⋮ → Delete this curve.</summary>
    private void CurveContextDelete_Click(object sender, RoutedEventArgs e)
    {
        var curve = GetContextDataContext<CurveItemViewModel>(sender);
        if (curve == null) return;

        var vm = DataContext as FanControlViewModel;
        vm?.DeleteCurveCommand.Execute(curve);
    }

    #endregion
}
