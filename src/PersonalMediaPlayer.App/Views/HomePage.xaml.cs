using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.App.Controls;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Storage;
using PersonalMediaPlayer.App.Subtitles;
using PersonalMediaPlayer.Core.Models;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class HomePage : Page
{
    private readonly List<DownloadQueueItem> _watchedDownloads = [];
    private readonly List<ContinueWatchCard> _continueCards = [];
    private CancellationTokenSource? _linkCheck;
    private int _linkGeneration;
    private int _previewTicket;
    private string? _previewPlaylistId;
    private Storyboard? _previewMotion;
    private bool _previewHiding;
    private bool _left;
    private bool _continueAll;
    private int _searchGeneration;

    public HomePage()
    {
        InitializeComponent();
        LibraryWatch.Changed += OnLibraryWatchChanged;
        Unloaded += (_, _) => LibraryWatch.Changed -= OnLibraryWatchChanged;
        PlaylistPreview.PointerEntered += (_, _) => _previewTicket++;
        PlaylistPreview.PointerExited += PlaylistPreview_Exited;
        ContinueSection.SizeChanged += (_, _) => LayoutContinueGrid();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _left = false;
        _continueAll = e.Parameter is HomeView.ContinueAll;
        var saved = default(SearchSession.Entry);
        if (!_continueAll)
        {
            saved = SearchSession.Recall(SearchSession.Home);
            if (!string.Equals(SearchBox.Text, saved.Text, StringComparison.Ordinal))
            {
                SearchBox.Text = saved.Text;
            }

            DownloadQueueHub.Items.CollectionChanged += Downloads_Changed;
            foreach (var item in DownloadQueueHub.Items)
            {
                WatchDownload(item);
            }
        }

        Show();
        if (!_continueAll && !string.IsNullOrWhiteSpace(saved.Text))
        {
            SearchScroll.Restore(PageScroll, saved.Offset);
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (!_continueAll)
        {
            SearchSession.Remember(
                SearchSession.Home,
                SearchBox.Text,
                PageScroll.VerticalOffset,
                PageScroll.ScrollableHeight > 0);
        }

        _left = true;
        _previewTicket++;
        _linkGeneration++;
        _linkCheck?.Cancel();
        ClosePreview();
        DownloadQueueHub.Items.CollectionChanged -= Downloads_Changed;
        foreach (var item in _watchedDownloads)
        {
            item.PropertyChanged -= Download_Changed;
        }

        _watchedDownloads.Clear();
    }

    private void OnLibraryWatchChanged(object? sender, EventArgs e)
    {
        if (_left)
        {
            return;
        }

        Show();
    }

    private void Show()
    {
        if (_left)
        {
            return;
        }

        if (_continueAll)
        {
            PageTitle.Text = "Continue watching";
            PageLead.Text = "Everything you left in the middle.";
            SearchBox.Visibility = Visibility.Collapsed;
            SearchSection.Visibility = Visibility.Collapsed;
            EmptyTitle.Text = "Nothing to continue.";
            EmptyLead.Text = "A video shows up here after you leave it in the middle.";
            EmptyActions.Visibility = Visibility.Collapsed;
            ShowContinue();
            PlaylistsSection.Visibility = Visibility.Collapsed;
            RecentSection.Visibility = Visibility.Collapsed;
            WordsSection.Visibility = Visibility.Collapsed;
            DownloadsSection.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = ContinueSection.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        PageTitle.Text = "Home";
        PageLead.Text = "Continue a video, open a playlist, or go back to something you just added.";
        SearchBox.Visibility = Visibility.Visible;
        EmptyTitle.Text = "Nothing here yet.";
        EmptyLead.Text = "Import a file or open a video link.";
        EmptyActions.Visibility = Visibility.Visible;
        if (HasSearch())
        {
            ClosePreview();
            ShowSearch();
            return;
        }

        SearchSection.Visibility = Visibility.Collapsed;
        ShowContinue();
        ShowPlaylists();
        ShowRecent();
        ShowWords();
        ShowDownloads();
        var any = ContinueSection.Visibility == Visibility.Visible
            || PlaylistsSection.Visibility == Visibility.Visible
            || RecentSection.Visibility == Visibility.Visible
            || WordsSection.Visibility == Visibility.Visible
            || DownloadsSection.Visibility == Visibility.Visible;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowContinue()
    {
        ContinueList.Children.Clear();
        _continueCards.Clear();
        ContinueGrid.Children.Clear();
        ContinueGrid.RowDefinitions.Clear();
        ContinueGrid.ColumnDefinitions.Clear();
        var unfinished = PlaybackProgress.Unfinished();
        var points = (_continueAll ? unfinished : unfinished.Take(16)).ToArray();
        ContinueSection.Visibility = points.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ContinueHeading.Visibility = _continueAll ? Visibility.Collapsed : Visibility.Visible;
        ContinueRow.Visibility = _continueAll ? Visibility.Collapsed : Visibility.Visible;
        ContinueGrid.Visibility = _continueAll ? Visibility.Visible : Visibility.Collapsed;
        var library = App.MediaLibrary.GetItems(null);
        foreach (var point in points)
        {
            var file = library.FirstOrDefault(item => item.IsVideo && string.Equals(item.FilePath, point.Key, StringComparison.OrdinalIgnoreCase));
            var title = file?.DisplayName ?? point.Title ?? ContinueName(point.Key);
            var image = file is null ? point.Thumbnail ?? StreamThumbnail.ForPage(point.Key) : null;
            var card = new ContinueWatchCard();
            card.Show(title, ContinuePlace(point.TimeMs, point.DurationMs), ContinueFraction(point.TimeMs, point.DurationMs), file?.FilePath, image);
            var key = point.Key;
            card.Chosen += (_, _) => OpenContinue(key, file);
            if (_continueAll)
            {
                _continueCards.Add(card);
            }
            else
            {
                ContinueList.Children.Add(card);
            }
        }

        if (_continueAll)
        {
            LayoutContinueGrid();
        }
    }

    private void LayoutContinueGrid()
    {
        if (!_continueAll || _continueCards.Count == 0)
        {
            return;
        }

        var width = ContinueSection.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        const double cardWidth = 220;
        const double gap = 12;
        var columns = Math.Max(1, (int)Math.Floor((width + gap) / (cardWidth + gap)));
        if (ContinueGrid.ColumnDefinitions.Count == columns && ContinueGrid.Children.Count == _continueCards.Count)
        {
            return;
        }

        ContinueGrid.Children.Clear();
        ContinueGrid.RowDefinitions.Clear();
        ContinueGrid.ColumnDefinitions.Clear();
        for (var column = 0; column < columns; column++)
        {
            ContinueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var i = 0; i < _continueCards.Count; i++)
        {
            var row = i / columns;
            while (ContinueGrid.RowDefinitions.Count <= row)
            {
                ContinueGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var card = _continueCards[i];
            Grid.SetRow(card, row);
            Grid.SetColumn(card, i % columns);
            ContinueGrid.Children.Add(card);
        }
    }

    private void OpenContinue(string key, MediaItem? file)
    {
        if (file is { IsMissing: true })
        {
            Status("This file is missing. Locate it in the library to keep saved words, bookmarks, and the playback position.", InfoBarSeverity.Warning);
            return;
        }

        if (file is not null && File.Exists(file.FilePath))
        {
            NavigationHelper.OpenPlayer(file);
            return;
        }

        if (StreamLink.TryNormalize(key, out var page) && App.MainAppWindow is MainWindow window)
        {
            window.OpenResolvedPage(page, PlaybackProgress.Load(key));
            return;
        }

        Status("That video is no longer on this PC.", InfoBarSeverity.Warning);
        Show();
    }

    private void ShowPlaylists()
    {
        ClosePreview();
        PlaylistList.Children.Clear();
        var lists = Playlists.All();
        PlaylistsSection.Visibility = lists.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var names = LibraryNames();
        foreach (var list in lists)
        {
            PlaylistList.Children.Add(PlaylistCard(list, names));
        }
    }

    private UIElement PlaylistCard(Playlist list, IReadOnlyDictionary<string, string> names)
    {
        var index = list.Videos.FindIndex(item => item.IsPlayable);
        var count = list.Videos.Count == 0
            ? "No videos"
            : index < 0
                ? "Not on this PC"
                : list.Videos.Count == 1 ? "1 video" : $"{list.Videos.Count} videos";
        var shots = new List<PlaylistCoverShot>();
        foreach (var entry in list.Videos)
        {
            if (CoverShot(entry) is not PlaylistCoverShot shot)
            {
                continue;
            }

            shots.Add(shot);
            if (shots.Count == 4)
            {
                break;
            }
        }
        var card = new PlaylistHomeCard();
        card.Show(list.Name, count, index >= 0, shots);
        var id = list.Id;
        card.PlayChosen += (_, _) =>
        {
            if (index >= 0)
            {
                NavigationHelper.OpenPlayer(new PlaylistOpenRequest(id, index));
            }
        };
        card.OpenChosen += (_, _) => OpenSection(typeof(PlaylistsPage), id);
        card.PointerEntered += (_, _) => RevealPlaylist(list, names, card);
        card.PointerExited += (_, args) =>
        {
            if (PointerLeft(card, args))
            {
                SchedulePreviewHide();
            }
        };
        return card;
    }

    private static PlaylistCoverShot? CoverShot(PlaylistEntry entry)
    {
        if (entry.Resolve)
        {
            var url = entry.Thumbnail ?? StreamThumbnail.ForPage(entry.Location);
            return url is null ? null : new PlaylistCoverShot(null, url);
        }

        return entry.IsPlayable ? new PlaylistCoverShot(entry.Location, null) : null;
    }

    private void RevealPlaylist(Playlist list, IReadOnlyDictionary<string, string> names, FrameworkElement card)
    {
        if (_left)
        {
            return;
        }

        _previewTicket++;
        var same = string.Equals(_previewPlaylistId, list.Id, StringComparison.Ordinal)
            && PlaylistPreview.Visibility == Visibility.Visible;
        HighlightPreviewCard(card);
        if (same)
        {
            if (_previewHiding)
            {
                _previewMotion?.Stop();
                _previewMotion = null;
                _previewHiding = false;
                PlaylistPreview.Opacity = 1;
            }

            return;
        }

        _previewHiding = false;
        _previewMotion?.Stop();
        _previewMotion = null;
        _previewPlaylistId = list.Id;
        FillPreview(list, names);
        PlayPreviewMotion();
        PlaylistPreview.Visibility = Visibility.Visible;
        PlaylistPreview.Opacity = 1;
        _previewMotion?.Begin();
    }

    private void FillPreview(Playlist list, IReadOnlyDictionary<string, string> names)
    {
        PlaylistPreviewList.Children.Clear();
        var total = list.Videos.Count;
        PlaylistPreviewTitle.Text = total == 0
            ? $"{list.Name} · No videos"
            : total == 1 ? $"{list.Name} · 1 video" : $"{list.Name} · {total} videos";
        if (total == 0)
        {
            PlaylistPreviewList.Children.Add(PreviewNote("No videos in this playlist."));
            return;
        }

        var shown = Math.Min(total, 12);
        for (var i = 0; i < shown; i++)
        {
            PlaylistPreviewList.Children.Add(PreviewVideo(list, i, names));
        }

        var extra = total - shown;
        if (extra > 0)
        {
            PlaylistPreviewList.Children.Add(PreviewNote(extra == 1 ? "1 more" : $"{extra} more", list.Id));
        }
    }

    private UIElement PreviewVideo(Playlist list, int index, IReadOnlyDictionary<string, string> names)
    {
        var entry = list.Videos[index];
        var title = entry.DisplayTitle(path => names.TryGetValue(path, out var known) ? known : null);
        var place = entry.Resolve ? "Online" : entry.IsPlayable ? "Video" : "Not on this PC";
        var path = !entry.Resolve && entry.IsPlayable ? entry.Location : null;
        var image = entry.Resolve ? entry.Thumbnail ?? StreamThumbnail.ForPage(entry.Location) : null;
        var card = new ContinueWatchCard();
        card.Show(title, place, 0, path, image, entry.IsPlayable);
        var playlistId = list.Id;
        var playable = entry.IsPlayable;
        card.Chosen += (_, _) =>
        {
            if (!playable)
            {
                Status("That video is no longer on this PC.", InfoBarSeverity.Warning);
                return;
            }

            NavigationHelper.OpenPlayer(new PlaylistOpenRequest(playlistId, index));
        };
        return card;
    }

    private UIElement PreviewNote(string text, string? playlistId = null)
    {
        var note = new Border
        {
            Width = 220,
            Height = 176,
            Style = (Style)Resources["HomeCard"],
            Child = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            }
        };
        if (playlistId is not null)
        {
            var id = playlistId;
            note.Tapped += (_, args) =>
            {
                args.Handled = true;
                OpenSection(typeof(PlaylistsPage), id);
            };
        }

        return note;
    }

    private void PlayPreviewMotion()
    {
        var board = new Storyboard();
        var index = 0;
        AddPreviewMotion(board, PlaylistPreviewTitle, index++, scale: false, opacity: 0.7);
        foreach (var child in PlaylistPreviewList.Children.OfType<UIElement>())
        {
            AddPreviewMotion(board, child, index++, scale: true);
        }

        _previewMotion = board;
    }

    private static void AddPreviewMotion(Storyboard board, UIElement element, int index, bool scale, double opacity = 1)
    {
        var transform = new CompositeTransform
        {
            TranslateY = 16,
            ScaleX = scale ? 0.94 : 1,
            ScaleY = scale ? 0.94 : 1
        };
        element.RenderTransform = transform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.Opacity = 0;
        var delay = TimeSpan.FromMilliseconds(40 * index);
        AddMotion(board, element, "Opacity", 0, opacity, delay);
        AddMotion(board, transform, "TranslateY", 16, 0, delay);
        if (!scale)
        {
            return;
        }

        AddMotion(board, transform, "ScaleX", 0.94, 1, delay);
        AddMotion(board, transform, "ScaleY", 0.94, 1, delay);
    }

    private static void AddMotion(Storyboard board, DependencyObject target, string property, double from, double to, TimeSpan delay)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = delay,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
    }

    private void HighlightPreviewCard(FrameworkElement active)
    {
        foreach (var child in PlaylistList.Children.OfType<FrameworkElement>())
        {
            child.Opacity = ReferenceEquals(child, active) ? 1 : 0.62;
        }
    }

    private void PlaylistPreview_Exited(object sender, PointerRoutedEventArgs args)
    {
        if (PointerLeft(PlaylistPreview, args))
        {
            SchedulePreviewHide();
        }
    }

    private async void SchedulePreviewHide()
    {
        var ticket = ++_previewTicket;
        await Task.Delay(200);
        if (ticket != _previewTicket || _left)
        {
            return;
        }

        HidePreview();
    }

    private void HidePreview()
    {
        _previewMotion?.Stop();
        _previewHiding = true;
        var fade = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };
        Storyboard.SetTarget(fade, PlaylistPreview);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var board = new Storyboard();
        board.Children.Add(fade);
        var ticket = _previewTicket;
        board.Completed += (_, _) =>
        {
            if (ticket != _previewTicket || _left)
            {
                return;
            }

            if (ReferenceEquals(_previewMotion, board))
            {
                _previewMotion = null;
            }

            CollapsePreview();
        };
        _previewMotion = board;
        board.Begin();
    }

    private void ClosePreview()
    {
        var motion = _previewMotion;
        _previewMotion = null;
        motion?.Stop();
        CollapsePreview();
    }

    private void CollapsePreview()
    {
        _previewHiding = false;
        _previewPlaylistId = null;
        PlaylistPreview.Visibility = Visibility.Collapsed;
        PlaylistPreview.Opacity = 0;
        PlaylistPreviewList.Children.Clear();
        foreach (var child in PlaylistList.Children.OfType<FrameworkElement>())
        {
            child.Opacity = 1;
        }
    }

    private static bool PointerLeft(FrameworkElement element, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(element).Position;
        return point.X < 0 || point.Y < 0 || point.X >= element.ActualWidth || point.Y >= element.ActualHeight;
    }

    private static Dictionary<string, string> LibraryNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in App.MediaLibrary.GetItems())
        {
            names.TryAdd(item.FilePath, item.DisplayName);
        }

        return names;
    }

    private void ShowRecent()
    {
        RecentList.Children.Clear();
        var recent = App.MediaLibrary.GetItems()
            .OrderByDescending(item => item.ImportedAt)
            .Take(12)
            .ToArray();
        RecentSection.Visibility = recent.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in recent)
        {
            var card = new ContinueWatchCard();
            card.Show(item.DisplayName, RecentLabel(item), 0, item.FilePath, showPlay: item.IsVideo);
            card.Chosen += (_, _) => OpenRecent(item);
            RecentList.Children.Add(card);
        }
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || _continueAll || _left)
        {
            return;
        }

        Show();
    }

    private bool HasSearch()
        => !_continueAll && !string.IsNullOrWhiteSpace(SearchBox.Text);

    private void ShowSearch()
    {
        ContinueSection.Visibility = Visibility.Collapsed;
        PlaylistsSection.Visibility = Visibility.Collapsed;
        RecentSection.Visibility = Visibility.Collapsed;
        WordsSection.Visibility = Visibility.Collapsed;
        DownloadsSection.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        SearchSection.Visibility = Visibility.Visible;
        SearchList.Children.Clear();
        var generation = ++_searchGeneration;
        var hits = HomeSearch.Find(SearchBox.Text, App.MediaLibrary.GetItems(), Playlists.All());
        SearchEmpty.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var hit in hits)
        {
            SearchList.Children.Add(SearchRow(hit, generation));
        }
    }

    private UIElement SearchRow(HomeHit hit, int generation)
    {
        var photo = hit.Kind == HomeHitKind.LibraryPhoto;
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false
        };
        image.ImageFailed += (_, _) => image.Source = null;
        var thumb = new Border
        {
            Width = 120,
            Height = 68,
            CornerRadius = new CornerRadius(6),
            Background = ThemeBrush("SubtleFillColorSecondaryBrush"),
            IsHitTestVisible = false,
            Child = new Grid
            {
                IsHitTestVisible = false,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = photo ? "\uE91B" : "\uE714",
                        FontSize = 20,
                        Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    },
                    image
                }
            }
        };
        ShowPicture(image, hit, generation);
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = hit.Title,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                },
                new TextBlock
                {
                    Text = hit.Source,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2
                }
            }
        };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        row.Children.Add(thumb);
        row.Children.Add(text);
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8),
            Content = row
        };
        button.SizeChanged += (_, args) =>
        {
            var room = args.NewSize.Width - thumb.Width - row.ColumnSpacing - button.Padding.Left - button.Padding.Right;
            text.MaxWidth = room > 40 ? room : 40;
        };
        button.Click += (_, _) => OpenHit(hit);
        if (photo)
        {
            return button;
        }

        var actions = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(QueueButton("Play next", "Put this first in the queue", next: true));
        actions.Children.Add(QueueButton("Add to queue", "Add this at the end of the queue", next: false));
        var playNext = new MenuFlyoutItem { Text = "Play next" };
        playNext.Click += (_, _) => QueueHit(hit, next: true);
        var add = new MenuFlyoutItem { Text = "Add to queue" };
        add.Click += (_, _) => QueueHit(hit, next: false);
        var menu = new MenuFlyout();
        menu.Items.Add(playNext);
        menu.Items.Add(add);
        button.ContextFlyout = menu;
        var shell = new Grid { ColumnSpacing = 8 };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(actions, 1);
        shell.Children.Add(button);
        shell.Children.Add(actions);
        return shell;

        Button QueueButton(string label, string tip, bool next)
        {
            var queue = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 4, 10, 4)
            };
            ToolTipService.SetToolTip(queue, tip);
            queue.Click += (_, _) => QueueHit(hit, next);
            return queue;
        }
    }

    private void ShowPicture(Image image, HomeHit hit, int generation)
    {
        if (hit.Kind != HomeHitKind.Online && !string.IsNullOrWhiteSpace(hit.FilePath))
        {
            if (hit.Kind == HomeHitKind.LibraryPhoto)
            {
                if (!File.Exists(hit.FilePath))
                {
                    return;
                }

                try
                {
                    image.Source = new BitmapImage
                    {
                        DecodePixelWidth = 240,
                        UriSource = new Uri(hit.FilePath, UriKind.Absolute),
                        CreateOptions = BitmapCreateOptions.IgnoreImageCache
                    };
                }
                catch (Exception)
                {
                    image.Source = null;
                }

                return;
            }

            _ = LoadVideoThumbAsync(image, hit.FilePath, generation);
            return;
        }

        if (!string.IsNullOrWhiteSpace(hit.Thumbnail))
        {
            LoadRemoteThumb(image, hit.Thumbnail);
        }
    }

    private static void LoadRemoteThumb(Image image, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var imageUri))
        {
            return;
        }

        try
        {
            image.Source = new BitmapImage
            {
                DecodePixelWidth = 240,
                UriSource = imageUri
            };
        }
        catch (Exception)
        {
            image.Source = null;
        }
    }

    private async Task LoadVideoThumbAsync(Image image, string path, int generation)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 240);
            if (generation != _searchGeneration || _left || thumb is null || thumb.Size == 0)
            {
                return;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumb);
            if (generation != _searchGeneration || _left)
            {
                return;
            }

            image.Source = bitmap;
        }
        catch (Exception)
        {
            if (generation == _searchGeneration)
            {
                image.Source = null;
            }
        }
    }

    private static Brush? ThemeBrush(string key)
        => Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;

    private void QueueHit(HomeHit hit, bool next)
    {
        if (hit.Kind == HomeHitKind.LibraryPhoto)
        {
            return;
        }

        if (hit.Kind == HomeHitKind.LibraryVideo)
        {
            if (string.IsNullOrWhiteSpace(hit.FilePath) || !File.Exists(hit.FilePath))
            {
                Status("That video is no longer on this PC.", InfoBarSeverity.Warning);
                return;
            }

            var added = next
                ? PlayQueue.PlayNextFile(hit.Title, hit.FilePath)
                : PlayQueue.AddFile(hit.Title, hit.FilePath);
            Status(
                added is null
                    ? "That video could not be queued."
                    : next ? $"\"{hit.Title}\" will play next." : $"Added \"{hit.Title}\" to the queue.",
                added is null ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            return;
        }

        var list = string.IsNullOrEmpty(hit.PlaylistId) ? null : Playlists.Find(hit.PlaylistId);
        if (list is null || hit.PlaylistIndex < 0 || hit.PlaylistIndex >= list.Videos.Count)
        {
            Status("That video is no longer in a playlist.", InfoBarSeverity.Warning);
            return;
        }

        var queued = next
            ? PlayQueue.PlayNext(list.Videos[hit.PlaylistIndex])
            : PlayQueue.Add(list.Videos[hit.PlaylistIndex]);
        Status(
            queued is null
                ? "That video could not be queued."
                : next ? $"\"{queued.Title}\" will play next." : $"Added \"{queued.Title}\" to the queue.",
            queued is null ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
    }

    private void OpenHit(HomeHit hit)
    {
        if (hit.Kind == HomeHitKind.Online)
        {
            if (!string.IsNullOrEmpty(hit.PlaylistId))
            {
                NavigationHelper.OpenPlayer(new PlaylistOpenRequest(hit.PlaylistId, hit.PlaylistIndex));
            }

            return;
        }

        var item = App.MediaLibrary.GetItems().FirstOrDefault(media =>
            string.Equals(media.FilePath, hit.FilePath, StringComparison.OrdinalIgnoreCase));
        if (item is null || !File.Exists(item.FilePath))
        {
            Status(item is { IsMissing: true }
                ? "This file is missing. Locate it in the library to keep saved words, bookmarks, and the playback position."
                : "That file is no longer on this PC.", InfoBarSeverity.Warning);
            return;
        }

        if (item.IsVideo)
        {
            NavigationHelper.OpenPlayer(item);
            return;
        }

        Frame.Navigate(typeof(MediaPreviewPage), item);
    }

    private void OpenRecent(MediaItem item)
    {
        if (!File.Exists(item.FilePath))
        {
            Status(item.IsMissing
                ? "This file is missing. Locate it in the library to keep saved words, bookmarks, and the playback position."
                : "That file is no longer on this PC.", InfoBarSeverity.Warning);
            Show();
            return;
        }

        if (item.IsVideo)
        {
            NavigationHelper.OpenPlayer(item);
            return;
        }

        Frame.Navigate(typeof(MediaPreviewPage), item);
    }

    private static string RecentLabel(MediaItem item)
    {
        if (item.FolderName.Contains(LibraryFolder.Screenshots, StringComparison.OrdinalIgnoreCase))
        {
            return "Screenshot";
        }

        if (item.Kind == MediaKind.Recording || item.FolderName.Contains(LibraryFolder.Recordings, StringComparison.OrdinalIgnoreCase))
        {
            return "Recording";
        }

        return item.IsVideo ? "Video" : "Photo";
    }

    private void ShowWords()
    {
        WordList.Children.Clear();
        var words = SavedWords.All().Where(SavedWords.CanOpen).Take(8).ToArray();
        WordsSection.Visibility = words.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var word in words)
        {
            WordList.Children.Add(WordRow(word));
        }
    }

    private UIElement WordRow(SavedWord word)
    {
        var title = string.IsNullOrWhiteSpace(word.Turkish) ? word.English : $"{word.English} — {word.Turkish}";
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = WordPlace(word),
                        Opacity = 0.7,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            }
        };
        button.Click += (_, _) => OpenWord(word);
        return button;
    }

    private void OpenWord(SavedWord word)
    {
        if (!string.IsNullOrWhiteSpace(word.PageUrl))
        {
            if (App.MainAppWindow is MainWindow window)
            {
                window.OpenSavedStream(word);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(word.VideoPath) || word.TimeMs is not long time || !File.Exists(word.VideoPath))
        {
            Status("That video is no longer on this PC.", InfoBarSeverity.Warning);
            return;
        }

        var item = App.MediaLibrary.GetItems().FirstOrDefault(media =>
                string.Equals(media.FilePath, word.VideoPath, StringComparison.OrdinalIgnoreCase))
            ?? new MediaItem
            {
                Id = word.VideoPath,
                Kind = MediaKind.Video,
                DisplayName = Path.GetFileName(word.VideoPath),
                FilePath = word.VideoPath,
                ImportedAt = new DateTimeOffset(File.GetCreationTimeUtc(word.VideoPath)),
                FileSizeBytes = new FileInfo(word.VideoPath).Length
            };
        NavigationHelper.OpenPlayer(new VideoOpenRequest(item, time, word));
    }

    private static string WordPlace(SavedWord word)
    {
        var name = !string.IsNullOrWhiteSpace(word.SourceName)
            ? word.SourceName.Trim()
            : !string.IsNullOrWhiteSpace(word.VideoPath)
                ? Path.GetFileName(word.VideoPath)
                : null;
        if (word.TimeMs is not long time)
        {
            return name ?? "Saved word";
        }

        var clock = ContinueClock(time);
        return name is null ? clock : $"{name} · {clock}";
    }

    private void ShowDownloads()
    {
        if (_left)
        {
            return;
        }

        DownloadList.Children.Clear();
        var active = DownloadQueueHub.Items.Where(ShownOnHome).Take(6).ToArray();
        DownloadsSection.Visibility = active.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in active)
        {
            DownloadList.Children.Add(new StackPanel
            {
                Padding = new Thickness(12, 10, 12, 10),
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = item.Title,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = item.Detail,
                        Opacity = 0.7,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            });
        }

        var more = DownloadQueueHub.Items.Count(ShownOnHome) - active.Length;
        if (more > 0)
        {
            DownloadList.Children.Add(new TextBlock
            {
                Text = more == 1 ? "1 more on the Download page" : $"{more} more on the Download page",
                Margin = new Thickness(12, 0, 12, 12),
                Opacity = 0.7
            });
        }
    }

    private static bool ShownOnHome(DownloadQueueItem item)
        => item.Status is "Queued" or "Downloading" or "Paused" or "Ready" or "Failed";

    private void ContinueAll_Click(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(HomePage), HomeView.ContinueAll);

    private void PlaylistsAll_Click(object sender, RoutedEventArgs e) => OpenSection(typeof(PlaylistsPage));

    private void RecentAll_Click(object sender, RoutedEventArgs e) => OpenSection(typeof(LibraryPage));

    private void WordsAll_Click(object sender, RoutedEventArgs e) => OpenSection(typeof(SavedWordsPage));

    private void DownloadsAll_Click(object sender, RoutedEventArgs e) => OpenSection(typeof(DownloadPage));

    private void OpenSection(Type page, object? parameter = null)
    {
        if (!Frame.Navigate(page, parameter))
        {
            return;
        }

        Frame.BackStack.Clear();
        if (App.MainAppWindow is MainWindow window)
        {
            window.SyncNavigationSelection();
        }
    }

    private void Downloads_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        OpenSection(typeof(DownloadPage));
    }

    private void Downloads_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _watchedDownloads.ToArray())
            {
                item.PropertyChanged -= Download_Changed;
            }

            _watchedDownloads.Clear();
            foreach (var item in DownloadQueueHub.Items)
            {
                WatchDownload(item);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<DownloadQueueItem>())
            {
                WatchDownload(item);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<DownloadQueueItem>())
            {
                item.PropertyChanged -= Download_Changed;
                _watchedDownloads.Remove(item);
            }
        }

        Dispatch(RefreshAfterDownload);
    }

    private void WatchDownload(DownloadQueueItem item)
    {
        if (_watchedDownloads.Contains(item))
        {
            return;
        }

        item.PropertyChanged += Download_Changed;
        _watchedDownloads.Add(item);
    }

    private void Download_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(DownloadQueueItem.Status) or nameof(DownloadQueueItem.Detail)))
        {
            return;
        }

        Dispatch(RefreshAfterDownload);
    }

    private void RefreshAfterDownload()
    {
        if (HasSearch())
        {
            return;
        }

        ShowDownloads();
        var any = ContinueSection.Visibility == Visibility.Visible
            || PlaylistsSection.Visibility == Visibility.Visible
            || RecentSection.Visibility == Visibility.Visible
            || WordsSection.Visibility == Visibility.Visible
            || DownloadsSection.Visibility == Visibility.Visible;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Dispatch(Action action)
    {
        if (_left)
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_left)
            {
                action();
            }
        });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainAppWindow is not MainWindow window)
        {
            return;
        }

        var files = await FilePickerHelper.PickMediaAsync(window);
        if (_left || files.Count == 0)
        {
            return;
        }

        var choice = await ImportChoiceDialog.AskAsync(XamlRoot, files.Count);
        if (_left || choice is null)
        {
            return;
        }

        var linked = choice == ImportChoice.Link;
        var imported = 0;
        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                if (linked && !string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
                {
                    await Task.Run(() => App.MediaLibrary.LinkMedia(file.Path, null));
                }
                else
                {
                    await Task.Run(() => App.MediaLibrary.ImportMedia(file.Path, null));
                }

                imported++;
            }
            catch (Exception)
            {
                failed++;
            }
        }

        if (_left)
        {
            return;
        }

        Show();
        if (failed == 0)
        {
            Status(linked
                ? imported == 1 ? "Added 1 file from its current location." : $"Added {imported} files from their current location."
                : imported == 1 ? "Imported 1 file." : $"Imported {imported} files.", InfoBarSeverity.Success);
        }
        else if (imported == 0)
        {
            Status(linked ? "Could not add those files." : "Could not import those files.", InfoBarSeverity.Error);
        }
        else
        {
            Status(linked
                ? $"Added {imported} files from their current location. {failed} could not be added."
                : $"Imported {imported} files. {failed} could not be imported.", InfoBarSeverity.Warning);
        }
    }

    private async void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        var address = new TextBox { PlaceholderText = "https://…", MinWidth = 420 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Paste a video address. It plays here and is not saved.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(address);
        var dialog = new ContentDialog
        {
            Title = "Open link",
            Content = panel,
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _left)
        {
            return;
        }

        if (!StreamLink.TryNormalize(address.Text, out var url))
        {
            Status(StreamLink.EnterAddressMessage, InfoBarSeverity.Error);
            return;
        }

        var generation = ++_linkGeneration;
        _linkCheck?.Cancel();
        _linkCheck = new CancellationTokenSource();
        var token = _linkCheck.Token;
        Status("Checking the link…", InfoBarSeverity.Informational);
        try
        {
            var result = await StreamLink.CheckAsync(url, token);
            if (generation != _linkGeneration || token.IsCancellationRequested || _left)
            {
                return;
            }

            if (result.Status != StreamCheckStatus.Media)
            {
                Status(result.Message, InfoBarSeverity.Error);
                return;
            }

            NavigationHelper.OpenPlayer(new StreamOpenRequest(result.Url, StreamLink.DisplayName(result.Url)));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (generation == _linkGeneration && !_left)
            {
                Status(StreamLink.OpenFailedMessage, InfoBarSeverity.Error);
            }
        }
    }

    private void Status(string message, InfoBarSeverity severity)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private static string ContinueName(string key)
    {
        if (Uri.TryCreate(key, UriKind.Absolute, out var page) && page.Host.Length > 0)
        {
            return page.Host;
        }

        return Path.GetFileName(key);
    }

    private static string ContinuePlace(long timeMs, long durationMs)
    {
        var at = ContinueClock(timeMs);
        if (durationMs <= timeMs + 1_000)
        {
            return $"At {at}";
        }

        return $"At {at} · {ContinueClock(durationMs - timeMs)} left";
    }

    private static double ContinueFraction(long timeMs, long durationMs)
        => durationMs > timeMs ? timeMs / (double)durationMs : 0;

    private static string ContinueClock(long durationMs)
    {
        if (durationMs < 0)
        {
            return "0:00";
        }

        var totalSeconds = durationMs / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
    }
}

internal enum HomeView
{
    ContinueAll
}
