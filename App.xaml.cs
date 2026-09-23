using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SsqAnalyzer.Services;
using SsqAnalyzer.Services.Kill;

namespace SsqAnalyzer
{
    public partial class App : Application
    {
        private static readonly string LogPath = AppPaths.CrashLogFile;

        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 配置 DI 容器
            var services = new ServiceCollection();
            services.AddSingleton<IDataService, DataService>();
            services.AddSingleton<ITicketStore, TicketStore>();
            services.AddSingleton<ITicketRepository>(sp => sp.GetRequiredService<ITicketStore>());
            services.AddSingleton<ICredentialStore>(sp => sp.GetRequiredService<ITicketStore>());
            services.AddSingleton<ITicketParser>(sp => sp.GetRequiredService<ITicketStore>());
            services.AddTransient<IAnalysisService, AnalysisService>();
            services.AddSingleton<IPositionValidationStore, PositionValidationStore>();
            services.AddSingleton<IPositionPredictor, PositionPredictor>();
            services.AddSingleton<PositionExperimentCoordinator>();
            services.AddSingleton<GroupInputStore>();
            services.AddTransient<GroupService>();
            services.AddSingleton<BiliService>();
            services.AddSingleton<IVideoSearchService>(sp => sp.GetRequiredService<BiliService>());
            services.AddSingleton<DownloadService>();
            services.AddSingleton<AiAnalysisService>();
            // 杀号引擎子系统（架构设计 §1.3 / §6 DI 注册）
            services.AddSingleton<IKillSettings, KillSettings>();   // 全局门槛（统一 80%）
            services.AddSingleton<IRuleRepository, RuleRepository>();
            services.AddSingleton<IRuleExecutor, JintRuleExecutor>();
            services.AddSingleton<IRuleContextBuilder, RuleContextBuilder>();
            services.AddSingleton<INlToCodeService, NlToCodeService>();   // NL→Code 生成
            services.AddTransient<IKillEngine, KillEngine>();
            services.AddTransient<IBacktestEngine, BacktestEngine>();
            services.AddSingleton<KillForwardStore>();
            services.AddSingleton<KillForwardCoordinator>();
            Services = services.BuildServiceProvider();

            // 全局异常处理
            DispatcherUnhandledException += (_, args) =>
            {
                LogCrash("UI", args.Exception);
                MessageBox.Show($"程序遇到意外错误，已记录到 crash.log\n{args.Exception.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                LogCrash("AppDomain", args.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogCrash("Task", args.Exception);
                args.SetObserved();
            };

            base.OnStartup(e);

            if (new PositionCommandLineHandler(Services, Shutdown).TryHandle(e.Args))
                return;

            // ⚡ StartupUri 已移除，手动创建 MainWindow 并注入 DI 服务
            Services.GetRequiredService<PositionExperimentCoordinator>().EnsureCurrentPredictions();
            Services.GetRequiredService<KillForwardCoordinator>().Start();
            var mainWindow = new MainWindow(Services.GetRequiredService<IDataService>());
            mainWindow.Show();
        }

        private static void LogCrash(string source, Exception? ex)
        {
            try
            {
                AppPaths.EnsureRoot();
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n");
            }
            catch (Exception innerEx)
            {
                Debug.WriteLine($"[App.LogCrash] 日志写入失败: {innerEx.Message}");
            }
        }
    }
}
