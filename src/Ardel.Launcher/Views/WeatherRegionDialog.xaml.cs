using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.Views;

public sealed partial class WeatherRegionDialog : UserControl
{
    private readonly WeatherService _weather;
    private bool _searching;
    private WeatherPlace? _pickedPlace;

    public WeatherRegionDialog(WeatherService weather, string? initialQuery = null)
    {
        _weather = weather;
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(initialQuery))
            QueryBox.Text = initialQuery.Trim();
    }

    public WeatherPlace? SelectedPlace => _pickedPlace ?? ResultsList.SelectedItem as WeatherPlace;

    public event EventHandler? SelectionChanged;

    private async void SearchButton_Click(object sender, RoutedEventArgs e) =>
        await SearchAsync().ConfigureAwait(true);

    private async void CurrentLocationButton_Click(object sender, RoutedEventArgs e) =>
        await UseCurrentLocationAsync().ConfigureAwait(true);

    private async void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;
        e.Handled = true;
        await SearchAsync().ConfigureAwait(true);
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is WeatherPlace place)
            _pickedPlace = place;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task UseCurrentLocationAsync()
    {
        if (_searching)
            return;

        _searching = true;
        CurrentLocationButton.IsEnabled = false;
        SearchButton.IsEnabled = false;
        ResultsList.ItemsSource = null;
        _pickedPlace = null;
        ResultsList.SelectedItem = null;
        SetStatus(Loc.Get(LocKeys.Settings_HomeWidgetsLocating));
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var position = await WeatherGeolocation.TryGetCurrentPositionAsync().ConfigureAwait(true);
            if (position is not { } coords)
            {
                SetStatus(Loc.Get(LocKeys.Settings_HomeWidgetsLocationDenied));
                return;
            }

            var place = await _weather
                .ReverseGeocodeAsync(coords.Latitude, coords.Longitude, Loc.ActiveLanguageTag)
                .ConfigureAwait(true);
            if (place is null)
            {
                place = new WeatherPlace(
                    Loc.Get(LocKeys.Settings_HomeWidgetsCurrentLocationLabel),
                    null,
                    null,
                    null,
                    coords.Latitude,
                    coords.Longitude,
                    null,
                    Loc.Get(LocKeys.Settings_HomeWidgetsCurrentLocationLabel));
            }

            _pickedPlace = place;
            ResultsList.ItemsSource = new[] { place };
            ResultsList.SelectedItem = place;
            ClearStatus();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.Format(LocKeys.Settings_HomeWidgetsLocationFailed, ex.Message));
        }
        finally
        {
            _searching = false;
            CurrentLocationButton.IsEnabled = true;
            SearchButton.IsEnabled = true;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task SearchAsync()
    {
        if (_searching)
            return;

        var query = QueryBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            SetStatus(Loc.Get(LocKeys.Settings_HomeWidgetsRegionPlaceholder));
            return;
        }

        _searching = true;
        SearchButton.IsEnabled = false;
        CurrentLocationButton.IsEnabled = false;
        ResultsList.ItemsSource = null;
        _pickedPlace = null;
        ResultsList.SelectedItem = null;
        SetStatus(Loc.Get(LocKeys.Settings_HomeWidgetsSearching));
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var results = await _weather
                .SearchPlacesAsync(query, Loc.ActiveLanguageTag)
                .ConfigureAwait(true);
            ResultsList.ItemsSource = results;
            if (results.Count == 0)
                SetStatus(Loc.Get(LocKeys.Settings_HomeWidgetsNoResults));
            else
                ClearStatus();
        }
        catch (Exception ex)
        {
            SetStatus(Loc.Format(LocKeys.Settings_HomeWidgetsSearchFailed, ex.Message));
        }
        finally
        {
            _searching = false;
            SearchButton.IsEnabled = true;
            CurrentLocationButton.IsEnabled = true;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    private void ClearStatus()
    {
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
    }

    public static async Task<WeatherPlace?> ShowAsync(
        XamlRoot xamlRoot,
        WeatherService weather,
        string? initialQuery = null)
    {
        var content = new WeatherRegionDialog(weather, initialQuery);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Get(LocKeys.Settings_HomeWidgetsRegionDialogTitle),
            Content = content,
            PrimaryButtonText = Loc.Get(LocKeys.Action_Apply),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        content.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = content.SelectedPlace is not null;

        var result = await dialog.SafeShowAsync();
        return result == ContentDialogResult.Primary ? content.SelectedPlace : null;
    }
}
