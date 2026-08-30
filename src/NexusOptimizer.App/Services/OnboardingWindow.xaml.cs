using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using NexusOptimizer.Core.Configuration;
// Disambiguazione WPF vs WinForms (UseWindowsForms=true nel csproj).
using UserControl = System.Windows.Controls.UserControl;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;

namespace NexusOptimizer.App.Services;

/// <summary>
/// Onboarding First Run: quattro passi brevi, senza eseguire scansioni o modifiche.
/// </summary>
public partial class OnboardingWindow : Window
{
    public const int Version = 2;

    private readonly AppConfig _config;
    private readonly ConfigStore _store;
    private readonly string[] _stepKeys = ["p1", "p2", "p3", "p4"];
    private int _step;

    public OnboardingWindow(AppConfig config, ConfigStore store)
    {
        _config = config;
        _store = store;
        InitializeComponent();
        BackBtn.Visibility = Visibility.Collapsed;
        Render();
    }

    private void Render()
    {
        PageHost.Content = BuildStep(_stepKeys[_step]);
        StepLabelText.Text = Locale.T("ob.progress")
            .Replace("{0}", (_step + 1).ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal)
            .Replace("{1}", _stepKeys.Length.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
        StepProgress.Value = _step + 1;

        BackBtn.IsEnabled = _step > 0;
        BackBtn.Visibility = _step > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_step == _stepKeys.Length - 1)
            NextBtn.Content = Locale.T("ob.finish");
        else
            NextBtn.Content = Locale.T("ob.next");
    }

    private FrameworkElement BuildStep(string key)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(Header($"ob.{key}.h"));
        panel.Children.Add(Lead(Locale.T($"ob.{key}.lead")));
        foreach (var i in new[] { 1, 2, 3 })
            panel.Children.Add(Bullet(Locale.T($"ob.{key}.b{i}")));
        return panel;
    }

    private static TextBlock Header(string key)
        => new()
        {
            Text = Locale.T(key),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8),
        };

    private static TextBlock Lead(string text)
        => new()
        {
            Text = text,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 18),
        };

    private static FrameworkElement Bullet(string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var marker = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = (Brush)Application.Current.FindResource("AccentSoftBrush"),
            Margin = new Thickness(0, 0, 11, 0),
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("AccentBrush"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            },
        };
        grid.Children.Add(marker);
        grid.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(grid.Children[1], 1);
        return grid;
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step == _stepKeys.Length - 1)
        {
            Complete();
            return;
        }
        _step++;
        Render();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step > 0)
        {
            _step--;
            Render();
        }
    }

    private void Complete()
    {
        _config.OnboardingDone = true;
        _config.OnboardingVersion = Version;
        try { _store.Save(_config); }
        catch { /* best-effort: l'onboarding si ripresenta al prossimo avvio */ }
        Close();
    }
}
