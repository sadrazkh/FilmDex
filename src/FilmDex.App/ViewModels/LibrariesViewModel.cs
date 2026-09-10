using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilmDex.App.Services;
using FilmDex.Core;
using Microsoft.Win32;

namespace FilmDex.App.ViewModels;

/// <summary>The Libraries screen: add a folder, scan it once, scan it again later.</summary>
public sealed partial class LibrariesViewModel : ObservableObject
{
    private readonly ArchiveSession _session;
    private readonly Func<Task> _afterChange;
    private CancellationTokenSource? _scanCancellation;

    public LibrariesViewModel(ArchiveSession session, Func<Task> afterChange)
    {
        _session = session;
        _afterChange = afterChange;
    }

    public ObservableCollection<LibraryRow> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string ScanStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScanDetail { get; set; } = string.Empty;

    public bool IsEmpty => Items.Count == 0;

    public async Task RefreshAsync()
    {
        var libraries = await _session.GetLibrariesAsync();

        Items.Clear();
        foreach (var library in libraries) Items.Add(LibraryRow.From(library));

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "پوشه‌ای که فیلم‌ها در آن است را انتخاب کن",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;

        await AddFoldersAsync(dialog.FolderNames);
    }

    /// <summary>Also the drag-and-drop path: folders dropped on the window land here.</summary>
    public async Task AddFoldersAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!Directory.Exists(path)) continue;

            var label = new DirectoryInfo(path).Name;
            var library = await _session.AddLibraryAsync(string.IsNullOrWhiteSpace(label) ? path : label, path);

            await RefreshAsync();

            var row = Items.FirstOrDefault(item => item.Id == library.Id);
            if (row is not null) await ScanAsync(row);
        }
    }

    [RelayCommand]
    private async Task ScanAsync(LibraryRow? row)
    {
        if (row is null || IsScanning) return;

        if (!Directory.Exists(row.RootPath))
        {
            ScanStatus = $"«{row.RootPath}» الان در دسترس نیست — چیزی تغییر نکرد.";
            ScanDetail = "درایو را وصل کن و دوباره اسکن بزن. نام‌های قبلی سر جایشان هستند.";
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = $"در حال خواندن «{row.Label}» …";
        ScanDetail = string.Empty;

        var progress = new Progress<ScanProgress>(update =>
        {
            ScanStatus = $"{update.FilesSeen:N0} فایل دیده شد، {update.EntriesAccepted:N0} نام نگه داشته شد";
            ScanDetail = update.CurrentDirectory;
        });

        try
        {
            var result = await _session.ScanAsync(row.Id, row.RootPath, progress, _scanCancellation.Token);

            ScanStatus = $"«{row.Label}»: {result.Entries.Count:N0} نام ذخیره شد.";
            ScanDetail = result.SkippedDirectories > 0
                ? $"{result.FilesSeen:N0} فایل بررسی شد — {result.SkippedDirectories:N0} پوشه قابل خواندن نبود و رد شد."
                : $"{result.FilesSeen:N0} فایل بررسی شد.";

            await RefreshAsync();
            await _afterChange();
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "اسکن لغو شد.";
            ScanDetail = "نام‌های قبلی این کتابخانه دست‌نخورده ماندند.";
        }
        catch (Exception exception)
        {
            ScanStatus = "اسکن ناتمام ماند.";
            ScanDetail = exception.Message;
        }
        finally
        {
            IsScanning = false;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelScan() => _scanCancellation?.Cancel();

    [RelayCommand]
    private async Task RemoveAsync(LibraryRow? row)
    {
        if (row is null || IsScanning) return;

        await _session.RemoveLibraryAsync(row.Id);
        await RefreshAsync();
        await _afterChange();

        ScanStatus = $"«{row.Label}» حذف شد.";
        ScanDetail = "فقط از حافظه‌ی برنامه — روی دیسک دست نخورد.";
    }

    [RelayCommand]
    private async Task ReparseAsync()
    {
        if (IsScanning) return;

        var count = await _session.ReparseAllAsync();
        await _afterChange();

        ScanStatus = $"{count:N0} نام دوباره تحلیل شد.";
        ScanDetail = "بدون خواندن دیسک — از روی همان نام‌های ذخیره‌شده.";
    }
}
