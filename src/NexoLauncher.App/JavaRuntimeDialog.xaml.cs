using System.IO;
using System.Windows;
using System.Windows.Controls;
using NexoLauncher.Java;
using NexoLauncher.Java.Compatibility;
using NexoLauncher.Java.Selection;

namespace NexoLauncher.App;

public partial class JavaRuntimeDialog : Window
{
    private readonly int? requiredMajor;

    public JavaRuntime? SelectedRuntime { get; private set; }
    public bool ManualBrowseRequested { get; private set; }

    public JavaRuntimeDialog(IReadOnlyList<JavaRuntime> runtimes, int? requiredMajor, JavaRuntime? selectedRuntime = null)
    {
        NativeWindowTheme.ApplyDarkTitleBar(this);
        InitializeComponent();
        Title = "Java · NEXA";
        Icon = System.Windows.Application.Current.TryFindResource("Nexo.BrandMark") as System.Windows.Media.ImageSource;
        this.requiredMajor = requiredMajor;

        RequirementText.Text = requiredMajor is > 0
            ? $"Minecraft requiere Java {requiredMajor}"
            : "NEXA muestra una sola opción recomendada por versión de Java";

        var recommendedRuntimes = JavaRuntimeSelector.BestPerMajor(runtimes);
        var choices = recommendedRuntimes.Select(CreateChoice).ToArray();
        RuntimeList.ItemsSource = choices;

        if (choices.Length == 0) return;

        var selected = selectedRuntime is null
            ? null
            : choices.FirstOrDefault(item => item.Runtime.MajorVersion == selectedRuntime.MajorVersion);

        RuntimeList.SelectedItem = selected ?? choices.FirstOrDefault(item => item.IsCompatible) ?? choices[0];
    }

    private RuntimeChoice CreateChoice(JavaRuntime runtime)
    {
        var compatible = requiredMajor is > 0
            ? JavaCompatibility.Evaluate(runtime, requiredMajor.Value).IsCompatible
            : File.Exists(runtime.JavawExecutable) && (!Environment.Is64BitOperatingSystem || runtime.Is64Bit);

        return new RuntimeChoice(
            runtime,
            $"Java {runtime.MajorVersion} · {runtime.FullVersion} · {runtime.Architecture}",
            $"{runtime.Vendor} · Recomendado por NEXA",
            runtime.JavaExecutable,
            compatible ? "COMPATIBLE" : "OTRA VERSIÓN",
            compatible);
    }

    private void RuntimeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UseButton.IsEnabled = RuntimeList.SelectedItem is RuntimeChoice choice && choice.IsCompatible;
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (RuntimeList.SelectedItem is not RuntimeChoice choice || !choice.IsCompatible) return;
        SelectedRuntime = choice.Runtime;
        DialogResult = true;
    }

    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        ManualBrowseRequested = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record RuntimeChoice(
        JavaRuntime Runtime,
        string Title,
        string VendorLine,
        string Path,
        string Status,
        bool IsCompatible);
}
