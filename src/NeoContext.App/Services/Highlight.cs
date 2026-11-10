using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace NeoContext.App.Services;

/// <summary>Attached properties that show a text with every match of a query in bold accent colour.</summary>
public static class Highlight
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Highlight), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.RegisterAttached("Query", typeof(string), typeof(Highlight), new PropertyMetadata("", OnChanged));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    public static string GetQuery(DependencyObject element) => (string)element.GetValue(QueryProperty);

    public static void SetQuery(DependencyObject element, string value) => element.SetValue(QueryProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        var text = GetText(block) ?? "";
        var query = (GetQuery(block) ?? "").Trim();
        block.Inlines.Clear();

        var position = 0;
        while (query.Length > 0)
        {
            // Ordinal matching keeps the match the same length as the query. Culture-aware matching can
            // match text of a different length, and the Substring below then throws.
            var index = text.IndexOf(query, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            if (index > position)
            {
                block.Inlines.Add(new Run { Text = text[position..index] });
            }

            block.Inlines.Add(new Run
            {
                Text = text.Substring(index, query.Length),
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            });
            position = index + query.Length;
        }

        if (position < text.Length)
        {
            block.Inlines.Add(new Run { Text = text[position..] });
        }
    }
}
