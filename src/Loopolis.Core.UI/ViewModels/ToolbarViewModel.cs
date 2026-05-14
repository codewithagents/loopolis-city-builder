using Loopolis.Core.Grid;

namespace Loopolis.Core.UI.ViewModels;

/// <summary>
/// Pure C# ViewModel for the Toolbar. Controls which zone buttons are enabled
/// based on scenario restrictions, and formats button style strings for Godot nodes.
/// </summary>
public class ToolbarViewModel
{
    public bool ResidentialEnabled { get; set; } = true;
    public bool CommercialEnabled { get; set; } = true;
    public bool IndustrialEnabled { get; set; } = true;

    /// <summary>
    /// Creates a ToolbarViewModel with the appropriate zones disabled based on the
    /// scenario's DisabledZones list. Passing null (sandbox / no restrictions) enables all.
    /// </summary>
    public static ToolbarViewModel FromDisabledZones(IReadOnlyList<ZoneType>? disabled)
    {
        var vm = new ToolbarViewModel();
        if (disabled == null) return vm;
        vm.ResidentialEnabled = !disabled.Contains(ZoneType.Residential);
        vm.CommercialEnabled = !disabled.Contains(ZoneType.Commercial);
        vm.IndustrialEnabled = !disabled.Contains(ZoneType.Industrial);
        return vm;
    }

    // --- Computed style strings (Godot nodes map these to theme variations) ---

    public string ResidentialButtonStyle => ResidentialEnabled ? "normal" : "disabled";
    public string CommercialButtonStyle => CommercialEnabled ? "normal" : "disabled";
    public string IndustrialButtonStyle => IndustrialEnabled ? "normal" : "disabled";
}
