using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CycloneNest2D.UI;

namespace CycloneNest2D.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<SheetPresetRow> _sheetPresetRows = new();
    private bool _updatingZoomPreset;
    private bool _syncingSheetPresetRows;
    private bool _sheetPresetRowsSyncQueued;
    private bool _partColumnsFitQueued;
    private bool _nestingPartColumnsFitQueued;
    private readonly HashSet<PartItem> _observedNestingParts = new();
    private System.Windows.Point _nestingPartDragStart;
    private PartItem? _nestingPartDragCandidate;
    private bool _isDarkTheme;
    private ResourceDictionary? _darkThemeDict;

    public MainWindow(NestSettings settings)
    {
        InitializeComponent();
        LoadThemePreference();
        _vm = new MainViewModel(new WpfDialogService(this), settings);
        DataContext = _vm;
        SheetPresetsGrid.ItemsSource = _sheetPresetRows;
        _vm.Presets.CollectionChanged += (_, _) => ScheduleSheetPresetRowsSync();
        SyncSheetPresetRows();
        var version = typeof(MainWindow).Assembly.GetName().Version;
        AboutVersion.Text = $"Phiên bản {version?.Major}.{version?.Minor}.{version?.Build}";
        _vm.Parts.CollectionChanged += (_, _) =>
        {
            SchedulePartColumnAutoFit();
            SyncNestingPartChangeSubscriptions();
            ScheduleNestingPartColumnAutoFit();
        };
        SyncNestingPartChangeSubscriptions();
        SheetCanvas.ZoomChanged += SheetCanvas_ZoomChanged;
        SheetCanvas.DragOver += SheetCanvas_DragOver;
        SheetCanvas.SelectionChanged += SheetCanvas_SelectionChanged;
        UpdatePartSelectionSummary();
        UpdateZoomReadout();
        MainTabs.SelectionChanged += (s, e) => { if (ReferenceEquals(e.OriginalSource, MainTabs)) SyncRail(); };
        SyncRail();
        UpdateWindowStateVisuals();
    }

    private void SyncRail()
    {
        var rail = new[] { NavFile, NavPart, NavSheets, NavNesting, NavExport, NavSetup, NavInfo };
        int i = MainTabs.SelectedIndex;
        if (i >= 0 && i < rail.Length && rail[i].IsChecked != true) rail[i].IsChecked = true;
    }

    private void SheetCanvas_ZoomChanged(object? sender, EventArgs e) => UpdateZoomReadout();

    private void UpdateZoomReadout()
    {
        double percent = SheetCanvas.ZoomPercent;
        string text = $"{percent:0}%";
        FooterZoom.Text = text;
        _updatingZoomPreset = true;
        var match = ZoomPresetBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            double.TryParse(item.Tag?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out double value) &&
            Math.Abs(value - percent) < 0.5);
        ZoomPresetBox.SelectedItem = match;
        ZoomPresetBox.Text = text;
        _updatingZoomPreset = false;
    }

    private void ZoomPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingZoomPreset || SheetCanvas == null || ZoomPresetBox.SelectedItem is not ComboBoxItem item) return;
        string? tag = item.Tag?.ToString();
        if (tag == "Fit") SheetCanvas.ZoomToFit();
        else if (double.TryParse(tag, NumberStyles.Number, CultureInfo.InvariantCulture, out double percent))
            SheetCanvas.SetZoomPercent(percent);
    }

    private void TabNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: not null } element && int.TryParse(element.Tag.ToString(), out int index))
            MainTabs.SelectedIndex = index;
    }

    private void WindowMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void WindowMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void WindowClose_Click(object sender, RoutedEventArgs e) => Close();

    private static string ThemeFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nesting2D", "theme.txt");

    private void LoadThemePreference()
    {
        bool dark = false;
        try { dark = System.IO.File.Exists(ThemeFile) && System.IO.File.ReadAllText(ThemeFile).Trim() == "dark"; }
        catch { }
        ApplyTheme(dark);
    }

    private void ApplyTheme(bool dark)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        _darkThemeDict ??= new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Themes/DesignSystem.Dark.xaml", UriKind.Absolute)
        };
        if (dark && !merged.Contains(_darkThemeDict)) merged.Add(_darkThemeDict);
        else if (!dark && merged.Contains(_darkThemeDict)) merged.Remove(_darkThemeDict);
        _isDarkTheme = dark;

        // Nút hiện biểu tượng của chế độ sẽ chuyển tới: đang tối -> mặt trời, đang sáng -> mặt trăng.
        if (ThemeIcon != null) ThemeIcon.Data = (Geometry)FindResource(dark ? "IcSun" : "IcMoon");
        if (ThemeButton != null) ThemeButton.ToolTip = dark ? "Chuyển sang giao diện sáng" : "Chuyển sang giao diện tối";

        // Các control tự vẽ (DrawingContext) không theo DynamicResource nên phải vẽ lại.
        SheetCanvas?.InvalidateVisual();
        PartPreview?.InvalidateVisual();
        InvalidateThumbnails(this);
    }

    private static void InvalidateThumbnails(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is SheetThumbnailView t) t.InvalidateVisual();
            else if (c is PartPreviewView p) p.InvalidateVisual();
            InvalidateThumbnails(c);
        }
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_isDarkTheme);
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ThemeFile)!);
            System.IO.File.WriteAllText(ThemeFile, _isDarkTheme ? "dark" : "light");
        }
        catch { }
    }

    private void Window_StateChanged(object? sender, EventArgs e) => UpdateWindowStateVisuals();

    private void UpdateWindowStateVisuals()
    {
        bool max = WindowState == WindowState.Maximized;
        if (MaxGlyph != null) MaxGlyph.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
        if (RestoreGlyph != null) RestoreGlyph.Visibility = max ? Visibility.Visible : Visibility.Collapsed;
        // Cửa sổ không viền khi phóng to bị tràn ra ngoài màn hình một đoạn bằng viền resize.
        if (RootLayout != null)
        {
            var t = SystemParameters.WindowResizeBorderThickness;
            double x = t.Left + SystemParameters.FixedFrameVerticalBorderWidth;
            double y = t.Top + SystemParameters.FixedFrameHorizontalBorderHeight;
            RootLayout.Margin = max ? new Thickness(x, y, x, y) : new Thickness(0);
        }
    }

    private void ProjectMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectMenu == null) return;
        ProjectMenu.DataContext = _vm;
        ProjectMenu.PlacementTarget = ProjectButton;
        ProjectMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ProjectMenu.IsOpen = true;
    }

    private void PartsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePartSelectionSummary();

    private void PartsGrid_Loaded(object sender, RoutedEventArgs e) => SchedulePartColumnAutoFit();

    private void PartsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        SchedulePartColumnAutoFit();
        ScheduleNestingPartColumnAutoFit();
    }

    private void ScheduleNestingPartColumnAutoFit(object? sender = null, RoutedEventArgs? e = null)
    {
        if (_nestingPartColumnsFitQueued) return;
        _nestingPartColumnsFitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _nestingPartColumnsFitQueued = false;
            if (NestingPartGroupsControl?.IsLoaded == true) AutoFitNestingPartColumns();
        }));
    }

    private void AutoFitNestingPartColumns()
    {
        if (NestingPartGroupsControl == null || NestingPartsScrollViewer == null) return;
        NestingPartGroupsControl.UpdateLayout();
        foreach (var grid in FindVisualChildren<Grid>(NestingPartGroupsControl))
        {
            if (grid.ColumnDefinitions.Count != 4) continue;
            grid.ColumnDefinitions[0].Width = GridLength.Auto;
            grid.ColumnDefinitions[1].Width = new GridLength(34);
            grid.ColumnDefinitions[2].Width = GridLength.Auto;
            grid.ColumnDefinitions[3].Width = GridLength.Auto;
        }
        NestingPartGroupsControl.UpdateLayout();
        NestingPartsScrollViewer.ScrollToHorizontalOffset(0);
    }

    private void SyncNestingPartChangeSubscriptions()
    {
        var current = _vm.Parts.ToHashSet();
        foreach (var oldPart in _observedNestingParts.Except(current).ToList())
        {
            oldPart.Changed -= NestingPart_Changed;
            _observedNestingParts.Remove(oldPart);
        }
        foreach (var part in current.Except(_observedNestingParts).ToList())
        {
            part.Changed += NestingPart_Changed;
            _observedNestingParts.Add(part);
        }
    }

    private void NestingPart_Changed(PartItem _)
    {
        SchedulePartColumnAutoFit();
        ScheduleNestingPartColumnAutoFit();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    private void UpdatePartSelectionSummary()
    {
        if (PartSelectionSummary == null || PartsGrid == null) return;
        int total = _vm.Parts.Count;
        int selected = PartsGrid.SelectedItems.Count;
        PartSelectionSummary.Text = selected > 0
            ? $"Đã chọn {selected} / {total} dòng"
            : $"{total} dòng chi tiết";
    }

    private void SchedulePartColumnAutoFit()
    {
        UpdatePartSelectionSummary();
        if (_partColumnsFitQueued) return;
        _partColumnsFitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _partColumnsFitQueued = false;
            if (PartsGrid?.IsLoaded == true) AutoFitPartColumns();
        }));
    }

    private void PointerTool_Click(object sender, RoutedEventArgs e)
    {
        SheetCanvas.ZoomRectMode = false;
        SheetCanvas.PanMode = false;
    }

    private void PanTool_Click(object sender, RoutedEventArgs e) => SheetCanvas.PanMode = true;

    private void ZoomRectangleTool_Click(object sender, RoutedEventArgs e) => SheetCanvas.ZoomRectMode = true;

    private void FitView_Click(object sender, RoutedEventArgs e) => SheetCanvas.ZoomToFit();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SheetCanvas.ZoomIn();

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SheetCanvas.ZoomOut();

    private void PreviousSheet_Click(object sender, RoutedEventArgs e) => SelectAdjacentSheet(-1);

    private void NextSheet_Click(object sender, RoutedEventArgs e) => SelectAdjacentSheet(1);

    private void SelectAdjacentSheet(int direction)
    {
        if (_vm.Sheets.Count == 0) return;
        int current = _vm.SelectedSheet == null ? -1 : _vm.Sheets.IndexOf(_vm.SelectedSheet);
        int next = Math.Clamp(current + direction, 0, _vm.Sheets.Count - 1);
        _vm.SelectedSheet = _vm.Sheets[next];
    }

    public void OpenFromCommandLine(string[] args)
    {
        HandlePaths(args);
    }

    private void HandlePaths(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        var project = list.FirstOrDefault(p => p.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase));
        if (project != null) _vm.OpenProject(project);
        else _vm.AddPaths(list);
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = _vm.IsIdle ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(typeof(PartItem)) && _vm.IsIdle)
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (_vm.IsIdle && e.Data.GetData(typeof(PartItem)) is PartItem part)
        {
            var position = e.GetPosition(SheetCanvas);
            if (position.X >= 0 && position.Y >= 0 && position.X <= SheetCanvas.ActualWidth && position.Y <= SheetCanvas.ActualHeight)
            {
                var world = SheetCanvas.WorldFromControl(position);
                if (_vm.TryPlaceManually(part, world.X, world.Y)) SheetCanvas.Focus();
            }
            e.Handled = true;
            return;
        }
        if (!_vm.IsIdle || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        HandlePaths(files);
        e.Handled = true;
    }

    private void NestingPart_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(list, source) is not ListBoxItem item || item.DataContext is not PartItem part)
        {
            _nestingPartDragCandidate = null;
            return;
        }
        _nestingPartDragCandidate = part;
        _nestingPartDragStart = e.GetPosition(list);
        _vm.SelectedPart = part;
    }

    private void NestingPart_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_nestingPartDragCandidate is not { } part || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || sender is not ListBox list)
            return;
        var current = e.GetPosition(list);
        if (Math.Abs(current.X - _nestingPartDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _nestingPartDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _nestingPartDragCandidate = null;
        var data = new DataObject(typeof(PartItem), part);
        DragDrop.DoDragDrop(list, data, DragDropEffects.Copy);
    }

    private void SheetCanvas_DragOver(object sender, DragEventArgs e)
    {
        bool canPlace = _vm.IsIdle && e.Data.GetDataPresent(typeof(PartItem));
        e.Effects = canPlace ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ClearNesting_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Result == null) return;
        if (ConfirmationDialog.Confirm(this, "Xóa nesting", "Xóa toàn bộ bố trí trên mọi tấm? Danh sách Part sẽ được giữ lại.", "Xóa"))
            _vm.ClearNesting();
    }

    private void SheetCanvas_SelectionChanged(object? sender, EventArgs e)
        => _vm.SetSelectedPlacements(SheetCanvas.SelectedIndices, SheetCanvas.SelectedIndex);

    private void AutoFitPartColumns()
    {
        if (PartsGrid.Columns.Count < 8) return;
        double[] minimum = { 42, 170, 48, 82, 82, 150, 58, 58 };
        double[] maximum = { 58, 360, 74, 118, 118, 280, 88, 82 };
        for (int i = 0; i < PartsGrid.Columns.Count; i++)
        {
            PartsGrid.Columns[i].MinWidth = minimum[i];
            PartsGrid.Columns[i].MaxWidth = maximum[i];
            PartsGrid.Columns[i].Width = DataGridLength.Auto;
        }
        PartsGrid.UpdateLayout();
        for (int i = 0; i < PartsGrid.Columns.Count; i++)
        {
            double measured = PartsGrid.Columns[i].ActualWidth;
            PartsGrid.Columns[i].Width = new DataGridLength(Math.Clamp(measured, minimum[i], maximum[i]));
        }
    }

    private void PartPreviewFitZoom_Click(object sender, RoutedEventArgs e) => PartPreview.ZoomToFit();

    private void SelectAllParts_Click(object sender, RoutedEventArgs e) => PartsGrid.SelectAll();

    private void ClearPartSelection_Click(object sender, RoutedEventArgs e) => PartsGrid.UnselectAll();

    private void PartsGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(PartsGrid, source) is not DataGridRow row) return;
        if (row.Item is not PartItem part) return;
        // Nhấp phải vào dòng ngoài selection: chuyển selection sang dòng đó.
        // Nhấp phải vào dòng đã chọn: giữ nguyên nhóm nhiều dòng đang chọn.
        if (!PartsGrid.SelectedItems.Contains(part))
        {
            PartsGrid.UnselectAll();
            row.IsSelected = true;
            PartsGrid.SelectedItem = part;
        }
    }

    private void PartsContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        bool hasSelection = PartsGrid.SelectedItems.Count > 0;
        if (sender is ContextMenu menu)
            foreach (var item in menu.Items.OfType<MenuItem>().Where(i => Equals(i.Tag, "requires-selection")))
                item.IsEnabled = hasSelection;
    }

    private void PartsGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Delete || PartsGrid.SelectedItems.Count == 0) return;
        DeleteSelectedParts_Click(sender, e);
        e.Handled = true;
    }

    private void DeleteSelectedParts_Click(object sender, RoutedEventArgs e)
    {
        PartsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PartsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = PartsGrid.SelectedItems.Cast<PartItem>().ToList();
        int removed = _vm.RemoveParts(selected);
        PartsGrid.UnselectAll();
        if (removed > 0) _vm.Toasts.Show(ToastKind.Success, "Đã xóa chi tiết", $"Đã xóa {removed} dòng Part.");
    }

    private void DeleteAllParts_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearCommand.Execute(null);
        if (_vm.Parts.Count == 0) PartsGrid.UnselectAll();
    }

    private void EditSelectedPartProperties_Click(object sender, RoutedEventArgs e)
    {
        PartsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PartsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = PartsGrid.SelectedItems.Cast<PartItem>().ToList();
        if (selected.Count == 0) return;
        bool single = selected.Count == 1;
        var dialog = new Window
        {
            Title = single ? $"Thuộc tính — {selected[0].Name}" : $"Thuộc tính — {selected.Count} dòng Part",
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
            Width = 500, Background = System.Windows.Media.Brushes.White
        };
        var panel = new StackPanel { Margin = new Thickness(20), MinWidth = 450 };
        panel.Children.Add(new TextBlock
        {
            Text = single ? "Chỉnh sửa thông tin chi tiết." : $"Đánh dấu thuộc tính muốn áp dụng cho cả {selected.Count} dòng đang chọn. Các thuộc tính không đánh dấu sẽ được giữ nguyên.",
            TextWrapping = TextWrapping.Wrap, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 101, 122)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        TextBox? nameBox = null;
        if (single)
        {
            nameBox = AddInput(panel, "Tên chi tiết", selected[0].Name);
        }

        var commonPreset = selected.Select(p => p.AssignedPreset ?? _vm.Presets.FirstOrDefault(s =>
            string.Equals(s.Material.Trim(), p.Material.Trim(), StringComparison.OrdinalIgnoreCase) && Math.Round(s.Thickness, 4) == Math.Round(p.Thickness, 4)))
            .Distinct().ToList();
        SheetPreset? initialPreset = commonPreset.Count == 1 ? commonPreset[0] : null;
        var presetRow = new Grid { Margin = new Thickness(0, 5, 0, 7) };
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var applyPreset = new CheckBox { Content = "Vật liệu · Độ dày", IsChecked = single && initialPreset != null, VerticalAlignment = VerticalAlignment.Center };
        var presetBox = new ComboBox { ItemsSource = _vm.Presets, DisplayMemberPath = "AssignmentLabel", SelectedItem = initialPreset, Padding = new Thickness(6, 4, 6, 4), IsEnabled = single && initialPreset != null };
        Grid.SetColumn(applyPreset, 0); Grid.SetColumn(presetBox, 1);
        presetRow.Children.Add(applyPreset); presetRow.Children.Add(presetBox);
        applyPreset.Checked += (_, _) => presetBox.IsEnabled = true;
        applyPreset.Unchecked += (_, _) => presetBox.IsEnabled = false;
        panel.Children.Add(presetRow);

        var initialQuantity = single ? selected[0].Quantity.ToString(CultureInfo.CurrentCulture) : "1";
        var qtyRow = new Grid { Margin = new Thickness(0, 5, 0, 7) };
        qtyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        qtyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var applyQuantity = new CheckBox { Content = "Số lượng", IsChecked = single, VerticalAlignment = VerticalAlignment.Center };
        var quantityBox = new TextBox { Text = initialQuantity, Padding = new Thickness(6, 4, 6, 4), IsEnabled = single };
        Grid.SetColumn(applyQuantity, 0); Grid.SetColumn(quantityBox, 1);
        qtyRow.Children.Add(applyQuantity); qtyRow.Children.Add(quantityBox);
        applyQuantity.Checked += (_, _) => quantityBox.IsEnabled = true;
        applyQuantity.Unchecked += (_, _) => quantityBox.IsEnabled = false;
        panel.Children.Add(qtyRow);

        var initialCanRotate = single ? selected[0].CanRotate : true;
        var rotateRow = BuildBooleanPropertyRow("Cho phép xoay", "Bật", initialCanRotate, single, out var applyRotate, out var rotateValue);
        panel.Children.Add(rotateRow);
        var initialFlip = single && selected[0].Flip;
        var flipRow = BuildBooleanPropertyRow("Lật hình", "Bật", initialFlip, single, out var applyFlip, out var flipValue);
        panel.Children.Add(flipRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(new Button { Content = "Hủy", IsCancel = true, MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) });
        var save = new Button { Content = single ? "Lưu" : $"Áp dụng cho {selected.Count} dòng", IsDefault = true, MinWidth = 130 };
        save.Click += (_, _) =>
        {
            var presetToApply = presetBox.SelectedItem as SheetPreset;
            if (applyPreset.IsChecked == true && presetToApply == null)
            {
                MessageBox.Show(dialog, "Hãy chọn vật liệu/độ dày từ danh sách khổ tấm.", "Chưa chọn cấu hình", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (applyQuantity.IsChecked == true && (!int.TryParse(quantityBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int parsedQuantity) || parsedQuantity < 0))
            {
                MessageBox.Show(dialog, "Số lượng phải là số nguyên từ 0 trở lên.", "Số lượng chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (single && string.IsNullOrWhiteSpace(nameBox?.Text))
            {
                MessageBox.Show(dialog, "Tên chi tiết không được để trống.", "Tên chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (single && nameBox != null)
                selected[0].Name = _vm.UniqueName(nameBox.Text.Trim(), selected[0]);
            if (applyPreset.IsChecked == true)
                _vm.AssignPresetToParts(selected, presetToApply!);
            if (applyQuantity.IsChecked == true)
            {
                int quantityValue = int.Parse(quantityBox.Text, CultureInfo.CurrentCulture);
                foreach (var part in selected) part.Quantity = quantityValue;
            }
            if (applyRotate.IsChecked == true)
                foreach (var part in selected) part.CanRotate = rotateValue.IsChecked == true;
            if (applyFlip.IsChecked == true)
                foreach (var part in selected) part.Flip = flipValue.IsChecked == true;
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        if (dialog.ShowDialog() == true)
        {
            _vm.Toasts.Show(ToastKind.Success, "Đã cập nhật thuộc tính", single ? "Đã lưu thay đổi cho chi tiết." : $"Đã áp dụng thay đổi cho {selected.Count} dòng.");
        }
    }

    private static Grid BuildBooleanPropertyRow(string label, string valueLabel, bool initialValue, bool applyByDefault, out CheckBox apply, out CheckBox value)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        apply = new CheckBox { Content = "Áp dụng", IsChecked = applyByDefault, VerticalAlignment = VerticalAlignment.Center };
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 101, 122)) };
        var valueControl = new CheckBox { Content = valueLabel, IsChecked = initialValue, IsEnabled = applyByDefault, VerticalAlignment = VerticalAlignment.Center };
        value = valueControl;
        Grid.SetColumn(apply, 0); Grid.SetColumn(caption, 1); Grid.SetColumn(valueControl, 2);
        row.Children.Add(apply); row.Children.Add(caption); row.Children.Add(valueControl);
        apply.Checked += (_, _) => valueControl.IsEnabled = true;
        apply.Unchecked += (_, _) => valueControl.IsEnabled = false;
        return row;
    }

    private static TextBox AddInput(Panel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2), Foreground = new SolidColorBrush(Color.FromRgb(82, 101, 122)) });
        var box = new TextBox { Text = value, Padding = new Thickness(7, 5, 7, 5), MinWidth = 320 };
        panel.Children.Add(box);
        return box;
    }

    private void ScheduleSheetPresetRowsSync()
    {
        if (_sheetPresetRowsSyncQueued) return;
        _sheetPresetRowsSyncQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _sheetPresetRowsSyncQueued = false;
            SyncSheetPresetRows();
        }));
    }

    private void SyncSheetPresetRows()
    {
        if (SheetPresetsGrid == null) return;
        var pendingRows = _sheetPresetRows.Where(row => row.IsNew).ToList();
        var selectedPendingRow = pendingRows.FirstOrDefault(row => SheetPresetsGrid.SelectedItems.Contains(row));
        _syncingSheetPresetRows = true;
        _sheetPresetRows.Clear();
        for (int i = 0; i < _vm.Presets.Count; i++)
            _sheetPresetRows.Add(new SheetPresetRow(_vm.Presets[i], i + 1));
        foreach (var row in pendingRows)
        {
            row.Sequence = _sheetPresetRows.Count + 1;
            _sheetPresetRows.Add(row);
        }

        var selected = selectedPendingRow ?? _sheetPresetRows.FirstOrDefault(row => ReferenceEquals(row.Preset, _vm.SelectedPreset));
        if (selected != null) SheetPresetsGrid.SelectedItems.Add(selected);
        _syncingSheetPresetRows = false;
    }

    private void SheetPresetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingSheetPresetRows && SheetPresetsGrid.SelectedItem is SheetPresetRow { Preset: { } preset })
            _vm.UseSheetPreset(preset);
    }

    private void SheetPresetsGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(SheetPresetsGrid, source) is not DataGridRow row ||
            row.Item is not SheetPresetRow presetRow)
            return;

        if (!SheetPresetsGrid.SelectedItems.Contains(presetRow))
        {
            SheetPresetsGrid.UnselectAll();
            row.IsSelected = true;
            SheetPresetsGrid.SelectedItem = presetRow;
        }
    }

    private void SheetPresetsContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        bool hasSelection = SheetPresetsGrid.SelectedItems.Count > 0;
        foreach (var item in menu.Items.OfType<MenuItem>().Where(item => Equals(item.Tag, "requires-selection")))
            item.IsEnabled = hasSelection;
    }

    private void SelectAllSheetPresets_Click(object sender, RoutedEventArgs e) => SheetPresetsGrid.SelectAll();

    private void ClearSheetPresetSelection_Click(object sender, RoutedEventArgs e) => SheetPresetsGrid.UnselectAll();

    private void FocusSheetPresetRow(SheetPresetRow row)
    {
        SheetPresetsGrid.SelectedItem = row;
        SheetPresetsGrid.ScrollIntoView(row);
        SheetPresetsGrid.CurrentCell = new DataGridCellInfo(row, SheetPresetsGrid.Columns[1]);
        SheetPresetsGrid.BeginEdit();
    }

    private void AddSheetPresetRow_Click(object sender, RoutedEventArgs e)
    {
        if (!SheetPresetsGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !SheetPresetsGrid.CommitEdit(DataGridEditingUnit.Row, true))
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var pending = _sheetPresetRows.FirstOrDefault(row => row.IsNew);
            if (pending != null)
            {
                FocusSheetPresetRow(pending);
                return;
            }

            var row = new SheetPresetRow(_sheetPresetRows.Count + 1);
            _sheetPresetRows.Add(row);
            SheetPresetsGrid.SelectedItems.Clear();
            FocusSheetPresetRow(row);
        }));
    }

    private void SheetPresetsGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (_syncingSheetPresetRows || e.Row.Item is not SheetPresetRow row) return;
        if (e.EditAction == DataGridEditAction.Cancel)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (row.IsNew) _sheetPresetRows.Remove(row);
                else SyncSheetPresetRows();
            }));
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => CommitSheetPresetRow(row)));
    }

    private void CommitSheetPresetRow(SheetPresetRow row)
    {
        if (!_sheetPresetRows.Contains(row)) return;
        string material = row.Material.Trim();
        if (string.IsNullOrWhiteSpace(material) ||
            string.Equals(material, "Chưa phân loại", StringComparison.OrdinalIgnoreCase) ||
            !(row.Width > 0) || !double.IsFinite(row.Width) ||
            !(row.Height > 0) || !double.IsFinite(row.Height) ||
            !(row.Thickness > 0) || !double.IsFinite(row.Thickness))
        {
            MessageBox.Show(this, "Nhập vật liệu cụ thể, chiều dài/rộng và độ dày lớn hơn 0 hợp lệ.", "Thông tin tấm chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (!row.IsNew) SyncSheetPresetRows();
            else FocusSheetPresetRow(row);
            return;
        }

        if (row.Preset is { } original &&
            string.Equals(original.Material, material, StringComparison.Ordinal) &&
            original.Width.Equals(row.Width) &&
            original.Height.Equals(row.Height) &&
            original.Thickness.Equals(row.Thickness) &&
            string.Equals(original.Note, row.Note.Trim(), StringComparison.Ordinal))
            return;

        string profileName = $"{material} · {row.Thickness:0.##} mm · {row.Width:0.##} × {row.Height:0.##} mm";
        var edited = new SheetPreset(profileName, row.Width, row.Height, material, row.Thickness)
        {
            Note = row.Note.Trim()
        };
        bool duplicate = _vm.Presets.Any(preset =>
            !ReferenceEquals(preset, row.Preset) &&
            string.Equals(preset.Material.Trim(), material, StringComparison.OrdinalIgnoreCase) &&
            Math.Round(preset.Thickness, 4) == Math.Round(row.Thickness, 4) &&
            Math.Round(preset.Width, 3) == Math.Round(row.Width, 3) &&
            Math.Round(preset.Height, 3) == Math.Round(row.Height, 3));
        if (duplicate)
        {
            MessageBox.Show(this, "Khổ tấm có cùng vật liệu, độ dày và kích thước đã tồn tại.", "Khổ tấm trùng", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (!row.IsNew) SyncSheetPresetRows();
            else FocusSheetPresetRow(row);
            return;
        }

        if (row.Preset is { } current)
        {
            _vm.EditSheetPreset(current, edited);
            row.Accept(edited);
        }
        else
        {
            _vm.AddSheetPreset(edited);
            row.Accept(edited);
        }
    }

    private void DeleteSelectedSheetPresets_Click(object sender, RoutedEventArgs e)
    {
        if (!SheetPresetsGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !SheetPresetsGrid.CommitEdit(DataGridEditingUnit.Row, true))
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(DeleteSelectedSheetPresetRows));
    }

    private void DeleteSelectedSheetPresetRows()
    {
        var selected = SheetPresetsGrid.SelectedItems.Cast<SheetPresetRow>().ToList();
        var presets = selected.Where(row => row.Preset != null).Select(row => row.Preset!).ToList();
        if (presets.Count > 0 &&
            !ConfirmationDialog.Confirm(this, "Xóa khổ tấm", $"Xóa {presets.Count} khổ tấm đã chọn?", "Xóa"))
            return;

        foreach (var preset in presets) _vm.DeleteSheetPreset(preset);
        foreach (var row in selected.Where(row => row.IsNew)) _sheetPresetRows.Remove(row);
        ScheduleSheetPresetRowsSync();
    }

    private void ExportDxf_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsIdle) return;
        if (_vm.Result == null || _vm.Result.Sheets.Count == 0)
        {
            MessageBox.Show(this, "Hãy chạy nesting trước khi xuất DXF.", "Chưa có kết quả", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new DxfExportDialog(_vm.Result.Sheets) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Options is { } options)
            _vm.ExportDxf(options);
    }

    private void PreviewReport_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Result == null || _vm.Result.Sheets.Count == 0)
        {
            MessageBox.Show(this, "Hãy chạy nesting trước khi xem báo cáo.", "Chưa có kết quả", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var report = NestingReportBuilder.Build(_vm);
            var preview = new ReportPreviewWindow(report) { Owner = this };
            preview.ShowDialog();
            if (!string.IsNullOrWhiteSpace(preview.ExportedPath))
                _vm.Toasts.Show(ToastKind.Success, "Đã xuất PDF", System.IO.Path.GetFileName(preview.ExportedPath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không thể tạo bản xem trước: " + ex.Message, "Lỗi báo cáo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_vm.IsProjectDirty &&
            !ConfirmationDialog.Confirm(this, "Thoát Cyclone Nest2D", "Dự án có thay đổi chưa được lưu. Bạn có muốn thoát và bỏ các thay đổi này không?", "Thoát"))
        {
            e.Cancel = true;
            return;
        }
        if (_vm.IsBusy) _vm.CancelCommand.Execute(null);
        _vm.Settings.SaveTo(NestSettings.DefaultFilePath);
    }

    private sealed class SheetPresetRow : INotifyPropertyChanged
    {
        private int _sequence;
        private string _material;
        private double _width;
        private double _height;
        private double _thickness;
        private string _note;

        public SheetPresetRow(SheetPreset preset, int sequence)
        {
            Preset = preset;
            _sequence = sequence;
            _material = preset.Material;
            _width = preset.Width;
            _height = preset.Height;
            _thickness = preset.Thickness;
            _note = preset.Note;
        }

        public SheetPresetRow(int sequence)
        {
            _sequence = sequence;
            _material = string.Empty;
            _note = string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public SheetPreset? Preset { get; private set; }
        public bool IsNew => Preset == null;

        public int Sequence
        {
            get => _sequence;
            set => Set(ref _sequence, value);
        }

        public string Material
        {
            get => _material;
            set => Set(ref _material, value);
        }

        public double Width
        {
            get => _width;
            set => Set(ref _width, value);
        }

        public double Height
        {
            get => _height;
            set => Set(ref _height, value);
        }

        public double Thickness
        {
            get => _thickness;
            set => Set(ref _thickness, value);
        }

        public string Note
        {
            get => _note;
            set => Set(ref _note, value);
        }

        public void Accept(SheetPreset preset)
        {
            Preset = preset;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preset)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNew)));
        }

        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
