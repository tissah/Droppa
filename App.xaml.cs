using Droppa.Services;

namespace Droppa;

public partial class App : Application
{
	// Held for the app's lifetime so it keeps listening for expired sessions.
	private readonly SessionGuard _sessionGuard;

	public App(SessionGuard sessionGuard)
	{
		InitializeComponent();
		_sessionGuard = sessionGuard;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}