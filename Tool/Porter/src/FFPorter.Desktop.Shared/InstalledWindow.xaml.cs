using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Theme;

namespace FFPorter.Desktop;

public sealed class InstalledItem(Installed source) : Observable
{
    private ImageSource? _picture;

    public Installed Source { get; } = source;
    public string Title => Source.Title;
    public string Meta => string.Join("  ·  ", ((string[])[Source.Kind, Source.Name, Source.Size > 0 ? Job.SizeText(Source.Size) : ""]).Where(part => part.Length > 0));
    public ImageSource? Picture { get => _picture; set { if (Set(ref _picture, value)) Raise(nameof(HasPicture)); } }
    public bool HasPicture => Picture != null;
}

public partial class InstalledWindow : Window
{
    private const int PictureWidth = 160;

    private readonly ObservableCollection<InstalledItem> _items = [];
    private readonly ICollectionView _view;
    private string _kind = "";

    public InstalledWindow(Window? owner, IReadOnlyList<Installed> found)
    {
        InitializeComponent();
        ThemeManager.StyleTitleBar(this);
        if (owner != null && new WindowInteropHelper(owner).Handle != IntPtr.Zero)
            Owner = owner;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        foreach (Installed item in found)
            _items.Add(new InstalledItem(item));
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = Matches;
        Items.ItemsSource = _view;
        foreach (RadioButton filter in (RadioButton[])[AllFilter, ZombiesFilter, MultiplayerFilter, ModsFilter])
            filter.Checked += FilterChanged;
        ShowList();
        _ = LoadPicturesAsync();
    }

    public IReadOnlyList<string> Chosen { get; private set; } = [];

    private bool Matches(object item) => item is InstalledItem entry && (_kind.Length == 0 || entry.Source.Type == _kind) && Sought(entry);

    private bool Sought(InstalledItem entry)
    {
        string query = SearchBox.Text.Trim();
        return query.Length == 0 || entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Source.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> Selected() => [.. Items.SelectedItems.Cast<InstalledItem>().Select(item => item.Source.Folder)];

    private void ShowList()
    {
        bool any = _view.Cast<object>().Any();
        Items.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = _items.Count == 0 ? "Nothing was found" : SearchBox.Text.Trim().Length > 0 ? "Nothing matches your search" : _kind switch
        {
            CommunityKinds.Zombies => "No Zombies maps were found",
            CommunityKinds.Multiplayer => "No Multiplayer maps were found",
            CommunityKinds.Mod => "No mods were found",
            _ => "Nothing was found",
        };
        EmptyText.Text = _items.Count == 0 ? "Nothing in your game files folder or Steam Workshop." : "";
        EmptyText.Visibility = EmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        InstalledItem[] found = [.. _items.Where(Sought)];
        string Count(string kind) => _items.Count == 0 ? "" : "  " + found.Count(item => kind.Length == 0 || item.Source.Type == kind);
        AllCount.Text = Count("");
        ZombiesCount.Text = Count(CommunityKinds.Zombies);
        MultiplayerCount.Text = Count(CommunityKinds.Multiplayer);
        ModsCount.Text = Count(CommunityKinds.Mod);
        ShowCount();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        _kind = (sender as FrameworkElement)?.Tag as string ?? "";
        _view.Refresh();
        ShowList();
    }

    private void ShowCount()
    {
        int chosen = Items.SelectedItems.Count;
        AddButton.IsEnabled = chosen > 0;
        AddButton.Content = chosen > 1 ? $"Add {chosen}" : "Add";
        CountText.Text = _items.Count == 0 ? "" : chosen > 0 ? $"{chosen} of {_items.Count} selected" : $"{_items.Count} found · Ctrl+A selects all";
    }

    private async Task LoadPicturesAsync()
    {
        foreach (InstalledItem item in _items.Where(item => item.Source.Picture != null).ToList())
        {
            string file = item.Source.Picture!;
            item.Picture = await Task.Run(() =>
            {
                try
                {
                    var image = new BitmapImage();
                    using FileStream stream = File.OpenRead(file);
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = PictureWidth;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return (ImageSource?)image;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException
                    or System.Runtime.InteropServices.COMException)
                {
                    return null;
                }
            });
        }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        ShowList();
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowCount();

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || Keyboard.Modifiers != ModifierKeys.Control || Items.Visibility != Visibility.Visible)
            return;
        Items.SelectAll();
        e.Handled = true;
    }

    private void ItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Items.SelectedItems.Count == 1 && (e.OriginalSource as FrameworkElement)?.DataContext is InstalledItem)
            AddClick(sender, e);
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        Chosen = Selected();
        if (Chosen.Count > 0)
            DialogResult = true;
    }
}
