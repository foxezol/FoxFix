using System.Globalization;
using System.Resources;

namespace FoxFix.Core;

/// <summary>Typed keys for Resources/Strings.resx. All user-facing text is Turkish and lives there.</summary>
public static class Strings
{
    public const string AdapterTitle = nameof(AdapterTitle);
    public const string AdapterNoneFound = nameof(AdapterNoneFound);
    public const string AdapterNoneUp = nameof(AdapterNoneUp);
    public const string AdapterOk = nameof(AdapterOk);

    public const string IpConfigTitle = nameof(IpConfigTitle);
    public const string IpConfigSkipped = nameof(IpConfigSkipped);
    public const string IpConfigApipa = nameof(IpConfigApipa);
    public const string IpConfigNoAddress = nameof(IpConfigNoAddress);
    public const string IpConfigNoGateway = nameof(IpConfigNoGateway);
    public const string IpConfigOk = nameof(IpConfigOk);

    public const string PingTitle = nameof(PingTitle);
    public const string PingOk = nameof(PingOk);
    public const string PingGatewaySilent = nameof(PingGatewaySilent);
    public const string PingGatewayOnly = nameof(PingGatewayOnly);
    public const string PingNone = nameof(PingNone);

    public const string DnsTitle = nameof(DnsTitle);
    public const string DnsOk = nameof(DnsOk);
    public const string DnsLocalConfig = nameof(DnsLocalConfig);
    public const string DnsUdpIntercepted = nameof(DnsUdpIntercepted);
    public const string DnsUnreachable = nameof(DnsUnreachable);
    public const string DnsDetail = nameof(DnsDetail);
    public const string DnsRedirected = nameof(DnsRedirected);
    public const string Yes = nameof(Yes);
    public const string No = nameof(No);

    public const string HttpsTitle = nameof(HttpsTitle);
    public const string HttpsOk = nameof(HttpsOk);
    public const string HttpsSiteBlocked = nameof(HttpsSiteBlocked);
    public const string HttpsCertificate = nameof(HttpsCertificate);
    public const string HttpsBroken = nameof(HttpsBroken);
    public const string HttpsDetail = nameof(HttpsDetail);
    public const string HttpsOutcomeOk = nameof(HttpsOutcomeOk);
    public const string HttpsOutcomeTimeout = nameof(HttpsOutcomeTimeout);
    public const string HttpsOutcomeTls = nameof(HttpsOutcomeTls);
    public const string HttpsOutcomeCertificate = nameof(HttpsOutcomeCertificate);
    public const string HttpsOutcomeDns = nameof(HttpsOutcomeDns);
    public const string HttpsOutcomeOther = nameof(HttpsOutcomeOther);

    public const string RebootComment = nameof(RebootComment);
    public const string RebootScheduled = nameof(RebootScheduled);
    public const string RebootScheduleFailed = nameof(RebootScheduleFailed);
    public const string RebootCancelled = nameof(RebootCancelled);
    public const string RebootCancelFailed = nameof(RebootCancelFailed);

    public const string ResidueTitle = nameof(ResidueTitle);
    public const string ResidueNone = nameof(ResidueNone);
    public const string ResidueFound = nameof(ResidueFound);
    public const string ResidueDnsPoisoned = nameof(ResidueDnsPoisoned);

    public const string ToolGoodbyeDpi = nameof(ToolGoodbyeDpi);
    public const string ToolWinDivert = nameof(ToolWinDivert);
    public const string ToolZapret = nameof(ToolZapret);
    public const string ToolByeDpi = nameof(ToolByeDpi);
    public const string ToolProxiFyre = nameof(ToolProxiFyre);
    public const string ToolDnsCrypt = nameof(ToolDnsCrypt);

    public const string FindingActive = nameof(FindingActive);
    public const string FindingLeftover = nameof(FindingLeftover);
    public const string FindingPossibleSuffix = nameof(FindingPossibleSuffix);
    public const string FindingSplitWire = nameof(FindingSplitWire);
    public const string FindingDnsOrphan = nameof(FindingDnsOrphan);
    public const string FindingDnsProxy = nameof(FindingDnsProxy);
    public const string FindingConflict = nameof(FindingConflict);
    public const string FindingDnsRedirect = nameof(FindingDnsRedirect);

    public const string CleanupTitle = nameof(CleanupTitle);
    public const string PreviewNothing = nameof(PreviewNothing);
    public const string PreviewRemoveService = nameof(PreviewRemoveService);
    public const string PreviewKillProcess = nameof(PreviewKillProcess);
    public const string PreviewSkipNotSure = nameof(PreviewSkipNotSure);
    public const string PreviewSkipSplitWire = nameof(PreviewSkipSplitWire);
    public const string PreviewSkipOutOfScope = nameof(PreviewSkipOutOfScope);
    public const string PreviewSkipUnsafe = nameof(PreviewSkipUnsafe);
    public const string PreviewUndoNote = nameof(PreviewUndoNote);
    public const string PreviewReboot = nameof(PreviewReboot);
    public const string ResultNothing = nameof(ResultNothing);
    public const string ResultDone = nameof(ResultDone);
    public const string ResultPartial = nameof(ResultPartial);
    public const string ResultReboot = nameof(ResultReboot);
    public const string FailStop = nameof(FailStop);
    public const string FailDelete = nameof(FailDelete);
    public const string FailKill = nameof(FailKill);
    public const string UndoDescription = nameof(UndoDescription);
    public const string UndoCreateFailed = nameof(UndoCreateFailed);
    public const string UndoStartFailed = nameof(UndoStartFailed);
    public const string UndoRecordBroken = nameof(UndoRecordBroken);

    public const string DnsRepairTitle = nameof(DnsRepairTitle);
    public const string DnsRepairNothing = nameof(DnsRepairNothing);
    public const string DnsRepairProxyRunning = nameof(DnsRepairProxyRunning);
    public const string DnsRepairPreviewAdapter = nameof(DnsRepairPreviewAdapter);
    public const string DnsRepairPreviewFlush = nameof(DnsRepairPreviewFlush);
    public const string DnsRepairPreviewUndo = nameof(DnsRepairPreviewUndo);
    public const string DnsRepairResultNothing = nameof(DnsRepairResultNothing);
    public const string DnsRepairResultDone = nameof(DnsRepairResultDone);
    public const string DnsRepairResultPartial = nameof(DnsRepairResultPartial);
    public const string DnsRepairNoBackup = nameof(DnsRepairNoBackup);
    public const string DnsRepairSetFailed = nameof(DnsRepairSetFailed);
    public const string DnsRepairFlushFailed = nameof(DnsRepairFlushFailed);
    public const string DnsRepairUndoDescription = nameof(DnsRepairUndoDescription);
    public const string DnsRepairUndoFailed = nameof(DnsRepairUndoFailed);


    public const string ReportTitle = nameof(ReportTitle);
    public const string ReportCreated = nameof(ReportCreated);
    public const string ReportVersion = nameof(ReportVersion);
    public const string ReportWindows = nameof(ReportWindows);
    public const string ReportUser = nameof(ReportUser);
    public const string ReportMachine = nameof(ReportMachine);
    public const string ReportMasked = nameof(ReportMasked);
    public const string ReportUnmasked = nameof(ReportUnmasked);
    public const string ReportMaskUser = nameof(ReportMaskUser);
    public const string ReportMaskMachine = nameof(ReportMaskMachine);
    public const string ReportMaskAddress = nameof(ReportMaskAddress);
    public const string ReportSectionSummary = nameof(ReportSectionSummary);
    public const string ReportSectionChecks = nameof(ReportSectionChecks);
    public const string ReportSectionAdapters = nameof(ReportSectionAdapters);
    public const string ReportSectionFindings = nameof(ReportSectionFindings);
    public const string ReportSectionChanges = nameof(ReportSectionChanges);
    public const string ReportNoChecks = nameof(ReportNoChecks);
    public const string ReportNoFindings = nameof(ReportNoFindings);
    public const string ReportNoChanges = nameof(ReportNoChanges);
    public const string ReportNoAdapters = nameof(ReportNoAdapters);
    public const string ReportSummaryOk = nameof(ReportSummaryOk);
    public const string ReportSummaryNotes = nameof(ReportSummaryNotes);
    public const string ReportSummaryProblem = nameof(ReportSummaryProblem);
    public const string ReportAdapterUp = nameof(ReportAdapterUp);
    public const string ReportAdapterDown = nameof(ReportAdapterDown);
    public const string ReportMac = nameof(ReportMac);
    public const string ReportIPv4 = nameof(ReportIPv4);
    public const string ReportGateway = nameof(ReportGateway);
    public const string ReportDns = nameof(ReportDns);
    public const string ReportNone = nameof(ReportNone);
    public const string ReportFindingConfident = nameof(ReportFindingConfident);
    public const string ReportFindingPossible = nameof(ReportFindingPossible);
    public const string ReportFindingRemovable = nameof(ReportFindingRemovable);
    public const string ReportFindingReportOnly = nameof(ReportFindingReportOnly);
    public const string ReportFindingActive = nameof(ReportFindingActive);
    public const string ReportFindingInactive = nameof(ReportFindingInactive);
    public const string ReportCopied = nameof(ReportCopied);
    public const string ReportCopyFailed = nameof(ReportCopyFailed);
    public const string ReportSaved = nameof(ReportSaved);
    public const string ReportSaveFailed = nameof(ReportSaveFailed);
    public const string ReportBuildFailed = nameof(ReportBuildFailed);

    public const string StartupTitle = nameof(StartupTitle);
    public const string StartupNoChanges = nameof(StartupNoChanges);
    public const string StartupPreviewDisable = nameof(StartupPreviewDisable);
    public const string StartupPreviewEnable = nameof(StartupPreviewEnable);
    public const string StartupPreviewNote = nameof(StartupPreviewNote);
    public const string StartupResultDone = nameof(StartupResultDone);
    public const string StartupResultPartial = nameof(StartupResultPartial);
    public const string StartupFailed = nameof(StartupFailed);
    public const string StartupUndoDescription = nameof(StartupUndoDescription);
    public const string StartupNothingToDo = nameof(StartupNothingToDo);

    public const string CleanupDiskTitle = nameof(CleanupDiskTitle);
    public const string CleanupNothingSelected = nameof(CleanupNothingSelected);
    public const string CleanupPreviewItem = nameof(CleanupPreviewItem);
    public const string CleanupPreviewTotal = nameof(CleanupPreviewTotal);
    public const string CleanupPreviewIrreversible = nameof(CleanupPreviewIrreversible);
    public const string CleanupResultDone = nameof(CleanupResultDone);
    public const string CleanupResultSkipped = nameof(CleanupResultSkipped);
    public const string CleanupFailed = nameof(CleanupFailed);
    public const string CleanupUndoUnsupported = nameof(CleanupUndoUnsupported);

    public const string TargetUserTemp = nameof(TargetUserTemp);
    public const string TargetUserTempInfo = nameof(TargetUserTempInfo);
    public const string TargetWindowsTemp = nameof(TargetWindowsTemp);
    public const string TargetWindowsTempInfo = nameof(TargetWindowsTempInfo);
    public const string TargetUpdateCache = nameof(TargetUpdateCache);
    public const string TargetUpdateCacheInfo = nameof(TargetUpdateCacheInfo);
    public const string TargetBrowserCache = nameof(TargetBrowserCache);
    public const string TargetBrowserCacheInfo = nameof(TargetBrowserCacheInfo);
    public const string TargetRecycleBin = nameof(TargetRecycleBin);
    public const string TargetRecycleBinInfo = nameof(TargetRecycleBinInfo);

    public const string SpeedIdle = nameof(SpeedIdle);
    public const string SpeedLatencyPhase = nameof(SpeedLatencyPhase);
    public const string SpeedDownloadPhase = nameof(SpeedDownloadPhase);
    public const string SpeedUploadPhase = nameof(SpeedUploadPhase);
    public const string SpeedLive = nameof(SpeedLive);
    public const string SpeedDone = nameof(SpeedDone);
    public const string SpeedCancelled = nameof(SpeedCancelled);
    public const string SpeedFailed = nameof(SpeedFailed);
    public const string SpeedMbps = nameof(SpeedMbps);
    public const string SpeedMs = nameof(SpeedMs);

    public const string StatusOkLabel = nameof(StatusOkLabel);
    public const string StatusWarnLabel = nameof(StatusWarnLabel);
    public const string StatusBadLabel = nameof(StatusBadLabel);
    public const string StatusSkippedLabel = nameof(StatusSkippedLabel);

    public const string VerdictChecking = nameof(VerdictChecking);
    public const string VerdictCheckingDetail = nameof(VerdictCheckingDetail);
    public const string VerdictOk = nameof(VerdictOk);
    public const string VerdictOkDetail = nameof(VerdictOkDetail);
    public const string VerdictAttention = nameof(VerdictAttention);
    public const string VerdictAttentionDetail = nameof(VerdictAttentionDetail);
    public const string VerdictProblem = nameof(VerdictProblem);
    public const string VerdictProblemDetail = nameof(VerdictProblemDetail);
    public const string VerdictFailed = nameof(VerdictFailed);

    public const string FlowApplyLabel = nameof(FlowApplyLabel);
    public const string FlowApplyElevatedLabel = nameof(FlowApplyElevatedLabel);
    public const string FlowRestarting = nameof(FlowRestarting);
    public const string FlowElevationDenied = nameof(FlowElevationDenied);
    public const string FlowPreviewFailed = nameof(FlowPreviewFailed);
    public const string FlowApplyFailed = nameof(FlowApplyFailed);
    public const string FlowUndone = nameof(FlowUndone);
    public const string FlowUndoFailed = nameof(FlowUndoFailed);
    public const string FlowUndoUnknownFix = nameof(FlowUndoUnknownFix);
    public const string FlowNeedsAdmin = nameof(FlowNeedsAdmin);

    public const string UninstallTitle = nameof(UninstallTitle);
    public const string UninstallPreviewDpi = nameof(UninstallPreviewDpi);
    public const string UninstallPreviewTask = nameof(UninstallPreviewTask);
    public const string UninstallPreviewData = nameof(UninstallPreviewData);
    public const string UninstallPreviewInstalled = nameof(UninstallPreviewInstalled);
    public const string UninstallPreviewPortable = nameof(UninstallPreviewPortable);
    public const string UninstallPreviewManual = nameof(UninstallPreviewManual);
    public const string UninstallIrreversible = nameof(UninstallIrreversible);
    public const string UninstallResultInstalled = nameof(UninstallResultInstalled);
    public const string UninstallResultClosing = nameof(UninstallResultClosing);
    public const string UninstallResultManual = nameof(UninstallResultManual);
    public const string FlowResultOk = nameof(FlowResultOk);
    public const string FlowResultFailed = nameof(FlowResultFailed);
    public const string FlowBusyText = nameof(FlowBusyText);
    public const string DpiProfile1 = nameof(DpiProfile1);
    public const string DpiProfile2 = nameof(DpiProfile2);
    public const string DpiProfile3 = nameof(DpiProfile3);
    public const string DpiProfile4 = nameof(DpiProfile4);
    public const string DpiProfile5 = nameof(DpiProfile5);
    public const string DpiProfile6 = nameof(DpiProfile6);
    public const string DpiProfile7 = nameof(DpiProfile7);
    public const string GdpiInstallTitle = nameof(GdpiInstallTitle);
    public const string GdpiPickFirst = nameof(GdpiPickFirst);
    public const string GdpiBlockedForeign = nameof(GdpiBlockedForeign);
    public const string GdpiBlockedManaged = nameof(GdpiBlockedManaged);
    public const string GdpiPreviewProfile = nameof(GdpiPreviewProfile);
    public const string GdpiPreviewFiles = nameof(GdpiPreviewFiles);
    public const string GdpiPreviewService = nameof(GdpiPreviewService);
    public const string GdpiPreviewDns = nameof(GdpiPreviewDns);
    public const string GdpiPreviewReplace = nameof(GdpiPreviewReplace);
    public const string GdpiPreviewDriver = nameof(GdpiPreviewDriver);
    public const string GdpiFilesBroken = nameof(GdpiFilesBroken);
    public const string GdpiServiceFailed = nameof(GdpiServiceFailed);
    public const string GdpiServiceDisplayName = nameof(GdpiServiceDisplayName);
    public const string GdpiResultService = nameof(GdpiResultService);
    public const string GdpiUndoDescription = nameof(GdpiUndoDescription);
    public const string TrialNotNeeded = nameof(TrialNotNeeded);
    public const string TrialFound = nameof(TrialFound);
    public const string TrialNothingHelped = nameof(TrialNothingHelped);
    public const string TrialCouldNotStart = nameof(TrialCouldNotStart);
    public const string DnsOkManagedRedirect = nameof(DnsOkManagedRedirect);

    public const string DnsResetTitle = nameof(DnsResetTitle);
    public const string DnsResetNothing = nameof(DnsResetNothing);
    public const string DnsResetPreviewAdapter = nameof(DnsResetPreviewAdapter);
    public const string GdpiRemoveTitle = nameof(GdpiRemoveTitle);
    public const string GdpiRemovePreview = nameof(GdpiRemovePreview);
    public const string GdpiRemoveNothing = nameof(GdpiRemoveNothing);
    public const string GdpiRemoveDone = nameof(GdpiRemoveDone);
    public const string GdpiRemovePartial = nameof(GdpiRemovePartial);
    public const string GdpiRemoveUndoUnsupported = nameof(GdpiRemoveUndoUnsupported);
    public const string FoxDpiAlreadyRunning = nameof(FoxDpiAlreadyRunning);
    public const string FoxDpiStartFailed = nameof(FoxDpiStartFailed);
    public const string FoxDpiStarted = nameof(FoxDpiStarted);
    public const string FoxDpiNotRunning = nameof(FoxDpiNotRunning);

    public const string ReportDnsStatic = nameof(ReportDnsStatic);
    public const string ReportDnsAutomatic = nameof(ReportDnsAutomatic);
    public const string CleanupProgress = nameof(CleanupProgress);

    public const string FoxDpiNeedsFoxFix = nameof(FoxDpiNeedsFoxFix);
    public const string FoxDpiExplainTitle = nameof(FoxDpiExplainTitle);
    public const string FoxDpiExplain = nameof(FoxDpiExplain);

    public const string AutoStartTitle = nameof(AutoStartTitle);
    public const string AutoStartAlreadyOn = nameof(AutoStartAlreadyOn);
    public const string AutoStartAlreadyOff = nameof(AutoStartAlreadyOff);
    public const string AutoStartNoPath = nameof(AutoStartNoPath);
    public const string AutoStartPreviewOn = nameof(AutoStartPreviewOn);
    public const string AutoStartPreviewPath = nameof(AutoStartPreviewPath);
    public const string AutoStartPreviewOff = nameof(AutoStartPreviewOff);
    public const string AutoStartResultOn = nameof(AutoStartResultOn);
    public const string AutoStartResultOff = nameof(AutoStartResultOff);
    public const string AutoStartFailed = nameof(AutoStartFailed);
    public const string AutoStartUndoOn = nameof(AutoStartUndoOn);
    public const string AutoStartUndoOff = nameof(AutoStartUndoOff);

    public const string EngineError = nameof(EngineError);

    private static readonly ResourceManager Manager = new("FoxFix.Core.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key)
    {
        return Manager.GetString(key, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Eksik metin kaynağı: {key}");
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }
}
