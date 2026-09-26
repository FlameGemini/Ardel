using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Ardel.Launcher.Views;

public sealed partial class InstanceSettingsPage : Page
{
    public InstanceSettingsViewModel ViewModel { get; }

    private bool _modPaintActive;
    private bool _modPaintSelectTo;
    private int _modPaintAnchor = -1;
    private int _modPaintCurrent = -1;
    private bool[]? _modPaintBaseline;
    private Pointer? _modPaintPointer;
    private ScrollMode _modsScrollModeBeforePaint = ScrollMode.Enabled;
    private bool _modPaintHandlersAttached;
    private bool _suppressTabAnimation;
    private bool _entrancePlayed;
    private int _lastAnimatedTab = -1;
    private int _animGeneration;
    private UIElement? _currentPanel;
    private Storyboard? _tabStoryboard;
    private readonly PointerEventHandler _modsPointerPressed;
    private readonly PointerEventHandler _modsPointerMoved;
    private readonly PointerEventHandler _modsPointerReleased;
    private readonly PointerEventHandler _modsPointerCaptureLost;

    private static readonly TimeSpan TabMotionDuration = TimeSpan.FromMilliseconds(75);

    public InstanceSettingsPage()
    {
        _modsPointerPressed = ModsList_PointerPressed;
        _modsPointerMoved = ModsList_PointerMoved;
        _modsPointerReleased = ModsList_PointerReleased;
        _modsPointerCaptureLost = ModsList_PointerCaptureLost;

        ViewModel = App.Services.GetRequiredService<InstanceSettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.InstanceDeleted += OnInstanceDeleted;
        ViewModel.NavigateToInstancesRequested += OnNavigateToInstancesRequested;
        ViewModel.OpenInstanceRequested += OnOpenInstanceRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += (_, _) =>
        {
            Loaded -= OnLoaded;
            if (ContentHost is not null)
                ContentHost.SizeChanged -= ContentHost_SizeChanged;
            // Keep ViewModel subscriptions — page is navigation-cached across visits.
            DetachModPaintHandlers();
            EndModPaint();
            ResetPanelContainerVisual();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AttachXamlRoot(XamlRoot);
        AttachModPaintHandlers();
        SyncCategoryListFromTab();
        _suppressTabAnimation = true;
        try
        {
            ApplyTabVisibility(animate: false);
        }
        finally
        {
            _suppressTabAnimation = false;
        }

        if (ContentHost is not null)
        {
            ContentHost.SizeChanged -= ContentHost_SizeChanged;
            ContentHost.SizeChanged += ContentHost_SizeChanged;
            UpdateContentClip();
        }

        // After first layout tick so Frame slide is already painting.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PlayEntranceOnce);
    }

    private void ContentHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateContentClip();

    private void UpdateContentClip()
    {
        if (SettingPanelsContainer is null)
            return;

        var w = SettingPanelsContainer.ActualWidth;
        var h = SettingPanelsContainer.ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        // Composition Offset can paint outside layout bounds; clip so slides never bleed under the rail.
        SettingPanelsContainer.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, w, h)
        };
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstanceSettingsViewModel.SelectedTabIndex)
            or nameof(InstanceSettingsViewModel.IsSavePropertiesOpen)
            or null)
        {
            SyncCategoryListFromTab();
            var animate = !_suppressTabAnimation &&
                          e.PropertyName == nameof(InstanceSettingsViewModel.SelectedTabIndex);
            ApplyTabVisibility(animate: animate);
        }
    }

    private void ApplyTabVisibility(bool animate = true)
    {
        try
        {
            var tab = ViewModel.SelectedTabIndex;
            var saveProps = ViewModel.IsSavePropertiesOpen && tab == 6;
            var shouldAnimate = animate && !saveProps && !_suppressTabAnimation;

            if (saveProps)
                FindName(nameof(SavePropertiesPanel));

            SetVisible(SettingsChromeGrid, !saveProps);
            SetVisible(SavePropertiesPanel, saveProps);

            if (saveProps)
            {
                CancelCrossfade();
                CollapseAllContentPanels();
                _currentPanel = null;
                _lastAnimatedTab = tab;
                return;
            }

            var incoming = GetPanelForTab(tab);
            if (incoming is null)
                return;

            if (!shouldAnimate)
            {
                CancelCrossfade();
                ShowOnlyPanel(incoming);
                _currentPanel = incoming;
                _lastAnimatedTab = tab;
                return;
            }

            var outgoing = _currentPanel;
            if (outgoing is null || ReferenceEquals(outgoing, incoming))
            {
                CancelCrossfade();
                ShowOnlyPanel(incoming);
                _currentPanel = incoming;
                _lastAnimatedTab = tab;
                return;
            }

            var dx = _lastAnimatedTab < 0 || tab > _lastAnimatedTab ? 16f : -16f;
            CrossfadePanels(outgoing, incoming, dx);
            _currentPanel = incoming;
            _lastAnimatedTab = tab;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InstanceSettingsPage] ApplyTabVisibility: {ex}");
        }
    }

    private UIElement? GetPanelForTab(int tab)
    {
        EnsureTabPanelLoaded(tab);
        return tab switch
        {
            0 => OverviewPanel,
            1 => JavaConfigPanel,
            2 => ManagePanel,
            3 => ModsPanel,
            4 => ResourcePacksPanel,
            5 => ShaderPacksPanel,
            6 => SavesPanel,
            7 => StatisticsPanel,
            _ => null
        };
    }

    /// <summary>Heavy tabs stay x:Load=False until first open so navigate can paint Overview immediately.</summary>
    private void EnsureTabPanelLoaded(int tab)
    {
        switch (tab)
        {
            case 1:
                FindName(nameof(JavaConfigPanel));
                break;
            case 2:
                FindName(nameof(ManagePanel));
                break;
            case 3:
                FindName(nameof(ModsPanel));
                AttachModPaintHandlers();
                break;
            case 4:
                FindName(nameof(ResourcePacksPanel));
                break;
            case 5:
                FindName(nameof(ShaderPacksPanel));
                break;
            case 6:
                FindName(nameof(SavesPanel));
                break;
            case 7:
                FindName(nameof(StatisticsPanel));
                break;
        }
    }

    private IEnumerable<UIElement?> AllContentPanels()
    {
        yield return OverviewPanel;
        yield return JavaConfigPanel;
        yield return ManagePanel;
        yield return ModsPanel;
        yield return ResourcePacksPanel;
        yield return ShaderPacksPanel;
        yield return SavesPanel;
        yield return StatisticsPanel;
    }

    private void CollapseAllContentPanels()
    {
        foreach (var panel in AllContentPanels())
            SetPanel(panel, visible: false);
    }

    private void ShowOnlyPanel(UIElement incoming)
    {
        foreach (var panel in AllContentPanels())
        {
            if (panel is null)
                continue;
            if (ReferenceEquals(panel, incoming))
            {
                ResetPanelVisual(panel);
                SetPanel(panel, visible: true);
            }
            else
            {
                StopPanelAnimations(panel);
                SetPanel(panel, visible: false);
            }
        }
    }

    private static void SetPanel(UIElement? element, bool visible)
    {
        if (element is null)
            return;
        element.Opacity = visible ? 1 : 0;
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element is FrameworkElement fe)
            fe.IsHitTestVisible = visible;
    }

    private static void SetVisible(UIElement? element, bool visible)
    {
        if (element is null)
            return;
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element is FrameworkElement fe)
            fe.IsHitTestVisible = visible;
    }

    private void CancelCrossfade()
    {
        _animGeneration++;
        StopTabStoryboard();
        foreach (var panel in AllContentPanels())
        {
            if (panel is null)
                continue;
            StopPanelAnimations(panel);
            ResetPanelVisual(panel);
            if (panel is FrameworkElement fe)
                fe.RenderTransform = null;
        }
        ResetContainerVisual();
    }

    private void StopTabStoryboard()
    {
        if (_tabStoryboard is null)
            return;
        try { _tabStoryboard.Stop(); } catch { /* ignore */ }
        _tabStoryboard = null;
    }

    private void ResetPanelContainerVisual() => CancelCrossfade();

    private void ResetContainerVisual()
    {
        if (SettingPanelsContainer is null)
            return;

        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(SettingPanelsContainer);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Offset");
            // Never leave the content host translucent — that made Overview look "missing".
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
        }
        catch
        {
            // composition not available
        }

        SettingPanelsContainer.Opacity = 1;
    }

    private static void StopPanelAnimations(UIElement element)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Offset");
        }
        catch
        {
            // ignore
        }
    }

    private static void ResetPanelVisual(UIElement element)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Offset");
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
        }
        catch
        {
            // ignore
        }

        element.Opacity = 1;
        if (element is FrameworkElement fe)
            fe.RenderTransform = null;
    }

    /// <summary>
    /// Settings-style crossfade on the two panels only (UIElement Opacity + TranslateTransform).
    /// Never animates SettingPanelsContainer / ContentHost opacity.
    /// </summary>
    private void CrossfadePanels(UIElement outgoing, UIElement incoming, float dx)
    {
        UpdateContentClip();
        var generation = ++_animGeneration;
        StopTabStoryboard();

        foreach (var panel in AllContentPanels())
        {
            if (panel is null || ReferenceEquals(panel, outgoing) || ReferenceEquals(panel, incoming))
                continue;
            StopPanelAnimations(panel);
            SetPanel(panel, visible: false);
            if (panel is FrameworkElement otherFe)
                otherFe.RenderTransform = null;
        }

        ResetPanelVisual(outgoing);
        ResetPanelVisual(incoming);

        outgoing.Visibility = Visibility.Visible;
        incoming.Visibility = Visibility.Visible;
        outgoing.Opacity = 1;
        incoming.Opacity = 0;
        if (outgoing is FrameworkElement outFe)
        {
            outFe.IsHitTestVisible = false;
            outFe.RenderTransform = new TranslateTransform { X = 0 };
        }
        if (incoming is FrameworkElement inFe)
        {
            inFe.IsHitTestVisible = false;
            inFe.RenderTransform = new TranslateTransform { X = dx };
        }

        if (outgoing is not FrameworkElement || incoming is not FrameworkElement)
        {
            ShowOnlyPanel(incoming);
            return;
        }

        var outTransform = (TranslateTransform)((FrameworkElement)outgoing).RenderTransform!;
        var inTransform = (TranslateTransform)((FrameworkElement)incoming).RenderTransform!;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TabMotionDuration);
        var sb = new Storyboard();
        sb.Children.Add(CreateDoubleAnimation(outgoing, "Opacity", 1, 0, duration, ease));
        sb.Children.Add(CreateDoubleAnimation(outTransform, "X", 0, -dx, duration, ease));
        sb.Children.Add(CreateDoubleAnimation(incoming, "Opacity", 0, 1, duration, ease));
        sb.Children.Add(CreateDoubleAnimation(inTransform, "X", dx, 0, duration, ease));
        sb.Completed += (_, _) =>
        {
            if (generation != _animGeneration)
                return;
            _tabStoryboard = null;
            SetPanel(outgoing, visible: false);
            ResetPanelVisual(outgoing);
            ResetPanelVisual(incoming);
            SetPanel(incoming, visible: true);
            ResetContainerVisual();
        };

        _tabStoryboard = sb;
        sb.Begin();
    }

    private static DoubleAnimation CreateDoubleAnimation(
        DependencyObject target,
        string property,
        double from,
        double to,
        Duration duration,
        EasingFunctionBase ease)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = ease,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        return anim;
    }

    /// <summary>One-shot content follow-in — Offset only; container Opacity stays 1.</summary>
    private void PlayEntranceOnce()
    {
        if (_entrancePlayed || SettingPanelsContainer is null)
            return;

        _entrancePlayed = true;
        UpdateContentClip();
        ResetContainerVisual();

        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(SettingPanelsContainer);
            var compositor = visual.Compositor;

            visual.StopAnimation("Offset");
            visual.Opacity = 1f;
            visual.Offset = new Vector3(0, 12, 0);

            var ease = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0f),
                new Vector2(0f, 1f));

            var offset = compositor.CreateVector3KeyFrameAnimation();
            offset.InsertKeyFrame(1f, Vector3.Zero, ease);
            offset.Duration = TabMotionDuration;
            offset.StopBehavior = AnimationStopBehavior.SetToFinalValue;

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            batch.Completed += (_, _) =>
            {
                try
                {
                    visual.Opacity = 1f;
                    visual.Offset = Vector3.Zero;
                    SettingPanelsContainer.Opacity = 1;
                }
                catch { /* ignore */ }
            };

            visual.StartAnimation("Offset", offset);
            batch.End();
        }
        catch
        {
            ResetContainerVisual();
        }
    }

    private void SyncCategoryListFromTab()
    {
        if (CategoryNavPanel is null)
            return;

        _isSyncingSelection = true;
        try
        {
            var tab = ViewModel.SelectedTabIndex;
            foreach (var child in CategoryNavPanel.Children)
            {
                if (child is not RadioButton rb)
                    continue;
                var tag = rb.Tag switch
                {
                    int i => i,
                    string s when int.TryParse(s, out var n) => n,
                    _ => -1
                };
                if (tag < 0)
                    continue;
                rb.IsChecked = tag == tab;
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private void AttachModPaintHandlers()
    {
        if (_modPaintHandlersAttached || ModsListView is null)
            return;

        // handledEventsToo: receive presses from row content (not when a Button already handled them).
        ModsListView.AddHandler(UIElement.PointerPressedEvent, _modsPointerPressed, true);
        ModsListView.AddHandler(UIElement.PointerMovedEvent, _modsPointerMoved, true);
        ModsListView.AddHandler(UIElement.PointerReleasedEvent, _modsPointerReleased, true);
        ModsListView.AddHandler(UIElement.PointerCaptureLostEvent, _modsPointerCaptureLost, true);
        _modPaintHandlersAttached = true;
    }

    private void DetachModPaintHandlers()
    {
        if (!_modPaintHandlersAttached || ModsListView is null)
            return;

        ModsListView.RemoveHandler(UIElement.PointerPressedEvent, _modsPointerPressed);
        ModsListView.RemoveHandler(UIElement.PointerMovedEvent, _modsPointerMoved);
        ModsListView.RemoveHandler(UIElement.PointerReleasedEvent, _modsPointerReleased);
        ModsListView.RemoveHandler(UIElement.PointerCaptureLostEvent, _modsPointerCaptureLost);
        _modPaintHandlersAttached = false;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _suppressTabAnimation = true;
        string? versionId = null;
        try
        {
            if (XamlRoot is not null)
                ViewModel.AttachXamlRoot(XamlRoot);
            if (e.Parameter is string id && !string.IsNullOrWhiteSpace(id))
            {
                versionId = id;
                ViewModel.Load(versionId);
            }

            SyncCategoryListFromTab();
            ApplyTabVisibility(animate: false);
        }
        finally
        {
            _suppressTabAnimation = false;
        }

        // Let the navigate transition / first frame paint before disk Load.
        await Task.Yield();

        if (versionId is not null)
            await ViewModel.CompleteLoadAfterPaintAsync(versionId).ConfigureAwait(true);
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        ViewModel.FlushPendingSave();
        base.OnNavigatingFrom(e);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateBackToInstances();

    private void ModFilterRadio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe)
            return;

        if (int.TryParse(fe.Tag?.ToString(), out var index) && ViewModel.ModFilterIndex != index)
            ViewModel.ModFilterIndex = index;
    }

    private void ModsList_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_modPaintActive || ModsListView is null)
            return;

        if (e.Pointer.PointerDeviceType is not (PointerDeviceType.Mouse
            or PointerDeviceType.Pen
            or PointerDeviceType.Touch))
            return;

        // Left button only for mouse.
        var point = e.GetCurrentPoint(ModsListView);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !point.Properties.IsLeftButtonPressed)
            return;

        if (IsPointerOverButton(e.OriginalSource as DependencyObject))
            return;

        if (!TryGetModIndexAt(point.Position, out var index) || index < 0)
            return;

        var item = ViewModel.ModsList[index];
        _modPaintSelectTo = !item.IsSelected;
        _modPaintAnchor = index;
        _modPaintCurrent = index;
        _modPaintBaseline = ViewModel.ModsList.Select(m => m.IsSelected).ToArray();
        _modPaintActive = true;
        _modPaintPointer = e.Pointer;

        // Freeze the list's own scroller while painting so drag doesn't become a scroll gesture.
        _modsScrollModeBeforePaint = ScrollViewer.GetVerticalScrollMode(ModsListView);
        ScrollViewer.SetVerticalScrollMode(ModsListView, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollMode(ModsListView, ScrollMode.Disabled);

        ApplyModPaintRange();
        ModsListView.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ModsList_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_modPaintActive || ModsListView is null)
            return;

        var localPoint = e.GetCurrentPoint(ModsListView).Position;
        if (!TryGetModIndexAt(localPoint, out var index) || index < 0)
            index = EstimateModIndexFromY(localPoint.Y);

        if (index < 0 || index == _modPaintCurrent)
            return;

        _modPaintCurrent = index;
        ApplyModPaintRange();
    }

    private void ModsList_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        EndModPaint(ModsListView, e.Pointer);

    private void ModsList_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        EndModPaint();

    private void ApplyModPaintRange()
    {
        if (_modPaintBaseline is null || _modPaintAnchor < 0)
            return;

        int lo = Math.Min(_modPaintAnchor, _modPaintCurrent);
        int hi = Math.Max(_modPaintAnchor, _modPaintCurrent);
        var mods = ViewModel.ModsList;

        for (int i = 0; i < mods.Count; i++)
        {
            var m = mods[i];
            bool desired = i >= lo && i <= hi
                ? _modPaintSelectTo
                : (i < _modPaintBaseline.Length && _modPaintBaseline[i]);

            if (m.IsSelected == desired)
                continue;

            m.SuppressSelectionNotify = true;
            m.IsSelected = desired;
            m.SuppressSelectionNotify = false;
        }

        ViewModel.RefreshModSelectionState();
    }

    private bool TryGetModIndexAt(Windows.Foundation.Point listLocalPoint, out int index)
    {
        index = -1;
        if (ModsListView is null)
            return false;

        // FindElementsInHostCoordinates expects XamlRoot / window space, not ListView-local.
        Windows.Foundation.Point rootPoint;
        try
        {
            rootPoint = ModsListView.TransformToVisual(null).TransformPoint(listLocalPoint);
        }
        catch
        {
            rootPoint = listLocalPoint;
        }

        foreach (var el in VisualTreeHelper.FindElementsInHostCoordinates(rootPoint, ModsListView))
        {
            DependencyObject? cur = el;
            while (cur is not null && !ReferenceEquals(cur, ModsListView))
            {
                if (cur is ListViewItem container)
                {
                    index = ModsListView.IndexFromContainer(container);
                    return index >= 0;
                }

                if (cur is FrameworkElement { DataContext: ResourceItem item })
                {
                    index = ViewModel.ModsList.IndexOf(item);
                    return index >= 0;
                }

                cur = VisualTreeHelper.GetParent(cur);
            }
        }

        return false;
    }

    private int EstimateModIndexFromY(double y)
    {
        if (ModsListView is null || ViewModel.ModsList.Count == 0)
            return -1;

        // When pointer is between items / past edges, snap to nearest container by Y.
        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < ViewModel.ModsList.Count; i++)
        {
            if (ModsListView.ContainerFromIndex(i) is not FrameworkElement fe)
                continue;

            var transform = fe.TransformToVisual(ModsListView);
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

        return y < 0 ? 0 : ViewModel.ModsList.Count - 1;
    }

    private void EndModPaint(UIElement? capturer = null, Pointer? pointer = null)
    {
        if (!_modPaintActive && _modPaintPointer is null)
            return;

        if (capturer is not null && pointer is not null)
        {
            try { capturer.ReleasePointerCapture(pointer); }
            catch { /* ignore */ }
        }

        if (ModsListView is not null)
        {
            ScrollViewer.SetVerticalScrollMode(ModsListView, _modsScrollModeBeforePaint);
            ScrollViewer.SetHorizontalScrollMode(ModsListView, ScrollMode.Enabled);
        }

        _modPaintActive = false;
        _modPaintPointer = null;
        _modPaintAnchor = -1;
        _modPaintCurrent = -1;
        _modPaintBaseline = null;
        ViewModel.RefreshModSelectionState();
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

    private void OnInstanceDeleted(object? sender, EventArgs e) => NavigateBackToInstances();

    private void OnNavigateToInstancesRequested(object? sender, EventArgs e) => NavigateBackToInstances();

    private void OnOpenInstanceRequested(object? sender, string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            return;

        Frame.Navigate(
            typeof(InstanceSettingsPage),
            versionId,
            new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            });
    }

    private void NavigateBackToInstances()
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
            if (App.MainWindowInstance is MainWindow main)
                main.HighlightNavTag("instances");
            return;
        }

        if (App.MainWindowInstance is MainWindow mainWindow)
        {
            mainWindow.NavigateToInstances();
            return;
        }

        Frame.Navigate(typeof(InstancesPage), null, new EntranceNavigationTransitionInfo());
    }

    private bool _isSyncingSelection;

    private void CategoryNav_Checked(object sender, RoutedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("InstanceSettingsPage.CategoryNav_Checked");
        if (_isSyncingSelection)
            return;
        if (sender is not RadioButton { IsChecked: true } rb)
            return;

        var tab = rb.Tag switch
        {
            int i => i,
            string s when int.TryParse(s, out var n) => n,
            _ => -1
        };
        if (tab < 0 || ViewModel.SelectedTabIndex == tab)
            return;

        // PropertyChanged drives a single animated ApplyTabVisibility — do not call it twice.
        ViewModel.SelectedTabIndex = tab;
    }

    private void PresetIcon_ItemClick(object sender, ItemClickEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("InstanceSettingsPage.PresetIcon_ItemClick");
        if (e.ClickedItem is string glyph)
        {
            ViewModel.SelectPresetIconCommand.Execute(glyph);
        }
    }

    private async void ModsContainer_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            e.AcceptedOperation = items.Any(i => ResourceDrop.IsAccepted(i, modsOnly: true))
                ? DataPackageOperation.Copy
                : DataPackageOperation.None;
        }
        catch
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void ModsContainer_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var def = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var targetModsFolder = System.IO.Path.Combine(ViewModel.InstanceDirectory, "mods");
            await ResourceDrop.CopyItemsAsync(
                items.ToList(),
                targetModsFolder,
                modsOnly: true,
                ConfirmDialog.ConfirmOverwriteAsync,
                status => ViewModel.StatusText = status);
            ViewModel.LoadResourceLists();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InstanceSettingsPage] DragDrop error: {ex.Message}");
        }
        finally
        {
            def.Complete();
        }
    }
}
