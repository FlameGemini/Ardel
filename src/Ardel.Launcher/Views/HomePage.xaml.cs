using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class HomePage : Page
{
    public LaunchViewModel ViewModel { get; }

    public HomePage()
    {
        StartupClock.Mark("HomePage ctor begin");
        ViewModel = App.Services.GetRequiredService<LaunchViewModel>();
        StartupClock.Mark("LaunchViewModel resolved");
        InitializeComponent();
        StartupClock.Mark("HomePage InitializeComponent done");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        DispatcherQueue.GetForCurrentThread().TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                StartupClock.Mark("HomePage Initialize (local versions)");
                ViewModel.InitializeCommand.Execute(null);
                StartupClock.Mark("HomePage Initialize done");
                StartupClock.Flush();
            });
    }
}
