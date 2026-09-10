using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilmDex.App.Services;

namespace FilmDex.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ArchiveSession _session;

    public MainViewModel(ArchiveSession session)
    {
        _session = session;
        Search = new SearchViewModel(session);
        Libraries = new LibrariesViewModel(session, OnArchiveChangedAsync);
    }

    public LibrariesViewModel Libraries { get; }

    public SearchViewModel Search { get; }

    public string DatabasePath => _session.DatabasePath;

    /// <summary>0 = libraries, 1 = search.</summary>
    [ObservableProperty]
    public partial int Section { get; set; }

    public async Task InitialiseAsync()
    {
        await _session.ReloadIndexAsync();
        await Libraries.RefreshAsync();
        await Search.RefreshLibrariesAsync();

        // Nothing remembered yet means there is nothing to search: start where the work is.
        Section = _session.Index.Count == 0 ? 0 : 1;
    }

    [RelayCommand]
    private void ShowLibraries() => Section = 0;

    [RelayCommand]
    private void ShowSearch() => Section = 1;

    private async Task OnArchiveChangedAsync() => await Search.RefreshLibrariesAsync();
}
