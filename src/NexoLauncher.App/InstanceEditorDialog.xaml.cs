using System.IO;
using System.Windows;
using Microsoft.Win32;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.App;

public partial class InstanceEditorDialog : Window
{
    public string UpdatedName { get; private set; }
    public InstanceSettings UpdatedSettings { get; private set; }

    public InstanceEditorDialog(GameInstance instance)
    {
        InitializeComponent();
        UpdatedName = instance.Name;
        UpdatedSettings = instance.Settings;
        IdentityText.Text = $"Minecraft {instance.MinecraftVersion} · {instance.Loader}" +
                            (string.IsNullOrWhiteSpace(instance.LoaderVersion) ? string.Empty : " " + instance.LoaderVersion);
        NameBox.Text = instance.Name;
        MemoryBox.Text = instance.Settings.MemoryMiB?.ToString() ?? string.Empty;
        JavaBox.Text = instance.Settings.JavaPath ?? string.Empty;
        WidthBox.Text = instance.Settings.WindowWidth?.ToString() ?? string.Empty;
        HeightBox.Text = instance.Settings.WindowHeight?.ToString() ?? string.Empty;
        FullscreenBox.IsChecked = instance.Settings.Fullscreen;
        JvmArgumentsBox.Text = string.Join(Environment.NewLine, instance.Settings.JvmArguments ?? []);
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona java.exe o javaw.exe",
            Filter = "Java para Windows|java.exe;javaw.exe|Ejecutables|*.exe"
        };
        if (dialog.ShowDialog(this) == true) JavaBox.Text = dialog.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length is < 1 or > 64) { Fail("El nombre debe tener entre 1 y 64 caracteres."); return; }
        if (!OptionalInt(MemoryBox.Text, 512, 65536, "RAM", out var memory)) return;
        if (!OptionalInt(WidthBox.Text, 320, 16384, "ancho", out var width)) return;
        if (!OptionalInt(HeightBox.Text, 240, 16384, "alto", out var height)) return;
        if ((width is null) != (height is null)) { Fail("Ancho y alto deben configurarse juntos."); return; }

        var javaPath = string.IsNullOrWhiteSpace(JavaBox.Text) ? null : Path.GetFullPath(JavaBox.Text.Trim());
        var arguments = JvmArgumentsBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        UpdatedName = name;
        UpdatedSettings = new InstanceSettings(memory, javaPath, arguments.Length == 0 ? null : arguments, width, height, FullscreenBox.IsChecked);
        DialogResult = true;
    }

    private bool OptionalInt(string text, int minimum, int maximum, string field, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!int.TryParse(text, out var parsed) || parsed < minimum || parsed > maximum)
        {
            Fail($"El campo {field} debe estar entre {minimum} y {maximum}.");
            return false;
        }
        value = parsed;
        return true;
    }

    private void Fail(string message) => ValidationText.Text = message;
}
