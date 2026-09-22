using System.Windows;
using System.Windows.Controls;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages;

/// <summary>
/// 添加杀号规则窗口（非模态）。
/// 三区：A. LLM API 状态条 + 去设置；B. 已有自定义规则表（删除）；C. 新增表单（NL→Code→沙箱→回测→入库）。
/// 入库走 IRuleRepository.Add，触发 RulesChanged → KillPage 主表格自动刷新。
/// </summary>
public partial class AddRuleWindow : Window
{
    private readonly IRuleRepository _ruleRepo;
    private readonly IRuleExecutor _executor;
    private readonly IBacktestEngine _backtestEngine;
    private readonly IRuleContextBuilder _ctxBuilder;
    private readonly ITicketStore _store;
    private readonly INlToCodeService _nlService;
    private readonly IKillSettings _killSettings;
    private bool _closed;
    private int _formVersion;
    private CancellationTokenSource? _generationCts;
    private CancellationTokenSource? _backtestCts;

    private void InvalidatePendingWork()
    {
        bool wasRunning = _generationCts is not null || _backtestCts is not null;
        _formVersion++;
        _generationCts?.Cancel();
        _backtestCts?.Cancel();
        if (wasRunning && !_closed) SetStatus("内容已更改，已取消旧任务", ok: true);
    }

    public AddRuleWindow()
        : this(App.Services.GetRequiredService<IRuleRepository>(),
               App.Services.GetRequiredService<IRuleExecutor>(),
               App.Services.GetRequiredService<IBacktestEngine>(),
               App.Services.GetRequiredService<IRuleContextBuilder>(),
               App.Services.GetRequiredService<ITicketStore>(),
               App.Services.GetRequiredService<INlToCodeService>(),
               App.Services.GetRequiredService<IKillSettings>()) { }

    public AddRuleWindow(
        IRuleRepository ruleRepo,
        IRuleExecutor executor,
        IBacktestEngine backtestEngine,
        IRuleContextBuilder ctxBuilder,
        ITicketStore store,
        INlToCodeService nlService,
        IKillSettings killSettings)
    {
        _ruleRepo = ruleRepo ?? throw new ArgumentNullException(nameof(ruleRepo));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _backtestEngine = backtestEngine ?? throw new ArgumentNullException(nameof(backtestEngine));
        _ctxBuilder = ctxBuilder ?? throw new ArgumentNullException(nameof(ctxBuilder));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _nlService = nlService ?? throw new ArgumentNullException(nameof(nlService));
        _killSettings = killSettings ?? throw new ArgumentNullException(nameof(killSettings));
        InitializeComponent();
        NlDescBox.TextChanged += (_, _) => InvalidatePendingWork();
        CodeBox.TextChanged += (_, _) => InvalidatePendingWork();
        BallBox.SelectionChanged += (_, _) => InvalidatePendingWork();
        CategoryBox.SelectionChanged += (_, _) => InvalidatePendingWork();
        Closed += (_, _) => { _closed = true; InvalidatePendingWork(); };
        Loaded += (_, _) => { RefreshLlmStatus(); RefreshCustomRules(); };
    }

    // ==================== A. LLM 状态 ====================

    private void RefreshLlmStatus()
    {
        var key = _store.LoadLlmApiKey();
        var model = _store.LoadLlmModel();
        var baseUrl = _store.LoadLlmBaseUrl();

        if (string.IsNullOrWhiteSpace(key))
        {
            LlmStatusText.Text = "未配置 API Key";
            LlmDetailText.Text = "生成代码需要先配置 API Key、BaseUrl 与模型";
            return;
        }
        var providerLabel = string.IsNullOrWhiteSpace(baseUrl)
            ? "未配置"
            : LlmProviderPresets.GuessPresetIdByBaseUrl(baseUrl) is { } pid && pid != "custom"
                ? LlmProviderPresets.FindById(pid)?.DisplayName ?? baseUrl
                : baseUrl!;
        LlmStatusText.Text = $"已配置 · {providerLabel} · 模型: {model ?? "（未选）"}";
        LlmDetailText.Text = $"API Key 长度 {key.Length} 位";
    }

    private void GoSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LlmApiConfigDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            RefreshLlmStatus();
    }

    // ==================== B. 已有自定义规则 ====================

    private void RefreshCustomRules()
    {
        var rules = _ruleRepo.GetAll().Where(r => !r.IsBuiltin).ToList();
        CustomRuleCount.Text = $"共 {rules.Count} 条";
        CustomRulesGrid.ItemsSource = rules.Select(r => new CustomRuleRow
        {
            IsVisible = r.IsVisible,
            IsEnabled = r.IsEnabled,
            RuleId = r.RuleId,
            Name = r.Name,
            CategoryLabel = r.Category switch
            {
                RuleCategory.Pattern => "图形",
                RuleCategory.Formula => "公式",
                _ => "其他"
            },
            BallLabel = r.BallType == BallType.Red ? "红球" : "蓝球",
            Description = r.Description
        }).ToList();
    }

    /// <summary>显示列点击：切换规则在杀号页列表的显示/隐藏（Update 持久化 + 触发 RulesChanged → KillPage 自动刷新）。</summary>
    private void VisibleToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is CustomRuleRow row)
        {
            var rule = _ruleRepo.Find(row.RuleId);
            if (rule is KillRule kr)
            {
                kr.IsVisible = cb.IsChecked == true;
                _ruleRepo.Update(kr);
                RefreshCustomRules();
            }
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not CustomRuleRow row) return;

        var confirm = MessageBox.Show($"确认删除规则 {row.RuleId}（{row.Name}）？\n删除后不可恢复。",
            "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            _ruleRepo.Delete(row.RuleId);
            RefreshCustomRules();
            SetStatus($"已删除规则 {row.RuleId}", ok: true);
        }
        catch (Exception ex)
        {
            SetStatus($"删除失败：{ex.Message}", ok: false);
        }
    }

    // ==================== C. 新增规则 ====================

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _generationCts is not null) return;
        var nl = NlDescBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(nl))
        {
            SetStatus("请先填写自然语言描述", ok: false);
            return;
        }
        var key = _store.LoadLlmApiKey();
        var model = _store.LoadLlmModel();
        var baseUrl = _store.LoadLlmBaseUrl();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(baseUrl))
        {
            SetStatus("未配置 LLM API Key/地址/模型，请先点「去设置」", ok: false);
            return;
        }

        var (ball, category) = ReadFormSelections();
        InvalidatePendingWork();
        int version = _formVersion;
        using var cts = new CancellationTokenSource();
        _generationCts = cts;
        BtnGenerate.IsEnabled = false;
        SetStatus("正在调用 LLM 生成代码...", ok: true, loading: true);
        try
        {
            var js = await _nlService.GenerateAsync(nl, ball, category, key!, model!, baseUrl, cts.Token);
            if (_closed || cts.IsCancellationRequested || version != _formVersion) return;
            _generationCts = null;
            CodeBox.Text = js;
            SetStatus("代码已生成，可编辑后点「沙箱验证」", ok: true);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested && version == _formVersion)
                SetStatus($"生成失败：{ex.Message}", ok: false);
        }
        finally
        {
            _generationCts = null;
            if (!_closed) BtnGenerate.IsEnabled = true;
        }
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        InvalidatePendingWork();
        var code = CodeBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            SetStatus("代码为空，请先生成或粘贴代码", ok: false);
            return;
        }
        if (!code.Contains("getKillBalls", StringComparison.Ordinal))
        {
            SetStatus("代码未定义 getKillBalls(ctx) 函数", ok: false);
            return;
        }

        var (ball, category) = ReadFormSelections();
        var tmpRule = BuildTempRule(code, ball, category);
        try
        {
            var ctx = _ctxBuilder.BuildLatest();
            var result = _executor.Execute(tmpRule, ctx);
            SetStatus($"✅ 沙箱验证通过 · 杀 [{string.Join(",", result.KilledBalls)}]（{result.KilledBalls.Count} 球）",
                      ok: true);
        }
        catch (RuleExecutionException ex)
        {
            SetStatus($"❌ 沙箱执行失败：{ex.Message}", ok: false);
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 验证异常：{ex.Message}", ok: false);
        }
    }

    private async void Backtest_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _backtestCts is not null) return;
        var code = CodeBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            SetStatus("代码为空，请先生成或粘贴代码", ok: false);
            return;
        }

        var (ball, category) = ReadFormSelections();
        var tmpRule = BuildTempRule(code, ball, category);
        InvalidatePendingWork();
        int version = _formVersion;
        using var cts = new CancellationTokenSource();
        _backtestCts = cts;
        BtnBacktest.IsEnabled = false;
        SetStatus("小样本回测中（近 30 次触发）...", ok: true, loading: true);
        try
        {
            var stat = await Task.Run(() => _backtestEngine.Run(tmpRule, BacktestWindow.Last30Triggers, cts.Token), cts.Token);
            if (_closed || cts.IsCancellationRequested || version != _formVersion) return;
            if (stat.FailureCount > 0)
                SetStatus($"回测存在执行异常：检查 {stat.EvaluatedCount} 期，失败 {stat.FailureCount} 次；结果不参与达标判断。{stat.LastExecutionError}", ok: false);
            else if (stat.SampleInsufficient)
                SetStatus($"回测完成：触发 {stat.TriggeredCount} 次（样本不足）· 准确率 {stat.Accuracy:P1}",
                          ok: true);
            else
                SetStatus($"回测完成：触发 {stat.TriggeredCount} 次 · 杀球 {stat.KillBallCount} · 准确率 {stat.Accuracy:P1} · 耗时 {FormatElapsed(stat.ElapsedMs)}",
                          ok: true);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed && !cts.IsCancellationRequested && version == _formVersion)
                SetStatus($"回测失败：{ex.Message}", ok: false);
        }
        finally
        {
            _backtestCts = null;
            if (!_closed) BtnBacktest.IsEnabled = true;
        }
    }

    private void Commit_Click(object sender, RoutedEventArgs e)
    {
        InvalidatePendingWork();
        var name = NameBox.Text?.Trim();
        var code = CodeBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetStatus("请填写规则名称", ok: false);
            return;
        }
        if (string.IsNullOrWhiteSpace(code) || !code.Contains("getKillBalls", StringComparison.Ordinal))
        {
            SetStatus("代码为空或未定义 getKillBalls(ctx)", ok: false);
            return;
        }

        var (ball, category) = ReadFormSelections();
        var ruleId = NextCustomRuleId(category, ball);
        try
        {
            var rule = new KillRule
            {
                RuleId = ruleId,
                Name = name,
                Category = category,
                BallType = ball,
                IsBuiltin = false,
                IsEnabled = true,
                MinAccuracy = _killSettings.GetMinAccuracy(ball),
                ForceEnabled = false,
                JsCode = code,
                Description = NlDescBox.Text?.Trim() ?? "",
                Source = "user",
                Tags = new List<string> { "user-llm" }
            };
            _ruleRepo.Add(rule);

            // 清空表单，准备添加下一条
            NameBox.Text = "";
            NlDescBox.Text = "";
            CodeBox.Text = "";
            RefreshCustomRules();
            SetStatus($"✅ 已入库：{ruleId}（{name}），可继续添加下一条", ok: true);
        }
        catch (Exception ex)
        {
            SetStatus($"入库失败：{ex.Message}", ok: false);
        }
    }

    // ==================== 辅助 ====================

    private (BallType ball, RuleCategory category) ReadFormSelections()
    {
        var ball = BallBox.SelectedIndex == 1 ? BallType.Blue : BallType.Red;
        var category = CategoryBox.SelectedIndex switch
        {
            0 => RuleCategory.Pattern,
            1 => RuleCategory.Formula,
            _ => RuleCategory.Other
        };
        return (ball, category);
    }

    /// <summary>构造临时规则用于沙箱验证 / 小样本回测（不入库、不持久化）。
    /// 用 GUID 后缀的 ruleId 确保 JintRuleExecutor 每次重新编译最新代码（避免缓存旧码）。</summary>
    private KillRule BuildTempRule(string code, BallType ball, RuleCategory category)
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "临时规则" : NameBox.Text.Trim();
        return new KillRule
        {
            RuleId = $"C-tmp-{Guid.NewGuid():N}",
            Name = name,
            Category = category,
            BallType = ball,
            IsBuiltin = false,
            IsEnabled = true,
            MinAccuracy = _killSettings.GetMinAccuracy(ball),
            ForceEnabled = false,
            JsCode = code,
            Description = NlDescBox.Text?.Trim() ?? "",
            Source = "user",
            Tags = new List<string>()
        };
    }

    /// <summary>生成自定义规则 ID，格式同内置：C-{类别}-{球种}-NNN。
    /// 类别：G(图形)/F(公式)/O(其他)；球种：R(红)/B(蓝)。编号按"类别+球种"前缀独立递增。</summary>
    private string NextCustomRuleId(RuleCategory category, BallType ball)
    {
        var catCode = category switch
        {
            RuleCategory.Pattern => "G",
            RuleCategory.Formula => "F",
            _ => "O"
        };
        var ballCode = ball == BallType.Blue ? "B" : "R";
        var prefix = $"C-{catCode}-{ballCode}-";

        int max = 0;
        foreach (var r in _ruleRepo.GetAll())
        {
            if (r.IsBuiltin) continue;
            var id = r.RuleId;
            if (id.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(prefix.Length), out var n) && n > max)
                max = n;
        }
        return $"{prefix}{max + 1:D3}";
    }

    private static string FormatElapsed(long ms)
        => ms <= 0 ? "—" : (ms < 1000 ? $"{ms} ms" : $"{ms / 1000.0:F2} s");

    private void SetStatus(string text, bool ok, bool loading = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = loading
            ? (System.Windows.Media.Brush)Application.Current.FindResource("TextTertiary")
            : ok
                ? (System.Windows.Media.Brush)Application.Current.FindResource("Accent")
                : (System.Windows.Media.Brush)Application.Current.FindResource("HotColor");
    }

    private sealed class CustomRuleRow
    {
        public bool IsVisible { get; set; }
        public bool IsEnabled { get; set; }
        public string RuleId { get; set; } = "";
        public string Name { get; set; } = "";
        public string CategoryLabel { get; set; } = "";
        public string BallLabel { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
