using System.Windows;
using Aion2Dashboard.Models;

namespace Aion2Dashboard;

public partial class CharacterDetailWindow : Window
{
    public CharacterDetailWindow(CharacterProfile profile)
    {
        InitializeComponent();
        DataContext = profile;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
