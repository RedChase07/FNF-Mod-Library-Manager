using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FnFModLibrary_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception error)
        {
            ShowStartupError(error);
            throw;
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
        catch (Exception error)
        {
            ShowStartupError(error);
            Exit();
        }
    }

    private static void ShowStartupError(Exception error)
    {
        var reportPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FNF Mod Library WinUI startup error.txt");
        var reportStatus = $"Details were saved to:\n{reportPath}";
        try
        {
            File.WriteAllText(reportPath, error.ToString());
        }
        catch (Exception writeError) when (
            writeError is IOException or UnauthorizedAccessException)
        {
            reportStatus = $"The error report could not be saved: {writeError.Message}";
        }
        MessageBoxW(
            IntPtr.Zero,
            $"The app could not start. {reportStatus}\n\n{error.Message}",
            "FNF Mod Library startup error",
            0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
