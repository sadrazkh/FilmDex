using System.IO;
using System.Windows;
using FilmDex.App.ViewModels;

namespace FilmDex.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var hasFolders = e.Data.GetDataPresent(DataFormats.FileDrop);

        e.Effects = hasFolders ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;

        var folders = dropped.Where(Directory.Exists).ToArray();
        if (folders.Length == 0) return;

        viewModel.Section = 0;
        await viewModel.Libraries.AddFoldersAsync(folders);
    }
}
