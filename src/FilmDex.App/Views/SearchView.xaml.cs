using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FilmDex.App.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();

        // Opening this screen means wanting to type: put the caret where the typing goes.
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
            });
    }
}
