using Loopolis.Core.Grid;
using Loopolis.Core.UI.ViewModels;

namespace Loopolis.Core.UI.Tests.ViewModels;

[TestFixture]
public class ToolbarViewModelTests
{
    // ---- FromDisabledZones factory ----

    [Test]
    public void FromDisabledZones_Null_AllEnabled()
    {
        var vm = ToolbarViewModel.FromDisabledZones(null);
        Assert.That(vm.ResidentialEnabled, Is.True);
        Assert.That(vm.CommercialEnabled, Is.True);
        Assert.That(vm.IndustrialEnabled, Is.True);
    }

    [Test]
    public void FromDisabledZones_EmptyList_AllEnabled()
    {
        var vm = ToolbarViewModel.FromDisabledZones(Array.Empty<ZoneType>());
        Assert.That(vm.ResidentialEnabled, Is.True);
        Assert.That(vm.CommercialEnabled, Is.True);
        Assert.That(vm.IndustrialEnabled, Is.True);
    }

    [Test]
    public void FromDisabledZones_Industrial_DisablesIndustrial()
    {
        var vm = ToolbarViewModel.FromDisabledZones(new[] { ZoneType.Industrial });
        Assert.That(vm.IndustrialEnabled, Is.False);
    }

    [Test]
    public void FromDisabledZones_Industrial_LeavesOthersEnabled()
    {
        var vm = ToolbarViewModel.FromDisabledZones(new[] { ZoneType.Industrial });
        Assert.That(vm.ResidentialEnabled, Is.True);
        Assert.That(vm.CommercialEnabled, Is.True);
    }

    [Test]
    public void FromDisabledZones_Commercial_DisablesCommercial()
    {
        var vm = ToolbarViewModel.FromDisabledZones(new[] { ZoneType.Commercial });
        Assert.That(vm.CommercialEnabled, Is.False);
    }

    [Test]
    public void FromDisabledZones_Residential_DisablesResidential()
    {
        var vm = ToolbarViewModel.FromDisabledZones(new[] { ZoneType.Residential });
        Assert.That(vm.ResidentialEnabled, Is.False);
    }

    [Test]
    public void FromDisabledZones_Multiple_DisablesMultiple()
    {
        var vm = ToolbarViewModel.FromDisabledZones(
            new[] { ZoneType.Industrial, ZoneType.Commercial });
        Assert.That(vm.IndustrialEnabled, Is.False);
        Assert.That(vm.CommercialEnabled, Is.False);
        Assert.That(vm.ResidentialEnabled, Is.True);
    }

    [Test]
    public void FromDisabledZones_AllThree_DisablesAll()
    {
        var vm = ToolbarViewModel.FromDisabledZones(
            new[] { ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial });
        Assert.That(vm.ResidentialEnabled, Is.False);
        Assert.That(vm.CommercialEnabled, Is.False);
        Assert.That(vm.IndustrialEnabled, Is.False);
    }

    // ---- Button style strings ----

    [Test]
    public void ResidentialButtonStyle_Normal_WhenEnabled()
    {
        var vm = new ToolbarViewModel { ResidentialEnabled = true };
        Assert.That(vm.ResidentialButtonStyle, Is.EqualTo("normal"));
    }

    [Test]
    public void ResidentialButtonStyle_Disabled_WhenDisabled()
    {
        var vm = new ToolbarViewModel { ResidentialEnabled = false };
        Assert.That(vm.ResidentialButtonStyle, Is.EqualTo("disabled"));
    }

    [Test]
    public void CommercialButtonStyle_Normal_WhenEnabled()
    {
        var vm = new ToolbarViewModel { CommercialEnabled = true };
        Assert.That(vm.CommercialButtonStyle, Is.EqualTo("normal"));
    }

    [Test]
    public void CommercialButtonStyle_Disabled_WhenDisabled()
    {
        var vm = new ToolbarViewModel { CommercialEnabled = false };
        Assert.That(vm.CommercialButtonStyle, Is.EqualTo("disabled"));
    }

    [Test]
    public void IndustrialButtonStyle_Normal_WhenEnabled()
    {
        var vm = new ToolbarViewModel { IndustrialEnabled = true };
        Assert.That(vm.IndustrialButtonStyle, Is.EqualTo("normal"));
    }

    [Test]
    public void IndustrialButtonStyle_Disabled_WhenDisabled()
    {
        var vm = new ToolbarViewModel { IndustrialEnabled = false };
        Assert.That(vm.IndustrialButtonStyle, Is.EqualTo("disabled"));
    }

    // ---- Default constructor ----

    [Test]
    public void DefaultConstructor_AllEnabled()
    {
        var vm = new ToolbarViewModel();
        Assert.That(vm.ResidentialEnabled, Is.True);
        Assert.That(vm.CommercialEnabled, Is.True);
        Assert.That(vm.IndustrialEnabled, Is.True);
    }
}
