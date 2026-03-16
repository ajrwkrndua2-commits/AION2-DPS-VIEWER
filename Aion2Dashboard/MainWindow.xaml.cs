using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Aion2Dashboard.Models;
using Aion2Dashboard.Services;
using Aion2Dashboard.ViewModels;

namespace Aion2Dashboard;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new AtoolApiClient(),
            new DpsMeterService(),
            new OverlaySettingsStore(),
            BuildFlavor.IsDistribution);
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        PreviewKeyDown -= OnPreviewKeyDown;
        _viewModel.Dispose();
    }

    private void SearchKeyword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.ExecuteSearch();
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_viewModel) { Owner = this };
        window.ShowDialog();
    }

    private void ToggleCompact_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsCompactMode = !_viewModel.IsCompactMode;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async void LivePlayersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var sourceElement = e.OriginalSource as DependencyObject;
        var item = FindAncestor<ListViewItem>(sourceElement);
        var row = item?.DataContext as DpsPlayerRow ?? LivePlayersList.SelectedItem as DpsPlayerRow;
        await OpenDetailAsync(row);
    }

    private async void PinnedSearchCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        await OpenDetailAsync(_viewModel.PinnedSearchPlayer);
    }

    private async Task OpenDetailAsync(DpsPlayerRow? row)
    {
        try
        {
            if (row is null)
            {
                return;
            }

            var profile = await _viewModel.LoadCharacterDetailAsync(row, forceLookup: true);
            if (profile is null)
            {
                return;
            }

            var window = new CharacterDetailWindow(profile) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"상세 창을 여는 중 오류가 발생했습니다.\n\n{ex.Message}",
                "DPSVIEWER",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MatchesHotkey(e, _viewModel.ResetHotkey))
        {
            _viewModel.TriggerResetHotkey();
            e.Handled = true;
            return;
        }

        if (MatchesHotkey(e, _viewModel.FullResetHotkey))
        {
            _viewModel.TriggerFullResetHotkey();
            e.Handled = true;
        }
    }

    private static bool MatchesHotkey(KeyEventArgs e, string hotkeyText)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            return false;
        }

        var parts = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var keyPart = parts[^1];
        if (!Enum.TryParse<Key>(keyPart, true, out var expectedKey))
        {
            return false;
        }

        var expectedModifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                expectedModifiers |= ModifierKeys.Control;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                expectedModifiers |= ModifierKeys.Alt;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                expectedModifiers |= ModifierKeys.Shift;
            }
        }

        return e.Key == expectedKey && Keyboard.Modifiers == expectedModifiers;
    }
}
