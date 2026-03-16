using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Aion2Dashboard.Models;
using Aion2Dashboard.Services;
using Aion2Dashboard.ViewModels;

namespace Aion2Dashboard;

public partial class MainWindow : Window
{
    private const int WmHotKey = 0x0312;
    private const int ResetHotkeyId = 0x5001;
    private const int FullResetHotkeyId = 0x5002;

    private readonly MainViewModel _viewModel;
    private HwndSource? _hwndSource;

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
        SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        PreviewKeyDown -= OnPreviewKeyDown;
        SourceInitialized -= OnSourceInitialized;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        UnregisterGlobalHotkeys();
        _viewModel.Dispose();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);
        RegisterGlobalHotkeys();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ResetHotkey) or nameof(MainViewModel.FullResetHotkey))
        {
            RegisterGlobalHotkeys();
        }
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
        ApplyGlobalHotkeys();
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

    private void RegisterGlobalHotkeys()
    {
        if (_hwndSource is null)
        {
            return;
        }

        UnregisterGlobalHotkeys();
        ApplyGlobalHotkeys();
    }

    private void ApplyGlobalHotkeys()
    {
        if (_hwndSource is null)
        {
            return;
        }

        var messages = new List<string>();
        RegisterHotkey(_hwndSource.Handle, ResetHotkeyId, _viewModel.ResetHotkey, "DPS 리셋", messages);
        RegisterHotkey(_hwndSource.Handle, FullResetHotkeyId, _viewModel.FullResetHotkey, "전체 리셋", messages);

        if (messages.Count == 0)
        {
            _viewModel.SetStatusMessage($"전역 단축키 적용 완료: {_viewModel.ResetHotkey} / {_viewModel.FullResetHotkey}");
        }
        else
        {
            _viewModel.SetStatusMessage(string.Join(" | ", messages));
        }
    }

    private void UnregisterGlobalHotkeys()
    {
        if (_hwndSource is null)
        {
            return;
        }

        UnregisterHotKey(_hwndSource.Handle, ResetHotkeyId);
        UnregisterHotKey(_hwndSource.Handle, FullResetHotkeyId);
    }

    private static void RegisterHotkey(IntPtr handle, int id, string hotkeyText, string label, List<string> messages)
    {
        if (!TryParseHotkey(hotkeyText, out var modifiers, out var key))
        {
            messages.Add($"{label} 단축키 형식 오류");
            return;
        }

        if (!RegisterHotKey(handle, id, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            messages.Add($"{label} 단축키 등록 실패");
        }
    }

    private static bool TryParseHotkey(string hotkeyText, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;

        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            return false;
        }

        var parts = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (!Enum.TryParse(parts[^1], true, out key))
        {
            return false;
        }

        foreach (var part in parts[..^1])
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0002;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0001;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0004;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0008;
            }
        }

        return modifiers != 0 || key != Key.None;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey)
        {
            switch (wParam.ToInt32())
            {
                case ResetHotkeyId:
                    _viewModel.TriggerResetHotkey();
                    handled = true;
                    break;
                case FullResetHotkeyId:
                    _viewModel.TriggerFullResetHotkey();
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
