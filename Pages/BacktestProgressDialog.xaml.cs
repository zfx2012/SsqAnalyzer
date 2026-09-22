using System.ComponentModel;
using System.Windows;

namespace SsqAnalyzer.Pages;

/// <summary>
/// 回测进度模态窗口。
/// 用法：
///   var dlg = new BacktestProgressDialog(owner) { Title = "批量回测" };
///   dlg.Show();
///   // 后台 Task.Run 跑回测，IProgress&lt;T&gt; 回调中 dlg.UpdateProgress(done, total) / dlg.SetIndeterminate()
///   // 完成后 dlg.Close()
/// </summary>
public partial class BacktestProgressDialog : Window
{
    /// <summary>是否为不确定模式（单规则回测：转圈，无 done/total）。</summary>
    private bool _indeterminate = true;

    public BacktestProgressDialog()
    {
        InitializeComponent();
    }

    public BacktestProgressDialog(Window owner) : this()
    {
        Owner = owner;
    }

    /// <summary>设置标题文字（"回测中" / "批量回测中" 等）。</summary>
    public void SetTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return;
        Title = title;
        TitleText.Text = title + "...";
    }

    /// <summary>不确定模式（单规则）：转圈，文案显示 ruleName。</summary>
    public void SetIndeterminate(string detailText = "")
    {
        _indeterminate = true;
        ProgressBar.IsIndeterminate = true;
        if (!string.IsNullOrEmpty(detailText))
            DetailText.Text = detailText;
    }

    /// <summary>确定模式（批量回测）：进度条显示 done/total 百分比，文案显示 "3/12"。</summary>
    public void UpdateProgress(int done, int total)
    {
        if (_indeterminate)
        {
            _indeterminate = false;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
        }
        if (total <= 0)
        {
            ProgressBar.Value = 0;
            DetailText.Text = "准备中...";
            return;
        }
        double pct = (double)done / total * 100.0;
        if (pct > 100) pct = 100;
        if (pct < 0) pct = 0;
        ProgressBar.Value = pct;
        DetailText.Text = $"已完成 {done} / {total} 条规则（{(int)pct}%）";
    }

    /// <summary>禁止用户手动关闭（点 X 无效）：回测期间只能由代码关闭。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // 回测未完成时不允许关闭
        if (_indeterminate || ProgressBar.Value < 100)
        {
            e.Cancel = true;
        }
        else
        {
            base.OnClosing(e);
        }
    }
}
