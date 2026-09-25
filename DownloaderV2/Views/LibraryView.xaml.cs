using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DownloaderV2.ViewModels;

namespace DownloaderV2.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    private void TrackList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel && viewModel.PlayCommand.CanExecute(null))
            viewModel.PlayCommand.Execute(null);
    }

    private async void DeleteTrack_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel || viewModel.SelectedTrack is null) return;
        var choice = MessageBox.Show(
            "Yes: remove the track and delete its audio file.\nNo: remove it from the library only.",
            "Delete track",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return;
        await viewModel.DeleteSelectedAsync(choice == MessageBoxResult.Yes);
    }
}
