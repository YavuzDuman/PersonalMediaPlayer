using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core.Library;
using PersonalMediaPlayer.Core.Models;
using Windows.Storage;

namespace PersonalMediaPlayer.App.ViewModels;

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IMediaLibrary _library;
    private readonly List<MediaItem> _allItems = [];
    private readonly Dictionary<string, int> _duplicateGroup = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressFolderLoad;
    private bool _duplicateCountKnown;
    private int _duplicateFileCount;
    private string? _duplicateCoverPath;
    private bool _duplicateCoverIsVideo;

    public LibraryViewModel(IMediaLibrary library)
    {
        _library = library;
        Folders.Add(new LibraryFolder { Name = LibraryFolder.All });
        MediaFavorites.Changed += async (_, _) =>
        {
            if (SelectedFolder?.IsFavorites == true)
            {
                await LoadItemsAsync();
            }

            await LoadFoldersAsync();
        };
    }

    public ObservableCollection<MediaItem> Items { get; } = [];

    public ObservableCollection<LibraryFolder> Folders { get; } = [];

    [ObservableProperty]
    private LibraryFolder? selectedFolder;

    [ObservableProperty]
    private MediaItem? detailsItem;

    [ObservableProperty]
    private string detailsExtra = string.Empty;

    public string DetailsImported => DetailsItem is null
        ? string.Empty
        : $"Added {DetailsItem.ImportedAt.ToLocalTime():g}";

    public string DetailsFolder
    {
        get
        {
            if (DetailsItem is null)
            {
                return string.Empty;
            }

            var albums = string.IsNullOrEmpty(DetailsItem.FolderName)
                ? "All media only"
                : $"All media, {DetailsItem.FolderName}";
            return SelectionCount > 1
                ? $"Last selected item is in {albums}."
                : $"In {albums}.";
        }
    }

    public string SelectionLabel => SelectionCount <= 1 ? string.Empty : $"{SelectionCount} selected";

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasStatus;

    [ObservableProperty]
    private bool hasSelection;

    [ObservableProperty]
    private int selectionCount;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private int typeFilterIndex;

    [ObservableProperty]
    private int sortIndex;

    public IReadOnlyList<string> TypeFilters { get; } = ["All types", "Images", "Videos"];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "Newest first",
        "Oldest first",
        "Name A–Z",
        "Name Z–A",
        "Largest",
        "Smallest",
        "Type"
    ];

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool showingDuplicates;

    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; } = [];

    public string CurrentFolderName => SelectedFolder is null || SelectedFolder.IsAll || SelectedFolder.IsUnfiled || SelectedFolder.IsSmart || SelectedFolder.IsFavorites || SelectedFolder.IsDuplicates
        ? string.Empty
        : SelectedFolder.Name;

    public bool LeavesCurrentFolder => IsOrdinaryFolder(CurrentFolderName);

    public string EmptyHint => SelectedFolder?.IsDuplicates == true
        ? "No files share the same content."
        : "Import or drop files here.";

    public bool MoveClearsOtherFolders(IReadOnlyList<MediaItem> selected)
        => !LeavesCurrentFolder && selected.Any(HasOrdinaryAlbum);

    public bool CanManageSelection => HasSelection && !IsBusy;

    public async Task LoadAsync()
    {
        await LoadFoldersAsync();
        await LoadItemsAsync();
    }

    public async Task LoadItemsAsync()
    {
        if (SelectedFolder?.IsDuplicates == true)
        {
            await LoadDuplicatesAsync();
            return;
        }

        var folder = SelectedFolder switch
        {
            null or { IsAll: true } => null,
            { IsUnfiled: true } => LibraryFolder.Unfiled,
            { IsFavorites: true } => null,
            _ => SelectedFolder.Name
        };
        var media = await _library.GetItemsAsync(folder);
        if (SelectedFolder?.IsFavorites == true)
        {
            media = media.Where(item => MediaFavorites.Contains(item.FilePath)).ToArray();
        }
        _allItems.Clear();
        _allItems.AddRange(media);
        ApplyFilter();
        _ = IndexScreenshotTextAsync(_allItems.ToArray());
    }

    private async Task LoadDuplicatesAsync()
    {
        IsBusy = true;
        try
        {
            var media = await _library.GetItemsAsync(null);
            var groups = await Task.Run(() => FindDuplicateGroups(media));
            _allItems.Clear();
            _duplicateGroup.Clear();
            var index = 0;
            foreach (var group in groups)
            {
                foreach (var item in group)
                {
                    _allItems.Add(item);
                    _duplicateGroup[item.FilePath] = index;
                }

                index++;
            }

            var cover = _allItems.FirstOrDefault();
            _duplicateFileCount = _allItems.Count;
            _duplicateCountKnown = true;
            _duplicateCoverPath = cover?.FilePath;
            _duplicateCoverIsVideo = cover?.IsVideo == true;
            ApplyFilter();
            RememberDuplicateFolder();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static List<List<MediaItem>> FindDuplicateGroups(IReadOnlyList<MediaItem> media)
    {
        var result = new List<List<MediaItem>>();
        foreach (var sized in media.GroupBy(item => item.FileSizeBytes).Where(group => group.Count() > 1))
        {
            var hashed = new Dictionary<string, List<MediaItem>>(StringComparer.Ordinal);
            foreach (var item in sized)
            {
                if (!File.Exists(item.FilePath))
                {
                    continue;
                }

                string hash;
                try
                {
                    hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.FilePath)));
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (!hashed.TryGetValue(hash, out var list))
                {
                    list = [];
                    hashed[hash] = list;
                }

                list.Add(item);
            }

            foreach (var list in hashed.Values.Where(list => list.Count > 1))
            {
                list.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase));
                result.Add(list);
            }
        }

        result.Sort((left, right) => right.Count.CompareTo(left.Count));
        return result;
    }

    private void RememberDuplicateFolder()
    {
        var index = -1;
        for (var i = 0; i < Folders.Count; i++)
        {
            if (Folders[i].IsDuplicates)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        var updated = new LibraryFolder
        {
            Name = LibraryFolder.Duplicates,
            FileCount = _duplicateFileCount,
            CoverPath = _duplicateCoverPath,
            CoverIsVideo = _duplicateCoverIsVideo
        };
        _suppressFolderLoad = true;
        Folders[index] = updated;
        if (SelectedFolder?.IsDuplicates == true)
        {
            SelectedFolder = updated;
        }

        _suppressFolderLoad = false;
    }

    private async Task IndexScreenshotTextAsync(IReadOnlyList<MediaItem> items)
    {
        var pending = items.Where(IsScreenshot).Where(item => !ScreenshotTextIndex.IsCurrent(item.FilePath)).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        foreach (var item in pending)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(item.FilePath);
                await ScreenshotTextIndex.StoreAsync(item.FilePath, bytes);
            }
            catch
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            ApplyFilter();
        }
    }

    private static bool IsScreenshot(MediaItem item)
        => item.FolderName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(name => name.Equals(LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase));

    public async Task LoadFoldersAsync()
    {
        var selectedName = SelectedFolder?.Name ?? string.Empty;
        var folders = _library.GetFolders();
        var all = _library.GetItems(null);
        var unfiled = _library.GetItems(LibraryFolder.Unfiled);
        Folders.Clear();
        Folders.Add(Describe(LibraryFolder.All, all));
        Folders.Add(Describe(LibraryFolder.Photos, all.Where(item => !item.IsVideo).ToArray()));
        Folders.Add(Describe(LibraryFolder.Videos, all.Where(item => item.IsVideo).ToArray()));
        Folders.Add(Describe(LibraryFolder.ThisMonth, all.Where(item => LibraryFolder.IsInThisMonth(item.ImportedAt)).ToArray()));
        Folders.Add(Describe(LibraryFolder.Favorites, all.Where(item => MediaFavorites.Contains(item.FilePath)).ToArray()));
        Folders.Add(new LibraryFolder
        {
            Name = LibraryFolder.Duplicates,
            FileCount = _duplicateFileCount,
            CountPending = !_duplicateCountKnown,
            CoverPath = _duplicateCoverPath,
            CoverIsVideo = _duplicateCoverIsVideo
        });
        Folders.Add(Describe(LibraryFolder.Unfiled, unfiled));
        foreach (var folder in folders)
        {
            Folders.Add(folder);
        }

        var deleted = _library.GetDeletedItems();
        Folders.Add(Describe(LibraryFolder.RecentlyDeleted, deleted));

        _suppressFolderLoad = true;
        SelectedFolder = Folders.FirstOrDefault(folder => folder.Name == selectedName) ?? Folders[0];
        _suppressFolderLoad = false;
    }

    public void SetSelection(IReadOnlyList<MediaItem> selected)
    {
        SelectionCount = selected.Count;
        HasSelection = selected.Count > 0;
        DetailsItem = selected.Count == 0 ? null : selected[^1];
        DetailsExtra = string.Empty;
        OnPropertyChanged(nameof(DetailsImported));
        OnPropertyChanged(nameof(DetailsFolder));
        OnPropertyChanged(nameof(SelectionLabel));
        _ = LoadDetailsExtraAsync(DetailsItem);
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var files = await FilePickerHelper.PickMediaAsync(App.MainAppWindow);
        if (files.Count == 0)
        {
            return;
        }

        await ImportStorageFilesAsync(files);
    }

    public async Task ImportStorageFilesAsync(IEnumerable<StorageFile> files)
    {
        IsBusy = true;
        var imported = 0;
        var failed = 0;
        var folder = CurrentFolderName is "" or LibraryFolder.Screenshots or LibraryFolder.Recordings ? null : CurrentFolderName;

        try
        {
            foreach (var file in files)
            {
                try
                {
                    await ImportFileAsync(file, folder);
                    imported++;
                }
                catch
                {
                    failed++;
                }
            }

            await LoadAsync();
            ShowImportStatus(imported, failed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        IsBusy = true;
        var imported = 0;
        var failed = 0;
        var folder = CurrentFolderName is "" or LibraryFolder.Screenshots or LibraryFolder.Recordings ? null : CurrentFolderName;

        try
        {
            foreach (var path in paths)
            {
                try
                {
                    await Task.Run(() => _library.ImportMedia(path, folder));
                    imported++;
                }
                catch
                {
                    failed++;
                }
            }

            await LoadAsync();
            ShowImportStatus(imported, failed);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddSelectionToNewFolderAsync(string name, IReadOnlyList<MediaItem> selected)
    {
        if (selected.Count == 0)
        {
            return;
        }

        try
        {
            var created = _library.CreateFolder(name);
            _library.MoveItems(selected.Select(item => item.FilePath), created);
            await LoadFoldersAsync();
            await LoadItemsAsync();
            ShowStatus($"Added {selected.Count} item{(selected.Count == 1 ? "" : "s")} to '{created}'.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task MoveSelectionToNewFolderAsync(string name, IReadOnlyList<MediaItem> selected)
    {
        if (selected.Count == 0)
        {
            return;
        }

        var source = LeavesCurrentFolder ? CurrentFolderName : null;
        var clearedOthers = MoveClearsOtherFolders(selected);
        try
        {
            var created = _library.CreateFolder(name);
            _library.MoveItemsTo(selected.Select(item => item.FilePath), source, created);
            await LoadFoldersAsync();
            await LoadItemsAsync();
            ShowStatus(MoveStatus(selected.Count, source, created, clearedOthers), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task CreateFolderAsync(string name)
    {
        try
        {
            var created = _library.CreateFolder(name);
            await LoadFoldersAsync();
            SelectedFolder = Folders.FirstOrDefault(folder => folder.Name == created);
            await LoadItemsAsync();
            ShowStatus($"Created folder '{created}'.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task RenameItemAsync(string filePath, string newName)
    {
        try
        {
            var renamed = _library.RenameItem(filePath, newName);
            PlaybackBookmarks.Move(filePath, renamed.FilePath);
            MediaFavorites.Move(filePath, renamed.FilePath);
            ScreenshotTextIndex.Move(filePath, renamed.FilePath);
            await LoadAsync();
            ShowStatus($"Renamed to '{renamed.DisplayName}'.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task RenameFolderAsync(string currentName, string newName)
    {
        try
        {
            var renamed = _library.RenameFolder(currentName, newName);
            await LoadFoldersAsync();
            SelectedFolder = Folders.FirstOrDefault(folder => folder.Name == renamed);
            await LoadItemsAsync();
            ShowStatus($"Renamed folder to '{renamed}'.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task DeleteFolderAsync(string name)
    {
        try
        {
            var wasSelected = SelectedFolder?.Name == name;
            _library.DeleteFolder(name);
            await LoadFoldersAsync();
            if (wasSelected)
            {
                SelectedFolder = Folders[0];
            }

            await LoadItemsAsync();
            ShowStatus($"Deleted folder '{name}'. Files remain in All media.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task DeleteAsync(IReadOnlyList<MediaItem> selected)
    {
        if (selected.Count == 0)
        {
            return;
        }

        try
        {
            if (SelectedFolder?.IsFavorites == true)
            {
                foreach (var item in selected)
                {
                    MediaFavorites.Remove(item.FilePath);
                }

                await LoadAsync();
                ShowStatus($"Removed {selected.Count} item{(selected.Count == 1 ? "" : "s")} from Favorites.", InfoBarSeverity.Success);
                return;
            }

            var folder = CurrentFolderName;
            if (SelectedFolder?.IsRecentlyDeleted == true)
            {
                foreach (var item in selected)
                {
                    ScreenshotTextIndex.Remove(item.FilePath);
                }

                _library.PurgeDeleted(selected.Select(item => item.FilePath));
                await LoadAsync();
                ShowStatus($"Permanently deleted {selected.Count} item{(selected.Count == 1 ? "" : "s")}.", InfoBarSeverity.Success);
                return;
            }

            if (string.IsNullOrEmpty(folder)
                || string.Equals(folder, LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase)
                || string.Equals(folder, LibraryFolder.Recordings, StringComparison.OrdinalIgnoreCase))
            {
                _library.DeleteItems(selected.Select(item => item.FilePath));
                await LoadAsync();
                ShowStatus($"Deleted {selected.Count} item{(selected.Count == 1 ? "" : "s")}. You can restore them from Recently deleted for 7 days.", InfoBarSeverity.Success);
                return;
            }

            _library.RemoveItemsFromFolder(selected.Select(item => item.FilePath), folder);
            await LoadAsync();
            ShowStatus($"Removed {selected.Count} item{(selected.Count == 1 ? "" : "s")} from '{folder}'. Files remain in All media.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task MoveAsync(IReadOnlyList<MediaItem> selected, string? folderName)
    {
        if (selected.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(folderName) && IsBlockedDestination(folderName))
        {
            return;
        }

        try
        {
            var clearing = string.IsNullOrWhiteSpace(folderName);
            _library.MoveItems(selected.Select(item => item.FilePath), clearing ? null : folderName);
            await LoadAsync();
            if (clearing)
            {
                var stays = selected.Count == 1 ? "It stays" : "They stay";
                var whose = selected.Count == 1 ? "its folders" : "their folders";
                ShowStatus($"Removed {selected.Count} item{(selected.Count == 1 ? "" : "s")} from {whose}. {stays} in All media. Recordings stays.", InfoBarSeverity.Success);
            }
            else
            {
                ShowStatus($"Added {selected.Count} item{(selected.Count == 1 ? "" : "s")} to '{folderName}'. Files stay in All media.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task RestoreDeletedAsync(IReadOnlyList<MediaItem> selected)
    {
        if (selected.Count == 0)
        {
            return;
        }

        try
        {
            _library.RestoreDeleted(selected.Select(item => item.FilePath));
            await LoadAsync();
            ShowStatus($"Restored {selected.Count} item{(selected.Count == 1 ? "" : "s")}.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    public async Task MoveToAsync(IReadOnlyList<MediaItem> selected, string folderName)
    {
        if (selected.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(folderName))
        {
            ShowStatus("Choose a folder.", InfoBarSeverity.Warning);
            return;
        }

        if (IsBlockedDestination(folderName))
        {
            return;
        }

        var source = LeavesCurrentFolder ? CurrentFolderName : null;
        if (source is not null && source.Equals(folderName, StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus($"Already in '{folderName}'.", InfoBarSeverity.Warning);
            return;
        }

        var clearedOthers = MoveClearsOtherFolders(selected);
        try
        {
            _library.MoveItemsTo(selected.Select(item => item.FilePath), source, folderName);
            await LoadAsync();
            ShowStatus(MoveStatus(selected.Count, source, folderName, clearedOthers), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private bool IsBlockedDestination(string folderName)
    {
        if (string.Equals(folderName, LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus("Items cannot be added to Screenshots. Use Capture.", InfoBarSeverity.Warning);
            return true;
        }

        if (string.Equals(folderName, LibraryFolder.Unfiled, StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus("Unfiled shows items that are not in a folder yet.", InfoBarSeverity.Warning);
            return true;
        }

        if (string.Equals(folderName, LibraryFolder.Recordings, StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus("New recordings are saved into Recordings. Add this item to another folder instead.", InfoBarSeverity.Warning);
            return true;
        }

        if (string.Equals(folderName, LibraryFolder.Favorites, StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus("Use the heart on a thumbnail to save a favorite.", InfoBarSeverity.Warning);
            return true;
        }

        if (string.Equals(folderName, LibraryFolder.Duplicates, StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus("Duplicates groups files that share the same content.", InfoBarSeverity.Warning);
            return true;
        }

        if (LibraryFolder.IsSmartName(folderName))
        {
            ShowStatus("Photos, Videos, and This month fill themselves. Add the item to one of your folders instead.", InfoBarSeverity.Warning);
            return true;
        }

        return false;
    }

    private static LibraryFolder Describe(string name, IReadOnlyList<MediaItem> itemsNewestFirst)
    {
        var cover = itemsNewestFirst.Count == 0 ? null : itemsNewestFirst[0];
        return new LibraryFolder
        {
            Name = name,
            FileCount = itemsNewestFirst.Count,
            CoverPath = cover?.FilePath,
            CoverIsVideo = cover?.IsVideo == true
        };
    }

    private static bool IsOrdinaryFolder(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && !name.Equals(LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase)
           && !name.Equals(LibraryFolder.Unfiled, StringComparison.OrdinalIgnoreCase)
           && !name.Equals(LibraryFolder.Recordings, StringComparison.OrdinalIgnoreCase)
           && !LibraryFolder.IsSmartName(name)
           && !name.Equals(LibraryFolder.RecentlyDeleted, StringComparison.OrdinalIgnoreCase)
           && !name.Equals(LibraryFolder.Favorites, StringComparison.OrdinalIgnoreCase)
           && !name.Equals(LibraryFolder.Duplicates, StringComparison.OrdinalIgnoreCase);

    private static bool HasOrdinaryAlbum(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.FolderName))
        {
            return false;
        }

        foreach (var part in item.FolderName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.Equals(LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase)
                && !part.Equals(LibraryFolder.Recordings, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string MoveStatus(int count, string? source, string destination, bool clearedOthers)
    {
        var noun = count == 1 ? "item" : "items";
        if (source is not null)
        {
            return $"Moved {count} {noun} from '{source}' to '{destination}'. Other folders stay. Files stay in All media.";
        }

        if (clearedOthers)
        {
            var left = count == 1 ? "It left its other folders." : "They left their other folders.";
            return $"Moved {count} {noun} to '{destination}'. {left} Recordings and Screenshots stay.";
        }

        return $"Moved {count} {noun} to '{destination}'. Files stay in All media.";
    }

    partial void OnSelectedFolderChanged(LibraryFolder? value)
    {
        OnPropertyChanged(nameof(EmptyHint));
        if (!_suppressFolderLoad)
        {
            _ = LoadItemsAsync();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        ImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnTypeFilterIndexChanged(int value) => ApplyFilter();

    partial void OnSortIndexChanged(int value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<MediaItem> matches = _allItems;

        matches = TypeFilterIndex switch
        {
            1 => matches.Where(item => !item.IsVideo),
            2 => matches.Where(item => item.IsVideo),
            _ => matches
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            matches = matches.Where(item =>
                item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.TypeLabel.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.FolderName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(item.FilePath).Contains(term, StringComparison.OrdinalIgnoreCase)
                || ScreenshotTextIndex.Matches(item.FilePath, term));
        }

        if (SelectedFolder?.IsDuplicates == true)
        {
            DuplicateGroups.Clear();
            var filtered = matches.ToList();
            foreach (var group in filtered.GroupBy(item => _duplicateGroup.GetValueOrDefault(item.FilePath)).OrderBy(group => group.Key))
            {
                var list = group.ToList();
                if (list.Count < 2)
                {
                    continue;
                }

                DuplicateGroups.Add(new DuplicateGroup(list));
            }

            Items.Clear();
            foreach (var group in DuplicateGroups)
            {
                foreach (var item in group)
                {
                    Items.Add(item);
                }
            }

            IsEmpty = DuplicateGroups.Count == 0;
            ShowingDuplicates = true;
            return;
        }

        matches = SortIndex switch
        {
            1 => matches.OrderBy(item => item.ImportedAt),
            2 => matches.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            3 => matches.OrderByDescending(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            4 => matches.OrderByDescending(item => item.FileSizeBytes),
            5 => matches.OrderBy(item => item.FileSizeBytes),
            6 => matches.OrderBy(item => item.TypeLabel).ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => matches.OrderByDescending(item => item.ImportedAt)
        };

        Items.Clear();
        foreach (var item in matches)
        {
            Items.Add(item);
        }

        IsEmpty = Items.Count == 0;
        ShowingDuplicates = false;
    }

    private bool CanImport => !IsBusy;

    private async Task ImportFileAsync(StorageFile file, string? folder)
    {
        if (!string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
        {
            await Task.Run(() => _library.ImportMedia(file.Path, folder));
            return;
        }

        await using var stream = await file.OpenStreamForReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.Position = 0;
        await Task.Run(() => _library.ImportMedia(buffer, file.Name, folder));
    }

    private async Task LoadDetailsExtraAsync(MediaItem? item)
    {
        if (item is null)
        {
            DetailsExtra = string.Empty;
            return;
        }

        try
        {
            DetailsExtra = await MediaDetails.DescribeAsync(item);
        }
        catch
        {
            DetailsExtra = string.Empty;
        }
    }

    private void ShowImportStatus(int imported, int failed)
    {
        if (failed == 0)
        {
            ShowStatus($"Imported {imported} file{(imported == 1 ? "" : "s")}.", InfoBarSeverity.Success);
        }
        else
        {
            ShowStatus($"Imported {imported}, skipped {failed}.", InfoBarSeverity.Warning);
        }
    }

    internal void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        HasStatus = true;
    }
}

public sealed class DuplicateGroup : List<MediaItem>
{
    public DuplicateGroup(IReadOnlyList<MediaItem> items)
    {
        AddRange(items);
        Label = items.Count == 1 ? "1 file" : $"{items.Count} identical files";
    }

    public string Label { get; }
}
