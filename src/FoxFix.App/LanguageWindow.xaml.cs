using System.Windows;
using FoxFix.Core;

namespace FoxFix.App;

/// <summary>The very first question: Türkçe or English. It is shown before anything else and only until an answer is saved.</summary>
public partial class LanguageWindow : Window
{
    public LanguageWindow(ThemeManager? theme = null)
    {
        InitializeComponent();

        if (theme is not null)
        {
            WindowTheme.Follow(this, theme);
        }

        Loaded += (_, _) => TurkishButton.Focus();
    }

    /// <summary>"tr" or "en" once a button was pressed; null when the window was closed without choosing.</summary>
    public string? Chosen { get; private set; }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnTurkish(object sender, RoutedEventArgs e) => Choose(Strings.Turkish);

    private void OnEnglish(object sender, RoutedEventArgs e) => Choose(Strings.English);

    private void Choose(string code)
    {
        Chosen = code;
        DialogResult = true;
    }
}
