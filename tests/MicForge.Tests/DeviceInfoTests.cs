using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Data;
using MicForge.Audio;

namespace MicForge.Tests;

public sealed class DeviceInfoTests
{
    [Fact]
    public void Equals_ComparesById()
    {
        var a = new DeviceInfo("id-1", "Mic");
        Assert.Equal(a, new DeviceInfo("id-1", "Mic (renamed)"));
        Assert.Equal(a.GetHashCode(), new DeviceInfo("id-1", "Mic").GetHashCode());
        Assert.NotEqual(a, new DeviceInfo("id-2", "Mic"));
        Assert.False(a.Equals(null));
    }

    // Regression for the follow-default restart failure: reloading the device lists made the
    // bound output ComboBox drop its selection and write null back to SelectedOutput, so the
    // restart threw "The selected audio device is no longer available".
    [Fact]
    public void BoundComboBox_KeepsSelection_WhenDeviceListIsReloaded()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new DeviceListModel { Outputs = Load() };
                vm.SelectedOutput = vm.Outputs[1];

                var combo = new ComboBox { DataContext = vm };
                combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(DeviceListModel.Outputs)));
                combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                    new Binding(nameof(DeviceListModel.SelectedOutput)));
                Assert.Same(vm.Outputs[1], combo.SelectedItem);

                vm.Outputs = Load();   // new instances, same devices

                Assert.NotNull(vm.SelectedOutput);
                Assert.Equal("cable-in", vm.SelectedOutput!.Id);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());

        static List<DeviceInfo> Load() =>
            [new DeviceInfo("speakers", "Speakers"), new DeviceInfo("cable-in", "CABLE Input")];
    }

    private sealed class DeviceListModel : INotifyPropertyChanged
    {
        private List<DeviceInfo> _outputs = [];
        private DeviceInfo? _selectedOutput;

        public List<DeviceInfo> Outputs { get => _outputs; set { _outputs = value; Changed(); } }
        public DeviceInfo? SelectedOutput { get => _selectedOutput; set { _selectedOutput = value; Changed(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
