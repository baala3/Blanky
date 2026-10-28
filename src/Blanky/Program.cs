using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Blanky;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // One Blanky per data folder: a second launch hands its activation to the
        // first (which brings its window forward) and exits. Dev and test runs with
        // their own --data folder stay separate from the user's Blanky.
        int dataFlag = Array.IndexOf(args, "--data");
        string key = dataFlag >= 0 && dataFlag + 1 < args.Length
            ? "Blanky." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(args[dataFlag + 1]).ToUpperInvariant())))[..16]
            : "Blanky.Main";
        var instance = AppInstance.FindOrRegisterForKey(key);
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(args);
        });
        return 0;
    }
}
