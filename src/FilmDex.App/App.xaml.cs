using System.IO;
using System.Windows;
using System.Windows.Threading;
using FilmDex.App.Services;
using FilmDex.App.ViewModels;
using FilmDex.Core;

namespace FilmDex.App;

public partial class App : Application
{
    private ArchiveSession? _session;

    /// <summary>
    /// The database FilmDex will use. `FILMDEX_DB` overrides it, which is how a second archive —
    /// or a throwaway one — is kept apart from the real one.
    /// </summary>
    public static string DatabasePath =>
        Environment.GetEnvironmentVariable("FILMDEX_DB") is { Length: > 0 } custom
            ? custom
            : LibraryStore.DefaultDatabasePath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        try
        {
            _session = new ArchiveSession(DatabasePath);
        }
        catch (Exception exception)
        {
            Log(exception);

            MessageBox.Show(
                $"پایگاه‌داده باز نشد:\n{DatabasePath}\n\n{exception.Message}",
                "FilmDex",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        var viewModel = new MainViewModel(_session);
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        // Folders passed on the command line — or dropped onto the .exe — are added and scanned.
        var folders = e.Args.Where(Directory.Exists).ToArray();

        _ = StartAsync(viewModel, folders);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _session?.Dispose();
        base.OnExit(e);
    }

    private static async Task StartAsync(MainViewModel viewModel, string[] folders)
    {
        await viewModel.InitialiseAsync();

        if (folders.Length == 0) return;

        viewModel.Section = 0;
        await viewModel.Libraries.AddFoldersAsync(folders);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);

        MessageBox.Show(e.Exception.Message, "FilmDex", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    /// <summary>Keeps the reason next to the database, because a message box that closed says nothing.</summary>
    private static void Log(Exception exception)
    {
        try
        {
            var folder = Path.GetDirectoryName(DatabasePath);
            if (string.IsNullOrEmpty(folder)) return;

            Directory.CreateDirectory(folder);

            File.AppendAllText(
                Path.Combine(folder, "last-error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never be the thing that takes the app down.
        }
    }
}
