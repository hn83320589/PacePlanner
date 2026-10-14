using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacePlanner.App.Services;
using PacePlanner.Core.Course;
using PacePlanner.Core.Export;
using PacePlanner.Core.Formatting;
using PacePlanner.Core.Gpx;
using PacePlanner.Core.Pacing;
using PacePlanner.Data;

namespace PacePlanner.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly PlanRepository _repository;
    private readonly IUiServices _ui;

    private long _currentId;
    private string? _gpxContent;
    private CourseProfile? _course;
    private bool _isLoading;

    public MainViewModel(PlanRepository repository, IUiServices ui)
    {
        _repository = repository;
        _ui = ui;
    }

    /// <summary>Raised after every recalculation so the view can redraw the profile chart.</summary>
    public event EventHandler? PlanChanged;

    public ObservableCollection<PlanSummary> Plans { get; } = [];

    public ObservableCollection<SplitRow> Rows { get; } = [];

    public IReadOnlyList<string> DistanceOptions { get; } = ["5K", "10K", "半馬 21.0975 km", "全馬 42.195 km", "自訂"];

    public IReadOnlyList<string> StrategyOptions { get; } = ["等速", "負分段", "坡度調整"];

    public IReadOnlyList<string> AidModeOptions { get; } = ["無補給", "每 N 公里", "指定公里數"];

    public PacePlan? CurrentPlan { get; private set; }

    [ObservableProperty]
    private PlanSummary? _selectedPlan;

    [ObservableProperty]
    private string _planName = string.Empty;

    [ObservableProperty]
    private string _gpxFileName = "（尚未匯入 GPX）";

    [ObservableProperty]
    private string _courseDistanceText = "—";

    [ObservableProperty]
    private string _ascentText = "—";

    [ObservableProperty]
    private string _descentText = "—";

    [ObservableProperty]
    private string _maxElevationText = "—";

    [ObservableProperty]
    private string _minElevationText = "—";

    [ObservableProperty]
    private string? _courseNote;

    [ObservableProperty]
    private int _distanceIndex = (int)RaceDistance.Marathon;

    [ObservableProperty]
    private double _customDistanceKm = 10;

    [ObservableProperty]
    private string _targetTimeText = "4:00:00";

    [ObservableProperty]
    private int _strategyIndex;

    [ObservableProperty]
    private double _negativeSplitSeconds = 5;

    [ObservableProperty]
    private int _aidModeIndex;

    [ObservableProperty]
    private double _aidIntervalKm = 5;

    [ObservableProperty]
    private string _aidListText = string.Empty;

    [ObservableProperty]
    private double _aidStopSeconds = 20;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isDirty;

    public bool IsCustomDistance => DistanceIndex == (int)RaceDistance.Custom;

    public bool IsNegativeSplit => StrategyIndex == (int)PaceStrategy.NegativeSplit;

    public bool IsAidEveryN => AidModeIndex == (int)AidStationMode.EveryNKm;

    public bool IsAidList => AidModeIndex == (int)AidStationMode.CustomList;

    public bool HasAid => AidModeIndex != (int)AidStationMode.None;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool HasCourseNote => !string.IsNullOrEmpty(CourseNote);

    public void Initialize()
    {
        ReloadList();
        if (Plans.Count == 0)
        {
            var id = _repository.Insert(new SavedPlan { Name = "我的第一個方案" });
            ReloadList(id);
        }
        else
        {
            SelectedPlan = Plans[0];
        }
    }

    /// <summary>Persists pending edits; called when switching plans and when the window closes.</summary>
    public void SaveIfDirty()
    {
        if (IsDirty && _currentId != 0)
        {
            Save();
        }
    }

    public void ReportUnexpectedError(Exception ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Message) ? "（無訊息）" : ex.Message.Trim();
        ShowError($"發生未預期的錯誤：{detail}［{ex.GetType().Name}, 0x{ex.HResult:X8}］");
    }

    [RelayCommand]
    private void Save()
    {
        if (_currentId == 0)
        {
            return;
        }

        try
        {
            _repository.Update(ToSavedPlan());
            IsDirty = false;
            ReloadList(_currentId, raiseSelection: false);
            ShowStatus("已儲存。");
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task NewPlanAsync()
    {
        var name = await _ui.PromptTextAsync("新增方案", "新方案");
        if (name is null)
        {
            return;
        }

        RunGuarded(() =>
        {
            SaveIfDirty();
            ReloadList(_repository.Insert(new SavedPlan { Name = name }));
        });
    }

    [RelayCommand]
    private async Task DuplicatePlanAsync()
    {
        if (_currentId == 0)
        {
            return;
        }

        var name = await _ui.PromptTextAsync("複製方案", $"{PlanName} (複本)");
        if (name is null)
        {
            return;
        }

        RunGuarded(() =>
        {
            SaveIfDirty();
            ReloadList(_repository.Duplicate(_currentId, name));
            ShowStatus("已複製方案。");
        });
    }

    [RelayCommand]
    private async Task RenamePlanAsync()
    {
        if (_currentId == 0)
        {
            return;
        }

        var name = await _ui.PromptTextAsync("重新命名方案", PlanName);
        if (name is null)
        {
            return;
        }

        RunGuarded(() =>
        {
            _repository.Rename(_currentId, name);
            _isLoading = true;
            PlanName = name.Trim();
            _isLoading = false;
            ReloadList(_currentId, raiseSelection: false);
            ShowStatus("已重新命名。");
        });
    }

    [RelayCommand]
    private async Task DeletePlanAsync()
    {
        if (_currentId == 0 || !await _ui.ConfirmAsync("刪除方案", $"確定要刪除「{PlanName}」嗎？此動作無法復原。", "刪除"))
        {
            return;
        }

        RunGuarded(() =>
        {
            _repository.Delete(_currentId);
            _currentId = 0;
            IsDirty = false;
            Initialize();
            ShowStatus("已刪除方案。");
        });
    }

    [RelayCommand]
    private async Task ImportGpxAsync()
    {
        var path = await _ui.PickGpxFileAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            var content = File.ReadAllText(path);
            var course = CourseProfile.FromTrack(GpxParser.Parse(content));
            if (course.Stats.TotalDistanceKm <= 0)
            {
                throw new GpxFormatException("軌跡總距離為 0（可能只有一個軌跡點），無法規劃配速。");
            }

            _gpxContent = content;
            _course = course;
            GpxFileName = Path.GetFileName(path);
            UpdateCourseStats();
            MarkDirty();
            Recalculate();
            ShowStatus($"已匯入 {GpxFileName}。");
        }
        catch (GpxFormatException ex)
        {
            ShowError($"無法匯入「{Path.GetFileName(path)}」：{ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"無法讀取檔案：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (BuildReport() is not { } report)
        {
            return;
        }

        var path = await _ui.PickSaveFileAsync(SafeFileName(PlanName), ".csv", "CSV（Excel）");
        if (path is not null)
        {
            RunGuarded(() =>
            {
                CsvExporter.Save(report, path);
                ShowStatus($"已匯出 CSV：{path}");
            });
        }
    }

    [RelayCommand]
    private async Task ExportHtmlAsync()
    {
        if (BuildReport() is not { } report)
        {
            return;
        }

        var path = await _ui.PickSaveFileAsync(SafeFileName(PlanName), ".html", "可列印配速表（HTML）");
        if (path is not null)
        {
            RunGuarded(() =>
            {
                HtmlExporter.Save(report, path);
                ShowStatus($"已匯出 HTML：{path}");
                _ui.OpenWithShell(path);
            });
        }
    }

    partial void OnSelectedPlanChanged(PlanSummary? oldValue, PlanSummary? newValue)
    {
        if (_isLoading || newValue is null || newValue.Id == _currentId)
        {
            return;
        }

        SaveIfDirty();
        Load(newValue.Id);
    }

    partial void OnDistanceIndexChanged(int value)
    {
        if (value == (int)RaceDistance.Custom && !_isLoading && _course is not null)
        {
            CustomDistanceKm = Math.Round(_course.Stats.TotalDistanceKm, 3);
        }

        OnInputChanged(nameof(IsCustomDistance));
    }

    partial void OnCustomDistanceKmChanged(double value) => OnInputChanged();

    partial void OnTargetTimeTextChanged(string value) => OnInputChanged();

    partial void OnStrategyIndexChanged(int value) => OnInputChanged(nameof(IsNegativeSplit));

    partial void OnNegativeSplitSecondsChanged(double value) => OnInputChanged();

    partial void OnAidModeIndexChanged(int value) => OnInputChanged(nameof(IsAidEveryN), nameof(IsAidList), nameof(HasAid));

    partial void OnAidIntervalKmChanged(double value) => OnInputChanged();

    partial void OnAidListTextChanged(string value) => OnInputChanged();

    partial void OnAidStopSecondsChanged(double value) => OnInputChanged();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnCourseNoteChanged(string? value) => OnPropertyChanged(nameof(HasCourseNote));

    private void OnInputChanged(params string[] dependentProperties)
    {
        foreach (var name in dependentProperties)
        {
            OnPropertyChanged(name);
        }

        if (_isLoading)
        {
            return;
        }

        MarkDirty();
        Recalculate();
    }

    private void MarkDirty()
    {
        if (!_isLoading)
        {
            IsDirty = true;
        }
    }

    private void ReloadList(long? selectId = null, bool raiseSelection = true)
    {
        var wasLoading = _isLoading;
        _isLoading = !raiseSelection;
        Plans.Clear();
        foreach (var p in _repository.List())
        {
            Plans.Add(p);
        }

        SelectedPlan = Plans.FirstOrDefault(p => p.Id == selectId) ?? Plans.FirstOrDefault();
        _isLoading = wasLoading;
    }

    private void Load(long id)
    {
        var plan = _repository.Get(id);
        if (plan is null)
        {
            ShowError($"找不到方案（id={id}）。");
            return;
        }

        string? loadError = null;
        _isLoading = true;
        try
        {
            _currentId = plan.Id;
            PlanName = plan.Name;
            _gpxContent = plan.GpxContent;
            _course = null;
            GpxFileName = plan.GpxFileName ?? "（尚未匯入 GPX）";
            if (plan.GpxContent is not null)
            {
                try
                {
                    _course = CourseProfile.FromTrack(GpxParser.Parse(plan.GpxContent));
                }
                catch (GpxFormatException ex)
                {
                    loadError = $"方案中儲存的 GPX 無法讀取：{ex.Message}";
                }
            }

            DistanceIndex = (int)plan.RaceDistance;
            CustomDistanceKm = plan.DistanceKm;
            TargetTimeText = TimeFormat.FormatDuration(plan.TargetSeconds);
            StrategyIndex = (int)plan.Strategy;
            NegativeSplitSeconds = plan.NegativeSplitSeconds;
            AidModeIndex = (int)plan.AidMode;
            AidIntervalKm = plan.AidIntervalKm;
            AidListText = AidStationListParser.Format(plan.AidCustomKms);
            AidStopSeconds = plan.AidStopSeconds;
        }
        finally
        {
            _isLoading = false;
        }

        IsDirty = false;
        foreach (var name in new[] { nameof(IsCustomDistance), nameof(IsNegativeSplit), nameof(IsAidEveryN), nameof(IsAidList), nameof(HasAid) })
        {
            OnPropertyChanged(name);
        }

        UpdateCourseStats();
        Recalculate();
        if (loadError is not null)
        {
            ShowError(loadError);
        }
    }

    private SavedPlan ToSavedPlan()
    {
        AidStationListParser.TryParse(AidListText, out var kms, out _);
        TimeFormat.TryParseHms(TargetTimeText, out var target);
        var existing = _repository.Get(_currentId);
        return new SavedPlan
        {
            Id = _currentId,
            Name = PlanName,
            GpxFileName = _gpxContent is null ? null : GpxFileName,
            GpxContent = _gpxContent,
            RaceDistance = (RaceDistance)DistanceIndex,
            DistanceKm = PlanDistanceKm() ?? CustomDistanceKm,
            TargetSeconds = target > 0 ? target : existing?.TargetSeconds ?? 4 * 3600,
            Strategy = (PaceStrategy)StrategyIndex,
            NegativeSplitSeconds = ToInt(NegativeSplitSeconds),
            AidMode = (AidStationMode)AidModeIndex,
            AidIntervalKm = double.IsFinite(AidIntervalKm) ? AidIntervalKm : 5,
            AidCustomKms = kms,
            AidStopSeconds = ToInt(AidStopSeconds),
        };
    }

    private double? PlanDistanceKm() =>
        RaceDistances.Km((RaceDistance)DistanceIndex) ?? (double.IsFinite(CustomDistanceKm) && CustomDistanceKm > 0 ? CustomDistanceKm : null);

    private void UpdateCourseStats()
    {
        if (_course is null)
        {
            CourseDistanceText = AscentText = DescentText = MaxElevationText = MinElevationText = "—";
            CourseNote = null;
            return;
        }

        var s = _course.Stats;
        CourseDistanceText = $"{s.TotalDistanceKm:0.00} km";
        AscentText = $"{s.AscentMeters:0} m";
        DescentText = $"{s.DescentMeters:0} m";
        MaxElevationText = s.MaxElevationMeters is { } max ? $"{max:0} m" : "無資料";
        MinElevationText = s.MinElevationMeters is { } min ? $"{min:0} m" : "無資料";
        CourseNote = s.HasElevation ? null : "此 GPX 沒有海拔資料：坡度一律視為 0，坡度調整策略的結果等同等速。";
    }

    private void Recalculate()
    {
        CurrentPlan = null;
        Rows.Clear();
        SummaryText = string.Empty;
        ErrorMessage = null;
        try
        {
            var settings = BuildSettings();
            CurrentPlan = PaceCalculator.Calculate(_course, settings);
            foreach (var row in SplitTable.Rows(CurrentPlan))
            {
                Rows.Add(row);
            }

            SummaryText = BuildSummary(CurrentPlan);
        }
        catch (PlanningException ex)
        {
            ErrorMessage = ex.Message;
        }

        PlanChanged?.Invoke(this, EventArgs.Empty);
    }

    private PlanSettings BuildSettings()
    {
        var km = PlanDistanceKm() ?? throw new PlanningException("請輸入大於 0 的自訂距離。");
        if (!TimeFormat.TryParseHms(TargetTimeText, out var target))
        {
            throw new PlanningException("目標完賽時間格式錯誤，請輸入 hh:mm:ss，例如 3:45:00。");
        }

        if (!AidStationListParser.TryParse(AidListText, out var kms, out var listError) && IsAidList)
        {
            throw new PlanningException(listError!);
        }

        var aid = new AidStationSettings((AidStationMode)AidModeIndex, AidIntervalKm, kms, ToInt(AidStopSeconds));
        return new PlanSettings(km, target, (PaceStrategy)StrategyIndex, ToInt(NegativeSplitSeconds), aid);
    }

    private string BuildSummary(PacePlan plan)
    {
        var parts = new List<string>
        {
            $"總時間 {TimeFormat.FormatDuration(plan.TotalSeconds)}",
            $"平均配速 {TimeFormat.FormatPace(plan.RunningSeconds / plan.Settings.DistanceKm)} /km",
        };
        if (plan.AidStationKms.Count > 0)
        {
            parts.Add($"補給 {plan.AidStationKms.Count} 站，共停留 {TimeFormat.FormatSplit(plan.TotalAidSeconds)}");
        }

        if (_course is not null && Math.Abs(_course.Stats.TotalDistanceKm - plan.Settings.DistanceKm) > 0.001)
        {
            parts.Add($"GPX 實測 {_course.Stats.TotalDistanceKm:0.00} km 已等比例縮放為 {plan.Settings.DistanceKm.ToString("0.###", CultureInfo.InvariantCulture)} km");
        }

        return string.Join("　｜　", parts);
    }

    private PlanReport? BuildReport()
    {
        if (CurrentPlan is null)
        {
            ShowError(ErrorMessage ?? "目前的設定無法產生配速表，請先修正錯誤。");
            return null;
        }

        return new PlanReport(PlanName, _course?.Stats, CurrentPlan);
    }

    private void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        StatusMessage = null;
        ErrorMessage = message;
    }

    private void ShowStatus(string message) => StatusMessage = message;

    private static int ToInt(double value) => double.IsFinite(value) ? (int)Math.Round(value) : 0;

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "配速表" : cleaned + " 配速表";
    }
}
