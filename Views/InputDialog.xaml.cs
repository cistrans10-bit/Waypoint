using System.Windows;
using Waypoint.Services;

namespace Waypoint.Views;

public partial class InputDialog : Window
{
    readonly bool selectNameOnly;
    readonly Func<string, string?>? validate;

    InputDialog(string heading, string label, string initial, bool selectNameOnly, Func<string, string?>? validate)
    {
        InitializeComponent();
        Title = heading;
        HeadingText.Text = heading;
        LabelText.Text = label;
        InputBox.Text = initial;
        this.selectNameOnly = selectNameOnly;
        this.validate = validate;
    }

    /// <summary>Asks for a single line of text. Returns null when cancelled.</summary>
    /// <param name="validate">Returns an error to show, or null when the text is acceptable.</param>
    public static string? Prompt(Window owner, string heading, string label, string initial,
        bool selectNameOnly = false, Func<string, string?>? validate = null)
    {
        var dialog = new InputDialog(heading, label, initial, selectNameOnly, validate) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.InputBox.Text.Trim() : null;
    }

    void Window_SourceInitialized(object? sender, EventArgs e) => ThemeService.ApplyWindowFrame(this);

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InputBox.Focus();
        int dot = selectNameOnly ? InputBox.Text.LastIndexOf('.') : -1;
        if (dot > 0)
            InputBox.Select(0, dot);
        else
            InputBox.SelectAll();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        string text = InputBox.Text.Trim();
        if (validate?.Invoke(text) is { } error)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            InputBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
