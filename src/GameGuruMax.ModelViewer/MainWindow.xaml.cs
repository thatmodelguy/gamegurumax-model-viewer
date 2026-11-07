using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GameGuruMax.ModelViewer.Core;
using GameGuruMax.ModelViewer.Core.Fpe;
using GameGuruMax.ModelViewer.Dialogs;
using GameGuruMax.ModelViewer.Models;
using GameGuruMax.ModelViewer.Services;
using Microsoft.Win32;

namespace GameGuruMax.ModelViewer;

public partial class MainWindow : Window
{
    private AppPaths _paths = SettingsStore.LoadPaths();
    private List<EntityRecord> _allEntities = new();
    private SharedReferenceIndex _refIndex = new();
    private FolderNode _folderRoot = new() { Name = "entitybank", RelativePath = "" };
    private readonly ObservableCollection<EntityListItem> _visibleEntities = new();
    private readonly HashSet<string> _checkedFpePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<FpeMapField> _mapFields = new();
    private readonly ObservableCollection<FpeMapField> _animFields = new();
    private ICollectionView? _view;
    private string? _selectedFolderRelative;
    private EntityRecord? _selected;
    private FpeDocument? _currentDoc;
    private CancellationTokenSource? _scanCts;
    private bool _suppressSelectAllSync;

    public MainWindow()
    {
        InitializeComponent();
        EntityGrid.ItemsSource = _visibleEntities;
        MapFieldsList.ItemsSource = _mapFields;
        AnimSlotsList.ItemsSource = _animFields;
        _view = CollectionViewSource.GetDefaultView(_visibleEntities);
        FieldCollisionMode.ItemsSource = FpeFieldOptions.CollisionModes;
        FieldCollisionMode.DisplayMemberPath = nameof(NamedIntOption.Label);
        FieldDefaultStatic.ItemsSource = FpeFieldOptions.DefaultStaticModes;
        FieldDefaultStatic.DisplayMemberPath = nameof(NamedIntOption.Label);
        PreviewImage.Source = ImageLoader.CreatePlaceholder();
        Loaded += async (_, _) => await RescanAsync();
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await RescanAsync();

    private async Task RescanAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        // Remember where the user was so delete/optimize/move don't kick them to root
        var folderToRestore = _selectedFolderRelative ?? "";
        var entityPathToRestore = _selected?.FpePath;

        StatusText.Text = $"Scanning {_paths.EntityBankRoot}...";
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanProgress.IsIndeterminate = false;
                ScanProgress.Maximum = Math.Max(1, p.Total);
                ScanProgress.Value = p.Processed;
                StatusText.Text = $"Scanning {p.Processed}/{p.Total}...";
            });

            var result = await EntityBankScanner.ScanAsync(_paths, progress, token);
            if (token.IsCancellationRequested)
                return;

            _allEntities = result.Entities;
            _refIndex = result.ReferenceIndex;
            _folderRoot = BuildFolderTree(result.FolderPaths);
            RebuildFolderTreeUi(folderToRestore);
            ApplyFilter();
            RestoreEntitySelection(entityPathToRestore);
            StatusText.Text = $"{_allEntities.Count} entities — {_paths.EntityBankRoot}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Scan failed.";
            MessageBox.Show(this, ex.Message, "Scan", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
        }
    }

    private static FolderNode BuildFolderTree(IEnumerable<string> folderPaths)
    {
        var root = new FolderNode { Name = "entitybank", RelativePath = "" };
        var map = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = root
        };

        foreach (var path in folderPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(path))
                continue;
            var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var acc = "";
            FolderNode parent = root;
            foreach (var part in parts)
            {
                acc = string.IsNullOrEmpty(acc) ? part : acc + "\\" + part;
                if (!map.TryGetValue(acc, out var node))
                {
                    node = new FolderNode { Name = part, RelativePath = acc };
                    map[acc] = node;
                    parent.Children.Add(node);
                }
                parent = node;
            }
        }

        return root;
    }

    private void RebuildFolderTreeUi(string? selectRelativePath = null)
    {
        FolderTree.Items.Clear();
        FolderTree.Items.Add(CreateTreeItem(_folderRoot));
        if (FolderTree.Items[0] is not TreeViewItem rootItem)
            return;

        rootItem.IsExpanded = true;

        var target = selectRelativePath ?? "";
        if (string.IsNullOrEmpty(target))
        {
            rootItem.IsSelected = true;
            _selectedFolderRelative = "";
            return;
        }

        if (!TrySelectFolderPath(rootItem, target))
        {
            // Folder may have been removed (e.g. last model deleted) — fall back to nearest parent
            var parent = Path.GetDirectoryName(target)?.Replace('/', '\\') ?? "";
            while (!string.IsNullOrEmpty(parent) && !TrySelectFolderPath(rootItem, parent))
                parent = Path.GetDirectoryName(parent)?.Replace('/', '\\') ?? "";

            if (string.IsNullOrEmpty(parent) || FolderTree.SelectedItem is null)
            {
                rootItem.IsSelected = true;
                _selectedFolderRelative = "";
            }
        }
    }

    private bool TrySelectFolderPath(TreeViewItem rootItem, string relativePath)
    {
        var parts = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        TreeViewItem current = rootItem;
        foreach (var part in parts)
        {
            current.IsExpanded = true;
            TreeViewItem? next = null;
            foreach (TreeViewItem child in current.Items)
            {
                if (child.Tag is FolderNode node
                    && node.Name.Equals(part, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }
            if (next is null)
                return false;
            current = next;
        }

        current.IsExpanded = true;
        current.IsSelected = true;
        current.BringIntoView();
        if (current.Tag is FolderNode selected)
            _selectedFolderRelative = selected.RelativePath;
        return true;
    }

    private void RestoreEntitySelection(string? fpePath)
    {
        if (string.IsNullOrWhiteSpace(fpePath))
            return;

        var match = _visibleEntities.FirstOrDefault(e =>
            e.FpePath.Equals(fpePath, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return;

        EntityGrid.SelectedItem = match;
        EntityGrid.ScrollIntoView(match);
    }

    private List<EntityRecord> GetCheckedEntities()
        => _visibleEntities.Where(i => i.IsChecked).Select(i => i.Entity).ToList();

    private List<EntityRecord> GetActionTargets()
    {
        var checkedEntities = GetCheckedEntities();
        if (checkedEntities.Count > 0)
            return checkedEntities;
        if (_selected is not null)
            return [_selected];
        return [];
    }

    private void SelectAllCheck_Click(object sender, RoutedEventArgs e)
    {
        var check = SelectAllCheck.IsChecked == true;
        _suppressSelectAllSync = true;
        try
        {
            foreach (var item in _visibleEntities)
                item.IsChecked = check;
        }
        finally
        {
            _suppressSelectAllSync = false;
        }
        UpdateCheckedUi();
    }

    private void OnEntityCheckedChanged(object? sender, EventArgs e)
    {
        if (sender is EntityListItem item)
        {
            if (item.IsChecked)
                _checkedFpePaths.Add(item.FpePath);
            else
                _checkedFpePaths.Remove(item.FpePath);
        }

        if (!_suppressSelectAllSync)
            UpdateCheckedUi();
    }

    private void UpdateCheckedUi()
    {
        var checkedCount = _visibleEntities.Count(i => i.IsChecked);
        var visibleCount = _visibleEntities.Count;

        if (checkedCount > 0)
        {
            CheckedCountText.Text = $"{checkedCount} checked";
            CheckedCountText.Visibility = Visibility.Visible;
            MoveButton.Content = $"Move ({checkedCount})";
            DeleteButton.Content = $"Delete ({checkedCount})";
            OptimizeButton.Content = $"Optimize ({checkedCount})";
        }
        else
        {
            CheckedCountText.Visibility = Visibility.Collapsed;
            MoveButton.Content = "Move";
            DeleteButton.Content = "Delete";
            OptimizeButton.Content = "Optimize";
        }

        _suppressSelectAllSync = true;
        try
        {
            if (visibleCount == 0)
                SelectAllCheck.IsChecked = false;
            else if (checkedCount == 0)
                SelectAllCheck.IsChecked = false;
            else if (checkedCount == visibleCount)
                SelectAllCheck.IsChecked = true;
            else
                SelectAllCheck.IsChecked = null; // indeterminate
        }
        finally
        {
            _suppressSelectAllSync = false;
        }
    }

    private static TreeViewItem CreateTreeItem(FolderNode node)
    {
        var item = new TreeViewItem
        {
            Header = string.IsNullOrEmpty(node.RelativePath) ? "entitybank" : node.Name,
            Tag = node
        };
        foreach (var child in node.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            item.Items.Add(CreateTreeItem(child));
        return item;
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (FolderTree.SelectedItem is TreeViewItem { Tag: FolderNode node })
        {
            _selectedFolderRelative = node.RelativePath;
            ApplyFilter();
        }
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterBox.Text.Trim();
        var selectedPath = _selected?.FpePath;

        foreach (var item in _visibleEntities)
            item.CheckedChanged -= OnEntityCheckedChanged;
        _visibleEntities.Clear();

        IEnumerable<EntityRecord> query = _allEntities;
        if (!string.IsNullOrEmpty(_selectedFolderRelative))
        {
            var prefix = _selectedFolderRelative;
            query = query.Where(e =>
                e.FolderRelative.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || e.FolderRelative.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(filter))
        {
            query = query.Where(e =>
                e.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.FolderRelative.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.Stem.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        // Drop checks for entities that no longer exist after a rescan
        var livePaths = new HashSet<string>(
            _allEntities.Select(e => e.FpePath),
            StringComparer.OrdinalIgnoreCase);
        _checkedFpePaths.RemoveWhere(p => !livePaths.Contains(p));

        foreach (var e in query.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var item = new EntityListItem(e, _checkedFpePaths.Contains(e.FpePath));
            item.CheckedChanged += OnEntityCheckedChanged;
            _visibleEntities.Add(item);
        }

        EntityCountText.Text = $"Total: {_visibleEntities.Count}";
        UpdateCheckedUi();

        if (!string.IsNullOrWhiteSpace(selectedPath))
            RestoreEntitySelection(selectedPath);
    }

    private void EntityGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = (EntityGrid.SelectedItem as EntityListItem)?.Entity;
        LoadSelectedEntity();
    }

    private void LoadSelectedEntity()
    {
        AssociatedList.Items.Clear();
        _currentDoc = null;

        if (_selected is null)
        {
            EntityTitle.Text = "";
            PreviewPathText.Text = "";
            PreviewImage.Source = ImageLoader.CreatePlaceholder();
            ClearFpeFields();
            RawFpeBox.Text = "";
            return;
        }

        EntityTitle.Text = _selected.DisplayName;
        foreach (var f in _selected.AssociatedFiles)
            AssociatedList.Items.Add(Path.GetFileName(f));

        var preview = _selected.PreviewPath;
        PreviewPathText.Text = preview is null
            ? "(no preview found)"
            : "Preview: " + preview;
        PreviewImage.Source = ImageLoader.Load(preview) ?? ImageLoader.CreatePlaceholder();

        try
        {
            _currentDoc = FpeParser.ParseFile(_selected.FpePath);
            FieldDesc.Text = _currentDoc.GetValue("desc") ?? "";
            FieldModel.Text = _currentDoc.GetValue("model") ?? "";
            FieldScale.Text = _currentDoc.GetValue("scale") ?? "";
            SelectIntOption(FieldCollisionMode, FpeFieldOptions.CollisionModes, _currentDoc.GetValue("collisionmode"));
            SelectIntOption(FieldDefaultStatic, FpeFieldOptions.DefaultStaticModes, _currentDoc.GetValue("defaultstatic"));
            FieldIsCharacter.Text = _currentDoc.GetValue("ischaracter") ?? "";
            FieldAnimSpeed.Text = _currentDoc.GetValue("animspeed") ?? "";
            FieldAnimMax.Text = _currentDoc.GetValue("animmax") ?? "";
            FieldPlayAnimInEditor.Text = _currentDoc.GetValue("playanimineditor") ?? "";
            LoadAnimFields(_currentDoc);
            LoadMapFields(_currentDoc);
            RawFpeBox.Text = _currentDoc.ToFpeText();
        }
        catch (Exception ex)
        {
            ClearFpeFields();
            RawFpeBox.Text = "";
            StatusText.Text = "Failed to load FPE: " + ex.Message;
        }
    }

    private static void SelectIntOption(ComboBox box, IReadOnlyList<NamedIntOption> options, string? raw)
    {
        if (int.TryParse((raw ?? "").Trim(), out var value))
        {
            var match = options.FirstOrDefault(o => o.Value == value);
            if (match is not null)
            {
                box.SelectedItem = match;
                return;
            }
            box.SelectedItem = null;
            box.Text = value.ToString();
            return;
        }

        box.SelectedItem = null;
        box.Text = raw?.Trim() ?? "";
    }

    private static string GetIntOptionValue(ComboBox box)
    {
        if (box.SelectedItem is NamedIntOption opt)
            return opt.Value.ToString();
        var text = box.Text?.Trim() ?? "";
        // Allow typing "0 — Box" or just "0"
        var dash = text.IndexOf('—');
        if (dash < 0)
            dash = text.IndexOf('-');
        var head = dash > 0 ? text[..dash].Trim() : text;
        if (int.TryParse(head, out var n))
            return n.ToString();
        return text;
    }

    private void ClearFpeFields()
    {
        FieldDesc.Text = "";
        FieldModel.Text = "";
        FieldScale.Text = "";
        FieldCollisionMode.SelectedItem = null;
        FieldCollisionMode.Text = "";
        FieldDefaultStatic.SelectedItem = null;
        FieldDefaultStatic.Text = "";
        FieldIsCharacter.Text = "";
        FieldAnimSpeed.Text = "";
        FieldAnimMax.Text = "";
        FieldPlayAnimInEditor.Text = "";
        _animFields.Clear();
        _mapFields.Clear();
        MapFieldsEmptyHint.Visibility = Visibility.Collapsed;
    }

    private void LoadAnimFields(FpeDocument doc)
    {
        _animFields.Clear();
        foreach (var (key, value) in doc.GetAnimSlots()
                     .OrderBy(e => int.TryParse(e.Key.AsSpan(4), out var n) ? n : int.MaxValue))
        {
            _animFields.Add(new FpeMapField
            {
                Key = key,
                Value = value
            });
        }
    }

    private void LoadMapFields(FpeDocument doc)
    {
        _mapFields.Clear();
        var maps = FpeMapKeys.GetMapEntries(doc)
            .OrderBy(e => FpeMapKeys.SortKey(e.Key))
            .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var (key, value) in maps)
        {
            _mapFields.Add(new FpeMapField
            {
                Key = key,
                Value = value
            });
        }

        MapFieldsEmptyHint.Visibility = _mapFields.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddAnimSlot_Click(object sender, RoutedEventArgs e)
    {
        var next = 0;
        foreach (var slot in _animFields)
        {
            if (int.TryParse(slot.Key.AsSpan(4), out var n) && n >= next)
                next = n + 1;
        }

        _animFields.Add(new FpeMapField
        {
            Key = "anim" + next,
            Value = "0,0"
        });

        SyncAnimMaxFromSlots();
        StatusText.Text = $"Added anim{next} — remember to Save FPE";
    }

    private void RemoveAnimSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FpeMapField slot })
            return;

        _animFields.Remove(slot);
        SyncAnimMaxFromSlots();
        StatusText.Text = $"Removed {slot.Key} — remember to Save FPE";
    }

    private void SyncAnimMaxFromSlots()
    {
        FieldAnimMax.Text = _animFields.Count.ToString();
    }

    private void SaveFpe_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        try
        {
            FpeDocument doc;
            // Prefer raw tab if it looks like the source of truth when user edited it
            // Sync form fields into document then allow raw override if raw differs from last parse
            doc = FpeParser.Parse(RawFpeBox.Text);
            doc.SourcePath = _selected.FpePath;

            // Apply form fields on top (form is authoritative for known keys)
            ApplyFormToDoc(doc);

            var model = doc.GetValue("model")?.Trim() ?? "";
            if (!string.IsNullOrEmpty(model))
            {
                var modelPath = Path.Combine(Path.GetDirectoryName(_selected.FpePath)!, Path.GetFileName(model));
                if (!File.Exists(modelPath))
                {
                    var warn = MessageBox.Show(
                        this,
                        $"Model file not found:\n{modelPath}\n\nSave anyway?",
                        "Save FPE",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (warn != MessageBoxResult.Yes)
                        return;
                }
            }

            doc.Save(_selected.FpePath);
            RawFpeBox.Text = doc.ToFpeText();
            _currentDoc = doc;

            _selected.Desc = doc.GetValue("desc")?.Trim() ?? "";
            _selected.Model = doc.GetValue("model")?.Trim() ?? "";
            _selected.AssociatedFiles = AssociatedFilesResolver.Resolve(doc);
            _selected.PreviewPath = ThumbnailResolver.Resolve(_paths, _selected.FpePath, doc);

            // Refresh reference index entry
            _refIndex.RemoveEntity(_selected.FpePath);
            _refIndex.AddEntity(_selected.FpePath, _selected.AssociatedFiles);

            LoadSelectedEntity();
            EntityGrid.Items.Refresh();
            StatusText.Text = "Saved " + _selected.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save FPE", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFormToDoc(FpeDocument doc)
    {
        SetIfPresent(doc, "desc", FieldDesc.Text);
        SetIfPresent(doc, "model", FieldModel.Text);
        SetIfPresent(doc, "scale", FieldScale.Text);
        SetIfPresent(doc, "collisionmode", GetIntOptionValue(FieldCollisionMode));
        SetIfPresent(doc, "defaultstatic", GetIntOptionValue(FieldDefaultStatic));
        SetIfPresent(doc, "ischaracter", FieldIsCharacter.Text);

        ApplyAnimFieldsToDoc(doc);

        foreach (var map in _mapFields)
            SetIfPresent(doc, map.Key, map.Value);
    }

    private void ApplyAnimFieldsToDoc(FpeDocument doc)
    {
        var keepKeys = new HashSet<string>(
            _animFields.Select(s => s.Key),
            StringComparer.OrdinalIgnoreCase);

        // Drop animN keys removed in the form
        foreach (var (key, _) in doc.GetAnimSlots().ToList())
        {
            if (!keepKeys.Contains(key))
                doc.RemoveKey(key);
        }

        // Only write remaining anim section keys when the form has something to say
        var hasAnimContent = !string.IsNullOrWhiteSpace(FieldAnimSpeed.Text)
            || !string.IsNullOrWhiteSpace(FieldAnimMax.Text)
            || !string.IsNullOrWhiteSpace(FieldPlayAnimInEditor.Text)
            || _animFields.Count > 0;

        if (!hasAnimContent)
            return;

        doc.SetAnimRelatedValue("animspeed", FieldAnimSpeed.Text.Trim());
        doc.SetAnimRelatedValue("animmax", string.IsNullOrWhiteSpace(FieldAnimMax.Text)
            ? _animFields.Count.ToString()
            : FieldAnimMax.Text.Trim());
        doc.SetAnimRelatedValue("playanimineditor", FieldPlayAnimInEditor.Text.Trim());

        foreach (var slot in _animFields)
            doc.SetAnimRelatedValue(slot.Key, slot.Value ?? "");
    }

    private static void SetIfPresent(FpeDocument doc, string key, string value)
    {
        // Always set — creates key if missing so users can add maps
        doc.SetValue(key, value);
    }

    private void Optimize_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetActionTargets();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Select or check a model first.", "Optimize", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OptimizeWindow(_paths, _refIndex, _allEntities, targets)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            StatusText.Text = dialog.Result?.Messages.FirstOrDefault() ?? "Optimize finished.";
            _checkedFpePaths.Clear();
            _ = RescanAsync();
        }
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetActionTargets();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Select or check a model first.", "Move", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new PickFolderWindow(_folderRoot, (parentRel, name) =>
        {
            FileOperations.CreateFolder(_paths, parentRel, name);
        })
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.SelectedRelativePath is null)
            return;

        var destDir = string.IsNullOrEmpty(dialog.SelectedRelativePath)
            ? _paths.EntityBankRoot
            : Path.Combine(_paths.EntityBankRoot, dialog.SelectedRelativePath);

        try
        {
            var plan = FileOperations.PlanMoveMany(targets, destDir, _paths);
            if (plan.FilesToMove.Count == 0)
            {
                MessageBox.Show(this, string.Join("\n", plan.Warnings.DefaultIfEmpty("Nothing to move.")),
                    "Move", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var label = targets.Count == 1
                ? targets[0].DisplayName
                : $"{targets.Count} models";
            var confirm = new ConfirmFilesWindow(
                "Confirm move",
                $"Move {label} to:\n{destDir}\n\nFiles:",
                plan.FilesToMove.Select(Path.GetFileName)!)
            {
                Owner = this
            };
            if (confirm.ShowDialog() != true)
                return;

            if (plan.Warnings.Count > 0)
            {
                var cont = MessageBox.Show(this, string.Join("\n", plan.Warnings) + "\n\nContinue?",
                    "Move warnings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (cont != MessageBoxResult.Yes)
                    return;
            }

            foreach (var entity in targets)
                _refIndex.RemoveEntity(entity.FpePath);

            FileOperations.ExecuteMoveMany(plan, _paths, targets);

            foreach (var entity in targets)
                _checkedFpePaths.Remove(entity.FpePath);

            StatusText.Text = targets.Count == 1
                ? "Moved " + targets[0].DisplayName
                : $"Moved {targets.Count} models";
            _ = RescanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Move", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseMapTexture_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show(this, "Select a model first.", "Browse texture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (sender is not FrameworkElement { Tag: FpeMapField field })
            return;

        var entityDir = Path.GetDirectoryName(_selected.FpePath) ?? _paths.EntityBankRoot;
        var picker = new OpenFileDialog
        {
            Title = $"Choose texture for {field.Key}",
            InitialDirectory = Directory.Exists(entityDir) ? entityDir : _paths.EntityBankRoot,
            Filter =
                "Texture files|*.dds;*.png;*.jpg;*.jpeg;*.tga;*.bmp;*.webp|" +
                "DDS|*.dds|PNG|*.png|JPEG|*.jpg;*.jpeg|All files|*.*"
        };

        if (picker.ShowDialog(this) != true)
            return;

        try
        {
            var source = picker.FileName;
            var destName = Path.GetFileName(source);
            var destPath = Path.Combine(entityDir, destName);
            var overwrite = false;

            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase)
                && File.Exists(destPath))
            {
                var answer = MessageBox.Show(
                    this,
                    $"A file named {destName} already exists in the model folder.\n\nOverwrite it?",
                    "Browse texture",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Cancel)
                    return;
                if (answer == MessageBoxResult.No)
                    return;
                overwrite = true;
            }

            var fileName = FileOperations.ImportTextureBesideEntity(_selected, source, _paths, overwrite);
            field.Value = fileName;

            // Keep associated-files list in sync for this session
            var importedFull = Path.Combine(entityDir, fileName);
            if (!_selected.AssociatedFiles.Any(f =>
                    f.Equals(importedFull, StringComparison.OrdinalIgnoreCase)))
            {
                _selected.AssociatedFiles = _selected.AssociatedFiles
                    .Append(importedFull)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                AssociatedList.Items.Clear();
                foreach (var f in _selected.AssociatedFiles)
                    AssociatedList.Items.Add(Path.GetFileName(f));
            }

            StatusText.Text = $"Set {field.Key} to {fileName} — remember to Save FPE";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Browse texture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetActionTargets();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Select or check a model first.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var plan = FileOperations.PlanDeleteMany(targets, _refIndex, _paths);
            var label = targets.Count == 1
                ? targets[0].DisplayName
                : $"{targets.Count} models";
            var confirm = new ConfirmFilesWindow(
                "Confirm delete",
                $"Send these files for {label} to the Recycle Bin?",
                plan.FilesToDelete.Select(f => Path.GetFileName(f)!),
                plan.FilesSkippedShared.Select(f => Path.GetFileName(f)!))
            {
                Owner = this
            };
            if (confirm.ShowDialog() != true)
                return;

            FileOperations.ExecuteDelete(plan, _paths, sendToRecycleBin: true,
                (path, recycle) => RecycleBin.DeleteFile(path, recycle));

            foreach (var entity in targets)
            {
                TryDeleteThumbbank(entity.RelativePath);
                _refIndex.RemoveEntity(entity.FpePath);
                _checkedFpePaths.Remove(entity.FpePath);
            }

            StatusText.Text = targets.Count == 1
                ? "Deleted " + targets[0].DisplayName
                : $"Deleted {targets.Count} models";
            _ = RescanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TryDeleteThumbbank(string relativeFpe)
    {
        try
        {
            var withoutExt = Path.ChangeExtension(relativeFpe, null)?.TrimEnd('.') ?? relativeFpe;
            var key = withoutExt.Replace('\\', '_').Replace('/', '_') + "512x288";
            foreach (var ext in new[] { ".jpg", ".png" })
            {
                var path = Path.Combine(_paths.ThumbBankRoot, key + ext);
                if (File.Exists(path))
                    RecycleBin.DeleteFile(path, true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void SetFilesFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select GameGuru MAX Files folder (contains entitybank and thumbbank)",
            InitialDirectory = _paths.FilesRoot
        };
        if (dialog.ShowDialog() != true)
            return;

        var selected = dialog.FolderName;
        // If user picked entitybank, use parent
        if (string.Equals(Path.GetFileName(selected), "entitybank", StringComparison.OrdinalIgnoreCase))
            selected = Path.GetDirectoryName(selected) ?? selected;

        _paths = new AppPaths(selected);
        SettingsStore.SavePaths(_paths);
        StatusText.Text = "Files root: " + _paths.FilesRoot;
        _ = RescanAsync();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
