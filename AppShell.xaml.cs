using Droppa.Views;

namespace Droppa;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Detail routes navigated to from the Home page.
        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("send", typeof(SendParcelPage));
        Routing.RegisterRoute("receive", typeof(ReceiveParcelPage));

        // Customer live tracking.
        Routing.RegisterRoute("track", typeof(TrackDeliveryPage));

        // Driver job preview (map + accept/deny) and the active-delivery screen.
        Routing.RegisterRoute("driverJobPreview", typeof(DriverJobPreviewPage));
        Routing.RegisterRoute("driverDelivery", typeof(DriverDeliveryPage));
    }
}
