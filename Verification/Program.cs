if (args.FirstOrDefault() == "--tune-alternation-gap")
    KillConditionTuning.TuneGap(args[1], args[2]);
else if (args.FirstOrDefault() == "--tune-rule-conditions")
    KillConditionTuning.Run(args[1], args[2], args.ElementAtOrDefault(3), args.Length > 4 ? int.Parse(args[4]) : 0);
else if (args.FirstOrDefault() == "--rule-revision-refresh")
    KillRuleRevisionAudit.Refresh();
else if (args.FirstOrDefault() == "--rule-revision-audit")
    KillRuleRevisionAudit.Run(args[1], args[2]);
else if (args.FirstOrDefault() == "--evaluation-check")
    VerificationSuite.VerifyKillEvaluation();
else if (args.FirstOrDefault() == "--backtest-check")
    VerificationSuite.VerifyBacktestCorrectness();
else if (args.FirstOrDefault() == "--page-check")
    VerificationSuite.VerifyPageLifecycle();
else if (args.FirstOrDefault() == "--benchmark")
    PerformanceMeasurements.Run(args.ElementAtOrDefault(1));
else if (args.FirstOrDefault() == "--trend-benchmark")
    TrendPerformanceMeasurements.Run(args.ElementAtOrDefault(1) ?? "tmp/trend-benchmark");
else if (args.FirstOrDefault() == "--data-benchmark")
    DataParsingVerification.Measure(args.ElementAtOrDefault(1) ?? "tmp/data-benchmark.json");
else
    VerificationSuite.Run();
