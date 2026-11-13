using System.Linq;
using System.Windows;

namespace nMIDI
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            ThemeManager.Apply(AppSettings.Load().Theme);
            // "nMIDI song.mid" opens the file.
            var window = new MainWindow(e.Args.FirstOrDefault());
            window.Show();
        }
    }
}
