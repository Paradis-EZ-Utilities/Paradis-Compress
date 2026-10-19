using System.Windows;
using System.Windows.Controls;

namespace ParadisCompress.UI;

/// <summary>A small dark question box whose buttons say what they do. The first button is the default.</summary>
public partial class ChoiceDialog : Window
{
    public int Choice { get; private set; } = -1;

    public ChoiceDialog(string title, string message, IReadOnlyList<string> buttons)
    {
        InitializeComponent();
        Title = $"Paradis Compress — {title}";
        TitleText.Text = title;
        MessageText.Text = message;
        for (int i = 0; i < buttons.Count; i++)
        {
            int index = i;
            var b = new Button
            {
                Content = buttons[i],
                Style = (Style)FindResource(i == 0 ? "PrimaryButton" : "FlatButton"),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(14, 8, 14, 8),
                FontSize = 13,
                IsDefault = i == 0,
                IsCancel = i == buttons.Count - 1,
            };
            b.Click += (_, _) => { Choice = index; Close(); };
            ButtonPanel.Children.Add(b);
        }
    }
}
