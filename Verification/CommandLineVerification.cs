using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SsqAnalyzer;
using SsqAnalyzer.Models;
using SsqAnalyzer.Services;

internal static partial class VerificationSuite
{
    private static void VerifyCommandLineCompatibility()
    {
        WithTestDirectory(testRoot =>
        {
            var data = new FakeDataService();
            data.SetRecords(BuildRecords(40).ToArray());
            var predictor = new CommandLinePredictorProbe(new PositionPredictor(data));
            var store = new PositionValidationStore(data, Path.Combine(testRoot, "ledger.json"), subscribeToUpdates: false);
            using var services = new ServiceCollection()
                .AddSingleton<IDataService>(data)
                .AddSingleton<IPositionPredictor>(predictor)
                .AddSingleton<IPositionValidationStore>(store)
                .BuildServiceProvider();
            var exits = new List<int>();
            var output = new List<JsonNode>();
            var handler = new PositionCommandLineHandler(services, exits.Add,
                value => output.Add(JsonSerializer.SerializeToNode(value)!));

            foreach (var arguments in new[]
            {
                Array.Empty<string>(), new[] { "--unknown" },
                new[] { " --position-backtest" }, new[] { "--unknown", "--position-backtest" }
            })
            {
                Assert(!handler.TryHandle(arguments), "unknown command keeps GUI startup");
                Assert(exits.Count == 0 && output.Count == 0, "unknown command has no command side effects");
            }

            foreach (var test in new[]
            {
                (Args: new[] { "--POSITION-BACKTEST" }, Expected: 200),
                (Args: new[] { "--position-backtest", "not-a-number" }, Expected: 200),
                (Args: new[] { "--position-backtest", "30", "ignored" }, Expected: 30),
                (Args: new[] { "--position-backtest", "-1" }, Expected: -1)
            })
            {
                exits.Clear();
                output.Clear();
                Assert(handler.TryHandle(test.Args), "backtest command recognized");
                Assert(predictor.SampleSize == test.Expected && predictor.Offset == 0,
                    "backtest argument parsing and defaults preserved");
                Assert(exits.SequenceEqual(new[] { 0 }), "backtest success exits once with zero");
                Assert(output.Count == 1 && JsonNode.DeepEquals(output[0], JsonSerializer.SerializeToNode(predictor.Report)),
                    "backtest JSON payload remains the report itself");
            }

            exits.Clear();
            output.Clear();
            bool missingExportPath = false;
            try { handler.TryHandle(new[] { "--position-export-all" }); }
            catch (ArgumentException exception)
            {
                missingExportPath = exception.Message == "--position-export-all 需要指定输出xlsx路径";
            }
            Assert(missingExportPath && exits.Count == 0 && output.Count == 0,
                "missing export path retains the original exception boundary");

            CheckResult(new[] { "--position-compare-versions" }, 2);
            Assert(output[0]["Error"]!.GetValue<string>() == "缺少可比较的主版本或影子版本前向记录",
                "missing versions retain error text");
            Assert(output[0]["FilePath"]!.GetValue<string>() == store.FilePath, "error includes ledger path");

            string absentLedger = Path.Combine(testRoot, "absent.json");
            CheckResult(new[] { "--POSITION-AUDIT-LEDGER", absentLedger }, 0);
            Assert(output[0]["Valid"]!.GetValue<bool>() && output[0]["FilePath"]!.GetValue<string>() == absentLedger,
                "audit honors explicit path and case-insensitive command");
            Assert(!File.Exists(absentLedger), "audit does not create a missing ledger");

            string corruptLedger = Path.Combine(testRoot, "corrupt.json");
            File.WriteAllText(corruptLedger, "not-json");
            CheckResult(new[] { "--position-audit-ledger", corruptLedger }, 2);
            Assert(!output[0]["Valid"]!.GetValue<bool>() && output[0]["Error"] is not null,
                "audit error JSON and exit code");
            Assert(File.ReadAllText(corruptLedger) == "not-json", "audit preserves corrupt input");

            CheckResult(new[] { "--position-reconcile" }, 0);
            Assert(output[0]["Settled"]!.GetValue<int>() == 0 && output[0]["Summaries"] is JsonArray,
                "reconcile output shape");
            CheckResult(new[] { "--position-forward-status" }, 0);
            Assert(output[0]["Summary"] is not null && output[0]["AllSummaries"] is JsonArray,
                "forward status output shape");

            void CheckResult(string[] arguments, int expectedExit)
            {
                exits.Clear();
                output.Clear();
                Assert(handler.TryHandle(arguments), "command recognized");
                Assert(exits.SequenceEqual(new[] { expectedExit }) && output.Count == 1,
                    "command emits one JSON result and one exit code");
            }
        });
    }

    private sealed class CommandLinePredictorProbe : IPositionPredictor
    {
        private readonly IPositionPredictor _inner;
        public CommandLinePredictorProbe(IPositionPredictor inner)
        {
            _inner = inner;
            Report = inner.Backtest(30);
        }
        public PositionBacktestReport Report { get; }
        public int SampleSize { get; private set; }
        public int Offset { get; private set; }
        public string RuleVersionId => _inner.RuleVersionId;
        public IReadOnlyList<int> GetIssueOptions() => _inner.GetIssueOptions();
        public PositionPrediction Predict(int issue, string runMode = "live") => _inner.Predict(issue, runMode);
        public PositionBacktestReport Backtest(int sampleSize = 200, int offsetFromLatest = 0,
            CancellationToken cancellationToken = default)
        {
            SampleSize = sampleSize;
            Offset = offsetFromLatest;
            return Report;
        }
    }
}
