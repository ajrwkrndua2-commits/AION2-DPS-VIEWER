using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Aion2Dashboard.ViewModels;

namespace Aion2Dashboard;

public partial class SettingsWindow : Window
{
    private const string UnsafeHotkeyMessage = "전역 단축키는 Ctrl/Alt/Win 조합 또는 F1~F24 키만 권장됩니다. Shift+문자 조합은 한글 입력을 막을 수 있습니다.";

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Keyboard.ClearFocus();
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            textBox.Text = string.Empty;
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
            return;
        }

        if (key is Key.Tab or Key.Enter or Key.Return)
        {
            return;
        }

        var hotkeyText = BuildHotkeyText(key, Keyboard.Modifiers);
        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            e.Handled = true;
            return;
        }

        if (!IsSafeGlobalHotkey(key, Keyboard.Modifiers))
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.SetStatusMessage(UnsafeHotkeyMessage);
            }

            MessageBox.Show(
                UnsafeHotkeyMessage,
                "단축키 설정",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            e.Handled = true;
            return;
        }

        textBox.Text = hotkeyText;
        textBox.CaretIndex = textBox.Text.Length;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private static string BuildHotkeyText(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        var keyText = key switch
        {
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            _ => key.ToString()
        };

        if (string.IsNullOrWhiteSpace(keyText))
        {
            return string.Empty;
        }

        parts.Add(keyText);
        return string.Join("+", parts);
    }

    private static bool IsSafeGlobalHotkey(Key key, ModifierKeys modifiers)
    {
        var hasSafeModifier =
            modifiers.HasFlag(ModifierKeys.Control) ||
            modifiers.HasFlag(ModifierKeys.Alt) ||
            (modifiers & ModifierKeys.Windows) == ModifierKeys.Windows;

        return hasSafeModifier || IsFunctionKey(key);
    }

    private static bool IsFunctionKey(Key key)
    {
        return key >= Key.F1 && key <= Key.F24;
    }
}
