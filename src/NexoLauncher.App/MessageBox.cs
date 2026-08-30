using System.Windows;

namespace NexoLauncher.App;

internal static class MessageBox
{
    public static MessageBoxResult Show(string messageBoxText)
        => Show(messageBoxText, "NEXO Client", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption)
        => Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        => Show(messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        => Show(ResolveOwner(), messageBoxText, caption, button, icon, MessageBoxResult.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText)
        => Show(owner, messageBoxText, "NEXO Client", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
        => Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button)
        => Show(owner, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        => Show(owner, messageBoxText, caption, button, icon, MessageBoxResult.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        if (owner is null)
            return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);

        if (button == MessageBoxButton.YesNo)
        {
            var accepted = NexoDialog.Confirm(owner, caption, messageBoxText, "SÍ", "NO");
            return accepted ? MessageBoxResult.Yes : MessageBoxResult.No;
        }

        if (button == MessageBoxButton.OKCancel)
        {
            var accepted = NexoDialog.Confirm(owner, caption, messageBoxText, "ACEPTAR", "CANCELAR");
            return accepted ? MessageBoxResult.OK : MessageBoxResult.Cancel;
        }

        if (button == MessageBoxButton.YesNoCancel)
            return System.Windows.MessageBox.Show(owner, messageBoxText, caption, button, icon, defaultResult);

        switch (icon)
        {
            case MessageBoxImage.Error:
                NexoDialog.Error(owner, caption, messageBoxText);
                break;
            case MessageBoxImage.Warning:
                NexoDialog.Warning(owner, caption, messageBoxText);
                break;
            default:
                NexoDialog.Info(owner, caption, messageBoxText);
                break;
        }
        return MessageBoxResult.OK;
    }

    private static Window? ResolveOwner()
        => System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
           ?? System.Windows.Application.Current?.MainWindow;
}
