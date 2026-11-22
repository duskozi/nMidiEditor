using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace nMIDI
{
    public partial class App : Application
    {
        private bool _showingError;

        private void OnStartup(object sender, StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            Model.SongFingering.Failed += ex => AppLog.Error("Fingering", ex);

            ThemeManager.Apply(AppSettings.Load().Theme);
            // "nMIDI song.mid" opens the file.
            var window = new MainWindow(e.Args.FirstOrDefault());
            window.Show();
        }

        /// <summary>
        /// An exception on the UI thread that nothing caught: log it, save the work to the autosave file, tell the
        /// user, and keep running (closing would lose more than the failed action did).
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            AppLog.Error("UI thread", e.Exception);
            if (e.Exception is OutOfMemoryException) return;   // let it end the process
            e.Handled = true;
            if (_showingError) return;   // an error while the message is up: logged, not shown again

            _showingError = true;
            try
            {
                bool saved = (MainWindow as MainWindow)?.EmergencyAutosave() == true;
                MessageBox.Show(MainWindow,
                    $"Something went wrong:\n{e.Exception.Message}\n\n" +
                    (saved ? "Your changes were saved to the autosave file. " : "") +
                    "If it happens again, save your work and restart nMIDI Editor.\n\n" +
                    $"Details are in {AppLog.FilePath}",
                    "nMIDI Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _showingError = false;
            }
        }

        /// <summary>An exception on another thread that nothing caught: the process ends, so log it and save what we can.</summary>
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex) AppLog.Error("Background thread (fatal)", ex);
            try
            {
                Dispatcher.Invoke(() => (MainWindow as MainWindow)?.EmergencyAutosave(), DispatcherPriority.Send, default, TimeSpan.FromSeconds(2));
            }
            catch (Exception saveError) when (saveError is not OutOfMemoryException)
            {
                AppLog.Error("Emergency autosave", saveError);
            }
        }

        /// <summary>A failed background task nobody waited for: log it, so the failure is not silent.</summary>
        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLog.Error("Background task", e.Exception);
            e.SetObserved();
        }
    }
}
