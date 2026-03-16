using System.Windows;
using System.Windows.Input;
using Aion2Dashboard.ViewModels;

namespace Aion2Dashboard;

public partial class SettingsWindow : Window
{
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
}
