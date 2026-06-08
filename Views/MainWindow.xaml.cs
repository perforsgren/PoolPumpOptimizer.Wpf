using System.Windows;
using PoolPumpOptimizer.Wpf.ViewModels;

namespace PoolPumpOptimizer.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void SettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window = new SettingsWindow
        {
            Owner = this,
            DataContext = _viewModel
        };

        window.ShowDialog();
    }
}