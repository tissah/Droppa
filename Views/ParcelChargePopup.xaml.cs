using CommunityToolkit.Maui.Views;
using Droppa.ViewModels;

namespace Droppa.Views;

public partial class ParcelChargePopup : Popup
{
    public ParcelChargePopup(ParcelChargeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        vm.RequestClose += async _ => await CloseAsync();
    }
}
