using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FoxFix.App;

/// <summary>A short message shown in the middle of the window, over everything else, until the user closes it.</summary>
public sealed partial class NoticeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _message = "";

    public void Show(string title, string message)
    {
        Title = title;
        Message = message;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;
}