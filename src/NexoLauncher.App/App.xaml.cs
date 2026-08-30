using System.Windows;
using NexoLauncher.App.UI;

namespace NexoLauncher.App;

public partial class App : System.Windows.Application
{
    public App()
    {
        NexoUiQualityModule.Initialize();
    }
}
