using System;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Ardel.Launcher.Views;

public sealed partial class InstancePackManagerPanel : UserControl
{
    public static readonly DependencyProperty ManagerProperty = DependencyProperty.Register(
        nameof(Manager),
        typeof(InstancePackManager),
        typeof(InstancePackManagerPanel),
        new PropertyMetadata(null, OnManagerChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(InstancePackManagerPanel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderHintProperty = DependencyProperty.Register(
        nameof(HeaderHint),
        typeof(string),
        typeof(InstancePackManagerPanel),
        new PropertyMetadata(string.Empty, OnHeaderHintChanged));

    private bool _paintActive;
    private bool _paintSelectTo;
    private int _paintAnchor = -1;
    private int _paintCurrent = -1;
    private bool[]? _paintBaseline;
    private Pointer? _paintPointer;
    private ScrollMode _scrollModeBeforePaint = ScrollMode.Enabled;
    private bool _paintHandlersAttached;
    private readonly PointerEventHandler _pointerPressed;
    private readonly PointerEventHandler _pointerMoved;
    private readonly PointerEventHandler _pointerReleased;
    private readonly PointerEventHandler _pointerCaptureLost;

    public InstancePackManagerPanel()
    {
        _pointerPressed = PacksList_PointerPressed;
        _pointerMoved = PacksList_PointerMoved;
        _pointerReleased = PacksList_PointerReleased;
        _pointerCaptureLost = PacksList_PointerCaptureLost;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public InstancePackManager? Manager
    {
        get => (InstancePackManager?)GetValue(ManagerProperty);
        set => SetValue(ManagerProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string HeaderHint
    {
        get => (string)GetValue(HeaderHintProperty);
        set => SetValue(HeaderHintProperty, value);
    }

    public Visibility HeaderHintVisibility =>
        string.IsNullOrWhiteSpace(HeaderHint) ? Visibility.Collapsed : Visibility.Visible;

    private static void OnHeaderHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InstancePackManagerPanel panel)
            panel.Bindings.Update();
    }

    private static void OnManagerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InstancePackManagerPanel panel)
        {
            panel.DataContext = e.NewValue;
            panel.Bindings.Update();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachPaintHandlers();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachPaintHandlers();
        EndPaint();
    }

    private void AttachPaintHandlers()
    {
        if (_paintHandlersAttached || PacksListView is null)
            return;

        PacksListView.AddHandler(UIElement.PointerPressedEvent, _pointerPressed, true);
        PacksListView.AddHandler(UIElement.PointerMovedEvent, _pointerMoved, true);
        PacksListView.AddHandler(UIElement.PointerReleasedEvent, _pointerReleased, true);
        PacksListView.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLost, true);
        _paintHandlersAttached = true;
    }

    private void DetachPaintHandlers()
    {
        if (!_paintHandlersAttached || PacksListView is null)
            return;

        PacksListView.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressed);
        PacksListView.RemoveHandler(UIElement.PointerMovedEvent, _pointerMoved);
        PacksListView.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleased);
        PacksListView.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLost);
        _paintHandlersAttached = false;
    }

    private void FilterRadio_Click(object sender, RoutedEventArgs e)
    {
        if (Manager is null || sender is not FrameworkElement fe)
            return;

        if (int.TryParse(fe.Tag?.ToString(), out var index) && Manager.FilterIndex != index)
            Manager.FilterIndex = index;
    }

    private void PacksList_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_paintActive || PacksListView is null || Manager is null)
            return;

        if (e.Pointer.PointerDeviceType is not (PointerDeviceType.Mouse
            or PointerDeviceType.Pen
            or PointerDeviceType.Touch))
            return;

        var point = e.GetCurrentPoint(PacksListView);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !point.Properties.IsLeftButtonPressed)
            return;

        if (IsPointerOverButton(e.OriginalSource as DependencyObject))
            return;

        if (!TryGetIndexAt(point.Position, out var index) || index < 0)
            return;

        var item = Manager.Items[index];
        _paintSelectTo = !item.IsSelected;
        _paintAnchor = index;
        _paintCurrent = index;
        _paintBaseline = Manager.Items.Select(m => m.IsSelected).ToArray();
        _paintActive = true;
        _paintPointer = e.Pointer;
        _scrollModeBeforePaint = ScrollViewer.GetVerticalScrollMode(PacksListView);
        ScrollViewer.SetVerticalScrollMode(PacksListView, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollMode(PacksListView, ScrollMode.Disabled);
        ApplyPaintRange();
        PacksListView.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PacksList_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_paintActive || PacksListView is null)
            return;

        var localPoint = e.GetCurrentPoint(PacksListView).Position;
        if (!TryGetIndexAt(localPoint, out var index) || index < 0)
            index = EstimateIndexFromY(localPoint.Y);

        if (index < 0 || index == _paintCurrent)
            return;

        _paintCurrent = index;
        ApplyPaintRange();
    }

    private void PacksList_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        EndPaint(PacksListView, e.Pointer);

    private void PacksList_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        EndPaint();

    private void ApplyPaintRange()
    {
        if (Manager is null || _paintBaseline is null || _paintAnchor < 0)
            return;

        int lo = Math.Min(_paintAnchor, _paintCurrent);
        int hi = Math.Max(_paintAnchor, _paintCurrent);
        var items = Manager.Items;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            bool desired = i >= lo && i <= hi
                ? _paintSelectTo
                : (i < _paintBaseline.Length && _paintBaseline[i]);

            if (item.IsSelected == desired)
                continue;

            item.SuppressSelectionNotify = true;
            item.IsSelected = desired;
            item.SuppressSelectionNotify = false;
        }

        Manager.RefreshSelectionState();
    }

    private bool TryGetIndexAt(Windows.Foundation.Point listLocalPoint, out int index)
    {
        index = -1;
        if (PacksListView is null || Manager is null)
            return false;

        Windows.Foundation.Point rootPoint;
        try
        {
            rootPoint = PacksListView.TransformToVisual(null).TransformPoint(listLocalPoint);
        }
        catch
        {
            rootPoint = listLocalPoint;
        }

        foreach (var el in VisualTreeHelper.FindElementsInHostCoordinates(rootPoint, PacksListView))
        {
            DependencyObject? cur = el;
            while (cur is not null && !ReferenceEquals(cur, PacksListView))
            {
                if (cur is ListViewItem container)
                {
                    index = PacksListView.IndexFromContainer(container);
                    return index >= 0;
                }

                if (cur is FrameworkElement { DataContext: ResourceItem item })
                {
                    index = Manager.Items.IndexOf(item);
                    return index >= 0;
                }

                cur = VisualTreeHelper.GetParent(cur);
            }
        }

        return false;
    }

    private int EstimateIndexFromY(double y)
    {
        if (PacksListView is null || Manager is null || Manager.Items.Count == 0)
            return -1;

        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < Manager.Items.Count; i++)
        {
            if (PacksListView.ContainerFromIndex(i) is not FrameworkElement fe)
                continue;

            var transform = fe.TransformToVisual(PacksListView);
            var topLeft = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double mid = topLeft.Y + fe.ActualHeight / 2;
            double dist = Math.Abs(mid - y);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        if (best >= 0)
            return best;

        return y < 0 ? 0 : Manager.Items.Count - 1;
    }

    private void EndPaint(UIElement? capturer = null, Pointer? pointer = null)
    {
        if (!_paintActive && _paintPointer is null)
            return;

        if (capturer is not null && pointer is not null)
        {
            try { capturer.ReleasePointerCapture(pointer); }
            catch { /* ignore */ }
        }

        if (PacksListView is not null)
        {
            ScrollViewer.SetVerticalScrollMode(PacksListView, _scrollModeBeforePaint);
            ScrollViewer.SetHorizontalScrollMode(PacksListView, ScrollMode.Enabled);
        }

        _paintActive = false;
        _paintPointer = null;
        _paintAnchor = -1;
        _paintCurrent = -1;
        _paintBaseline = null;
        Manager?.RefreshSelectionState();
    }

    private static bool IsPointerOverButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private async void PacksContainer_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            e.AcceptedOperation = items.Any(i => ResourceDrop.IsAccepted(i, modsOnly: false))
                ? DataPackageOperation.Copy
                : DataPackageOperation.None;
        }
        catch
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void PacksContainer_Drop(object sender, DragEventArgs e)
    {
        if (Manager is null ||
            !e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var def = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var targetFolder = System.IO.Path.Combine(
                InstanceSettingsViewModel.ActiveInstance?.InstanceDirectory ?? string.Empty,
                Manager.FolderName);
            if (string.IsNullOrWhiteSpace(targetFolder))
                return;

            await ResourceDrop.CopyItemsAsync(
                items.ToList(),
                targetFolder,
                modsOnly: false,
                ConfirmDialog.ConfirmOverwriteAsync,
                status =>
                {
                    if (InstanceSettingsViewModel.ActiveInstance is { } vm)
                        vm.StatusText = status;
                });

            InstanceSettingsViewModel.ActiveInstance?.LoadResourceLists();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InstancePackManagerPanel] DragDrop error: {ex.Message}");
        }
        finally
        {
            def.Complete();
        }
    }
}
