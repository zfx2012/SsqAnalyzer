using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;
using Microsoft.Extensions.DependencyInjection;

namespace SsqAnalyzer.Pages
{
    public partial class SettingsPage : UserControl
    {
        private readonly IDataService _data;
        private readonly ITicketStore _store;
        private readonly ICredentialStore _credentialStore;
        private bool _confirmingClear = false;
        private bool _subscribed;
        private int _loadVersion;
        private int _popupVersion;

        public SettingsPage() : this(App.Services.GetRequiredService<IDataService>(), App.Services.GetRequiredService<ITicketStore>(), App.Services.GetRequiredService<ICredentialStore>()) { }

        public SettingsPage(IDataService data, ITicketStore store, ICredentialStore credentialStore)
        {
            _data = data;
            _store = store;
            _credentialStore = credentialStore;
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_subscribed) return;
            _subscribed = true;
            _loadVersion++;
            _data.DataUpdated += UpdateCacheInfo;
            UpdateCacheInfo();
            UpdateApiStatus();
            UpdateBiliUidStatus();
            UpdateLlmStatus();
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            _subscribed = false;
            _loadVersion++;
            HidePopup();
            _data.DataUpdated -= UpdateCacheInfo;
        }

        private void UpdateApiStatus()
        {
            var apiKey = _store.LoadApiKey();
            var model = _store.LoadModel();
            ApiStatusText.Text = string.IsNullOrEmpty(apiKey)
                ? "未配置"
                : $"已配置 · 模型: {model ?? "（API默认）"}";
        }

        private void UpdateLlmStatus()
        {
            var key = _store.LoadLlmApiKey();
            var model = _store.LoadLlmModel();
            var baseUrl = _store.LoadLlmBaseUrl();
            if (string.IsNullOrWhiteSpace(key))
            {
                LlmStatusText.Text = "未配置";
                return;
            }
            var provider = string.IsNullOrWhiteSpace(baseUrl)
                ? "未配置"
                : LlmProviderPresets.GuessPresetIdByBaseUrl(baseUrl) is { } pid && pid != "custom"
                    ? LlmProviderPresets.FindById(pid)?.DisplayName ?? baseUrl
                    : baseUrl!;
            LlmStatusText.Text = $"已配置 · {provider} · 模型: {model ?? "（未选）"}";
        }

        private void LlmSetting_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LlmApiConfigDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
                UpdateLlmStatus();
        }

        private void UpdateBiliUidStatus()
        {
            var uid = _credentialStore.LoadBiliUid();
            BiliUidStatus.Text = string.IsNullOrEmpty(uid) ? "未设置" : $"当前: {uid}";
        }

        private void BiliUidSetting_Click(object sender, RoutedEventArgs e)
        {
            var currentUid = _credentialStore.LoadBiliUid() ?? "";
            var dialog = new BiliUidDialog(currentUid)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
            {
                _credentialStore.SaveBiliUid(dialog.BiliUid);
                UpdateBiliUidStatus();
            }
        }

        private void ApiSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ApiConfigDialog(
                _store.LoadApiKey() ?? "",
                _store.LoadModel() ?? "",
                _store.LoadWsId(),
                _store.LoadApiHost());
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                _store.SaveApiKey(dialog.ApiKey, dialog.Model, dialog.WorkspaceId, dialog.ApiHost);
                UpdateApiStatus();
            }
        }

        private void UpdateCacheInfo()
        {
            int version = _loadVersion;
            Dispatcher.InvokeAsync(() =>
            {
                if (!_subscribed || version != _loadVersion) return;
                int count = _data.GetRecordCount();
                bool hasLocalFile = _data.HasLocalFile;
                CacheInfo.Text = hasLocalFile
                    ? $"已缓存 {count} 期"
                    : $"无缓存文件，已使用内嵌数据 {count} 期";
            });
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!BtnUpdate.IsEnabled) return;
            BtnUpdate.IsEnabled = false;
            ShowSimplePopup("⏳", "正在检查网络...");
            int popupVersion = _popupVersion;

            try
            {
                int result = await _data.TryUpdateAsync();
                if (popupVersion != _popupVersion) return;

            if (result > 0)
            {
                int total = _data.GetRecordCount();
                ShowSimplePopup("✅", $"更新成功！新增 {result} 期数据，当前共 {total} 期");
                popupVersion = _popupVersion;
                UpdateCacheInfo();
                await Task.Delay(2000);
            }
            else if (result == 0)
            {
                ShowSimplePopup("📭", "数据已是最新，无需更新");
                popupVersion = _popupVersion;
                UpdateCacheInfo();
                await Task.Delay(1200);
            }
            else
            {
                string detail = _data.LastErrorMessage ?? "";
                string msg = result switch
                {
                    -1 => "网络不可达，请检查网络连接",
                    -2 => "数据源返回状态异常",
                    -3 => "数据源返回空数据",
                    -4 => "本地数据读取失败",
                    -5 => "远程数据格式解析失败",
                    -6 => $"连接超时：{detail}",
                    -7 => $"HTTP 请求异常：{detail}",
                    -11 => $"本地保存失败：{detail}",
                    _   => $"未知错误 ({result})：{detail}"
                };
                ShowSimplePopup("⚠️", msg);
                popupVersion = _popupVersion;
                _data.ResetCache();
                _data.NotifyDataUpdated();
                await Task.Delay(3000);
            }

            }
            catch (Exception ex)
            {
                if (popupVersion != _popupVersion) return;
                ShowSimplePopup("⚠️", $"更新失败：{ex.Message}");
                popupVersion = _popupVersion;
                await Task.Delay(3000);
            }
            finally
            {
                if (popupVersion == _popupVersion) HidePopup();
                BtnUpdate.IsEnabled = true;
            }
        }

        private void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            if (!_data.HasLocalFile)
            {
                ShowSimplePopup("📭", "无缓存文件");
                return;
            }

            int count = _data.GetRecordCount();
            if (count == 0) return;

            _confirmingClear = true;
            _popupVersion++;
            PopupIcon.Text = "⚠️";
            PopupText.Text = $"确认清除全部 {count} 期本地数据文件？\n删除后将自动使用内嵌数据，\n也可重新更新数据获取。";
            PopupActions.Visibility = Visibility.Visible;
            PopupOverlay.Visibility = Visibility.Visible;
        }

        private void PopupConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!_confirmingClear) return;
            _confirmingClear = false;
            PopupActions.Visibility = Visibility.Collapsed;

            _data.ClearAllData();
            if (_data.HasLocalFile)
            {
                PopupIcon.Text = "鈿狅笍";
                PopupText.Text = "删除本地数据失败，请检查安装目录权限";
                return;
            }
            _data.NotifyDataUpdated();
            UpdateCacheInfo();

            PopupIcon.Text = "🗑️";
            PopupText.Text = "本地数据文件已删除，自动使用内嵌数据";
        }

        private void PopupOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _confirmingClear = false;
            PopupActions.Visibility = Visibility.Collapsed;
            HidePopup();
        }

        private void PopupCancel_Click(object sender, RoutedEventArgs e)
        {
            _confirmingClear = false;
            PopupActions.Visibility = Visibility.Collapsed;
            HidePopup();
        }

        private void ShowSimplePopup(string icon, string text)
        {
            _popupVersion++;
            PopupActions.Visibility = Visibility.Collapsed;
            PopupIcon.Text = icon;
            PopupText.Text = text;
            PopupOverlay.Visibility = Visibility.Visible;
        }

        private void HidePopup()
        {
            _popupVersion++;
            PopupOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
