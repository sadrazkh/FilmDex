using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilmDex.App.Services;
using FilmDex.Core;

namespace FilmDex.App.ViewModels;

/// <summary>
/// The Search screen. Every keystroke restarts a 250 ms timer and cancels the search before it, so
/// typing never queues up work it no longer needs.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int DebounceMilliseconds = 250;
    private const int MaxResults = 200;

    private readonly ArchiveSession _session;
    private readonly Dictionary<long, string> _libraryRoots = [];
    private CancellationTokenSource? _pending;

    public SearchViewModel(ArchiveSession session) => _session = session;

    public ObservableCollection<SearchResultRow> Results { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int MinPercent { get; set; } = 60;

    [ObservableProperty]
    public partial string ParsedSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    public bool HasResults => Results.Count > 0;

    /// <summary>Called after a scan, so paths and counts stay right.</summary>
    public async Task RefreshLibrariesAsync()
    {
        _libraryRoots.Clear();
        foreach (var library in await _session.GetLibrariesAsync()) _libraryRoots[library.Id] = library.RootPath;

        if (!string.IsNullOrWhiteSpace(Query)) await SearchAsync(debounce: false);
        else ShowIdleStatus();
    }

    partial void OnQueryChanged(string value) => _ = SearchAsync(debounce: true);

    partial void OnMinPercentChanged(int value) => _ = SearchAsync(debounce: true);

    private async Task SearchAsync(bool debounce)
    {
        _pending?.Cancel();
        _pending?.Dispose();

        var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        var token = cancellation.Token;

        try
        {
            if (debounce) await Task.Delay(DebounceMilliseconds, token);

            var query = Query;

            if (string.IsNullOrWhiteSpace(query))
            {
                Results.Clear();
                OnPropertyChanged(nameof(HasResults));
                ParsedSummary = string.Empty;
                ShowIdleStatus();
                return;
            }

            IsSearching = true;

            var index = _session.Index;
            var threshold = MinPercent;
            var hits = await Task.Run(() => index.Search(query, threshold, MaxResults, token), token);

            token.ThrowIfCancellationRequested();

            Results.Clear();
            foreach (var hit in hits)
            {
                _libraryRoots.TryGetValue(hit.Entry.LibraryId, out var root);
                Results.Add(SearchResultRow.From(hit, root));
            }

            OnPropertyChanged(nameof(HasResults));

            var parsed = NameNormalizer.Parse(query);
            ParsedSummary = parsed.Year is { } year
                ? $"دنبال «{parsed.CleanTitle}» سال {year} می‌گردم"
                : $"دنبال «{parsed.CleanTitle}» می‌گردم";

            Status = hits.Count switch
            {
                0 => $"چیزی شبیه این پیدا نشد — بین {index.Count:N0} نام. احتمالاً تکراری نیست.",
                _ => $"{hits.Count:N0} نتیجه از {index.Count:N0} نام" +
                     (hits.Count == MaxResults ? " (فقط ۲۰۰ تای اول)" : string.Empty)
            };
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke owns the screen now.
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_pending, cancellation)) IsSearching = false;
        }
    }

    private void ShowIdleStatus()
    {
        var count = _session.Index.Count;

        Status = count == 0
            ? "هنوز چیزی در حافظه نیست — از بخش کتابخانه‌ها یک پوشه اضافه کن."
            : $"{count:N0} نام در حافظه است. اسم فیلم را بزن.";
    }

    [RelayCommand]
    private static void CopyPath(SearchResultRow? row)
    {
        if (row is null) return;

        try
        {
            Clipboard.SetText(row.FullPath);
        }
        catch (Exception)
        {
            // The clipboard belongs to whatever window has it open; losing that race is not an error.
        }
    }

    [RelayCommand]
    private void OpenFolder(SearchResultRow? row)
    {
        if (row is null) return;

        try
        {
            if (File.Exists(row.FullPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.FullPath}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(row.FullPath))
            {
                Process.Start(new ProcessStartInfo(row.FullPath) { UseShellExecute = true });
            }
            else
            {
                Status = "این مسیر الان در دسترس نیست — درایوش وصل نیست.";
            }
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }
}
