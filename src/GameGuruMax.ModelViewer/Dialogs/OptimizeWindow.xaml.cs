using System.Windows;
using System.Windows.Controls;
using GameGuruMax.ModelViewer.Core;
using GameGuruMax.ModelViewer.Core.Optimize;

namespace GameGuruMax.ModelViewer.Dialogs;

public partial class OptimizeWindow : Window
{
    private readonly AppPaths _paths;
    private readonly SharedReferenceIndex _index;
    private readonly IReadOnlyList<EntityRecord> _allEntities;
    private readonly EntityRecord _selected;
    private OptimizePlan? _plan;

    public OptimizeResult? Result { get; private set; }

    public OptimizeWindow(
        AppPaths paths,
        SharedReferenceIndex index,
        IReadOnlyList<EntityRecord> allEntities,
        EntityRecord selected)
    {
        InitializeComponent();
        _paths = paths;
        _index = index;
        _allEntities = allEntities;
        _selected = selected;

        ResolutionBox.Items.Add(new ComboBoxItem { Content = "Original (no resize)", Tag = 0, IsSelected = true });
        ResolutionBox.Items.Add(new ComboBoxItem { Content = "512", Tag = 512 });
        ResolutionBox.Items.Add(new ComboBoxItem { Content = "1024", Tag = 1024 });
        ResolutionBox.Items.Add(new ComboBoxItem { Content = "2048", Tag = 2048 });
        ResolutionBox.Items.Add(new ComboBoxItem { Content = "4096", Tag = 4096 });
        ResolutionBox.SelectedIndex = 0;
        ResolutionBox.SelectionChanged += (_, _) => RebuildPlan();
        ConvertCheck.Checked += (_, _) => RebuildPlan();
        ConvertCheck.Unchecked += (_, _) => RebuildPlan();

        HeaderText.Text = $"Optimize textures for: {_selected.DisplayName}";
        RebuildPlan();
    }

    private void OptionsChanged(object sender, RoutedEventArgs e) => RebuildPlan();

    private OptimizeOptions CurrentOptions()
    {
        var max = 0;
        if (ResolutionBox.SelectedItem is ComboBoxItem { Tag: int tag })
            max = tag;

        return new OptimizeOptions
        {
            ConvertToDds = ConvertCheck.IsChecked == true,
            MaxResolution = max,
            DeleteOriginalsAfterConvert = DeleteOriginalsCheck.IsChecked == true,
            EntireFolder = FolderCheck.IsChecked == true
        };
    }

    private List<EntityRecord> TargetEntities(OptimizeOptions options)
    {
        if (!options.EntireFolder)
            return [_selected];

        return _allEntities
            .Where(e => e.FolderRelative.Equals(_selected.FolderRelative, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void RebuildPlan()
    {
        var options = CurrentOptions();
        var targets = TargetEntities(options);
        HeaderText.Text = options.EntireFolder
            ? $"Optimize textures for folder: {_selected.FolderRelative} ({targets.Count} models)"
            : $"Optimize textures for: {_selected.DisplayName}";

        _plan = TextureOptimizer.BuildPlan(targets, _index, _paths, options);
        PlanList.Items.Clear();

        var unconventionalCount = _plan.Files.Count(f => f.IsUnconventionalSize && f.Action is not "Skip");
        if (unconventionalCount > 0)
        {
            PlanList.Items.Add(
                $"⚠ {unconventionalCount} texture(s) have unconventional sizes (not 512/1024/2048/…). " +
                "DDS conversion keeps those pixel dimensions so the model UVs still fit.");
        }

        foreach (var f in _plan.Files)
        {
            var name = System.IO.Path.GetFileName(f.SourcePath);
            var prefix = f.IsUnconventionalSize && f.Action is not "Skip" ? "⚠ " : "";
            PlanList.Items.Add($"{prefix}{f.Action,-14} {name}  —  {f.Reason}");
        }

        foreach (var w in _plan.Warnings)
            PlanList.Items.Add("WARN: " + w);

        var work = _plan.Files.Count(f => f.Action is not "Skip");
        StatusLine.Text = work == 0
            ? "Nothing to do with current options."
            : unconventionalCount > 0
                ? $"{work} file(s) to process ({unconventionalCount} unconventional size). Leave Max resolution on Original to keep exact dimensions."
                : $"{work} file(s) will be processed; {_plan.FpePathsToTouch.Count} FPE(s) may be updated.";
        RunButton.IsEnabled = work > 0;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_plan is null)
            return;

        var options = CurrentOptions();
        var work = _plan.Files.Count(f => f.Action is not "Skip");
        if (work == 0)
            return;

        var unconventional = _plan.Files.Where(f => f.IsUnconventionalSize && f.Action is not "Skip").ToList();
        var message =
            $"Process {work} texture file(s)?\n\n" +
            "PNG/JPEG → DDS uses BC1 (opaque) or BC7 (transparency).\n" +
            "Existing DDS (including surface maps) are only resized when requested.\n" +
            "FPE map paths will be updated to .dds names.";

        if (unconventional.Count > 0)
        {
            message +=
                "\n\nWARNING — unconventional texture size(s):\n" +
                string.Join("\n", unconventional.Take(8).Select(f =>
                    $"• {System.IO.Path.GetFileName(f.SourcePath)} ({f.Width}x{f.Height})")) +
                (unconventional.Count > 8 ? $"\n• …and {unconventional.Count - 8} more" : "") +
                "\n\nThese are not standard 512/1024/2048 sizes. " +
                "They will be converted at their current dimensions (e.g. 512x1012 stays 512x1012) " +
                "so they still fit the model. Only change Max resolution if you want them scaled down.";
        }

        var confirm = MessageBox.Show(
            this,
            message,
            unconventional.Count > 0 ? "Confirm optimize — size warning" : "Confirm optimize",
            MessageBoxButton.YesNo,
            unconventional.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        RunButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(msg => StatusLine.Text = msg);
            var plan = _plan;
            var result = await Task.Run(() =>
                TextureOptimizer.Execute(plan, options, _paths, _allEntities, progress));

            Result = result;
            var summary = string.Join("\n", result.Messages);
            if (result.Errors.Count > 0)
                summary += "\n\nErrors:\n" + string.Join("\n", result.Errors.Take(12));

            MessageBox.Show(this, summary, "Optimize complete", MessageBoxButton.OK,
                result.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Optimize failed", MessageBoxButton.OK, MessageBoxImage.Error);
            RunButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
