﻿using PnP.Framework.Modernization.Entities;
using PnP.Framework.Modernization.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Modernization.Telemetry.Observers
{
    /// <summary>
    /// Markdown observer intended for end-user output
    /// </summary>
    public class MarkdownObserver : ILogObserver
    {

        // Cache the logs between calls
        private static readonly Lazy<List<Tuple<LogLevel, LogEntry>>> _lazyLogInstance = new Lazy<List<Tuple<LogLevel, LogEntry>>>(() => new List<Tuple<LogLevel, LogEntry>>());
        protected bool _includeDebugEntries;
        protected bool _includeVerbose;
        protected DateTime _reportDate;
        protected string _reportFileName = "";
        protected string _reportFolder = Environment.CurrentDirectory;
        protected string _pageBeingTransformed;

        #region Construction
        /// <summary>
        /// Constructor for specifying to include debug entries
        /// </summary>
        /// <param name="fileName">Name used to construct the log file name</param>
        /// <param name="folder">Folder that will hold the log file</param>
        /// <param name="includeDebugEntries">Include Debug Log Entries</param>
        /// <param name="includeVerbose">Include verbose details</param>
        public MarkdownObserver(string fileName = "", string folder = "", bool includeDebugEntries = false, bool includeVerbose = false)
        {
            _includeDebugEntries = includeDebugEntries;
            _includeVerbose = includeVerbose;
            _reportDate = DateTime.Now;

            if (!string.IsNullOrEmpty(folder))
            {
                _reportFolder = folder;
            }

            // Drop possible file extension as we want to ensure we have a .md extension
            _reportFileName = System.IO.Path.GetFileNameWithoutExtension(fileName);

#if DEBUG && MEASURE && MEASURE
           _includeDebugEntries = true; //Override for debugging locally
#endif
        }
        #endregion

        #region Markdown Tokens
        private const string Heading1 = "#";
        private const string Heading2 = "##";
        private const string Heading3 = "###";
        private const string Heading4 = "####";
        private const string Heading5 = "#####";
        private const string Heading6 = "######";
        private const string UnorderedListItem = "-";
        private const string Italic = "_";
        private const string Bold = "**";
        private const string BlockQuotes = "> ";
        private const string TableHeaderColumn = "-------------";
        private const string TableColumnSeperator = " | ";
        private const string Link = "[{0}]({1})";
        #endregion

        /// <summary>
        /// Get the single List&lt;LogEntry&gt; instance, singleton pattern
        /// </summary>
        public static List<Tuple<LogLevel, LogEntry>> Logs
        {
            get
            {
                return _lazyLogInstance.Value;
            }
        }

        /// <summary>
        /// Debug level of data not recorded unless in debug mode
        /// </summary>
        /// <param name="entry"></param>
        public void Debug(LogEntry entry)
        {
            if (_includeDebugEntries)
            {
                entry.PageId = this._pageBeingTransformed;
                Logs.Add(new Tuple<LogLevel, LogEntry>(LogLevel.Debug, entry));
            }
        }

        /// <summary>
        /// Errors 
        /// </summary>
        /// <param name="entry"></param>
        public void Error(LogEntry entry)
        {
            entry.PageId = this._pageBeingTransformed;
            Logs.Add(new Tuple<LogLevel, LogEntry>(LogLevel.Error, entry));
        }

        /// <summary>
        /// Reporting operations throughout the transform process
        /// </summary>
        /// <param name="entry"></param>
        public void Info(LogEntry entry)
        {
            entry.PageId = this._pageBeingTransformed;
            Logs.Add(new Tuple<LogLevel, LogEntry>(LogLevel.Information, entry));
        }

        /// <summary>
        /// Report on any warnings generated by the reporting tool
        /// </summary>
        /// <param name="entry"></param>
        public void Warning(LogEntry entry)
        {
            entry.PageId = this._pageBeingTransformed;
            Logs.Add(new Tuple<LogLevel, LogEntry>(LogLevel.Warning, entry));
        }

        /// <summary>
        /// Sets the id of the page that's being transformed
        /// </summary>
        /// <param name="pageId">Id of the page</param>
        public void SetPageId(string pageId)
        {
            this._pageBeingTransformed = pageId;
        }


        /// <summary>
        /// Generates a markdown based report based on the logs
        /// </summary>
        /// <returns></returns>
        protected virtual string GenerateReportWithSummaryAtTop(bool includeHeading = true)
        {
            StringBuilder report = new StringBuilder();
            List<TransformationLogAnalysis> summaries = new List<TransformationLogAnalysis>();

            // Get one log entry per page...assumes that this log entry is included by each transformator
            var distinctLogs = Logs.Where(p => p.Item2.Heading == LogStrings.Heading_Summary && p.Item2.Significance == LogEntrySignificance.SourceSiteUrl); //TODO: Need to improve this

            GenerateReportWithSummaryAtTopDetails(report, distinctLogs);

            return report.ToString();
        }

        protected virtual string GenerateReportWithSummaryAtTopDetails(StringBuilder report, IEnumerable<Tuple<LogLevel, LogEntry>> distinctLogs)
        {
            List<TransformationLogAnalysis> logFileAnalysis = new List<TransformationLogAnalysis>();

            // Loop over each page
            foreach (var distinctLogEntry in distinctLogs)
            {
                // Get data for the given page
                logFileAnalysis.Add(AnalyseLogsForReport(Logs, distinctLogEntry.Item2.PageId));
            }

            if (logFileAnalysis.Count == 0)
            {
                return "";
            }

            // Start with summary table
            report.AppendLine($"{Heading1} {LogStrings.Report_ModernisationSummaryReport}");
            report.AppendLine();
            report.AppendLine($"Date {TableColumnSeperator} Duration {TableColumnSeperator} Source Page {TableColumnSeperator} Target Page Url {TableColumnSeperator} Status");
            report.AppendLine($"{TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn}");

            foreach (var modernizedFileLog in logFileAnalysis)
            {
                // Gather details
                string status = "";
                var duration = modernizedFileLog.TransformationDuration;
                var durationResult = string.Format("{0:D2}:{1:D2}:{2:D2}", duration.Hours, duration.Minutes, duration.Seconds); ;
                var logErrorCount = modernizedFileLog.Errors.Count;
                var logWarningsCount = modernizedFileLog.Warnings.Count;

                if (modernizedFileLog.CriticalErrors.Any())
                {
                    status = LogStrings.Report_TransformFail;
                }
                else
                {
                    status = (logWarningsCount > 0 || logErrorCount > 0) ? string.Format(LogStrings.Report_TransformSuccessWithIssues, logWarningsCount, logErrorCount)
                        : LogStrings.Report_TransformSuccess;
                }

                var reportSrcPageUrl = modernizedFileLog.SourcePage.PrependIfNotNull(modernizedFileLog.BaseSourceUrl);
                var reportTgtPageUrl = modernizedFileLog.TargetPage.PrependIfNotNull(modernizedFileLog.BaseTargetUrl);
                var reportSrcPageTitle = modernizedFileLog.SourcePage.StripRelativeUrlSectionString();
                var reportTgtPageTitle = modernizedFileLog.TargetPage.StripRelativeUrlSectionString();
                var transformPageStartDate = modernizedFileLog.PageLogsOrdered.FirstOrDefault();

                if (reportSrcPageUrl != null)
                {
                    reportSrcPageUrl = Uri.EscapeUriString(reportSrcPageUrl);
                }
                if (reportTgtPageUrl != null)
                {
                    reportTgtPageUrl = Uri.EscapeUriString(reportTgtPageUrl);
                }

                report.AppendLine($"{transformPageStartDate?.Item2.EntryTime} {TableColumnSeperator} {durationResult} {TableColumnSeperator} [{reportSrcPageTitle}]({reportSrcPageUrl}) {TableColumnSeperator} [{reportTgtPageTitle}]({reportTgtPageUrl}) {TableColumnSeperator} {status}");
            }

            // Add warning and error summary (if any)
            GenerateIssueSummaryReport(report, logFileAnalysis);

            // Conclude with details per page
            if (_includeVerbose)
            {
                report.AppendLine($"{Heading1} {LogStrings.Report_ModernisationPageDetails}");
                foreach (var modernizedFileLog in logFileAnalysis)
                {
                    #region Transform Overview
                    report.AppendLine($"{Heading2} {LogStrings.Report_TransformationDetails}: {modernizedFileLog.SourcePage.StripRelativeUrlSectionString()}");
                    report.AppendLine();
                    report.AppendLine($"{UnorderedListItem} {LogStrings.Report_ReportDate}: {modernizedFileLog.ReportDate}");
                    report.AppendLine($"{UnorderedListItem} {LogStrings.Report_TransformDuration}: {string.Format("{0:D2}:{1:D2}:{2:D2}", modernizedFileLog.TransformationDuration.Hours, modernizedFileLog.TransformationDuration.Minutes, modernizedFileLog.TransformationDuration.Seconds)}");

                    foreach (var log in modernizedFileLog.TransformationVerboseSummary)
                    {
                        var signifcance = "";
                        switch (log.Item2.Significance)
                        {
                            case LogEntrySignificance.AssetTransferred:
                                signifcance = LogStrings.AssetTransferredToUrl;
                                break;
                            case LogEntrySignificance.SourcePage:
                                signifcance = LogStrings.TransformingPage;
                                break;
                            case LogEntrySignificance.SourceSiteUrl:
                                signifcance = LogStrings.TransformingSite;
                                break;
                            case LogEntrySignificance.TargetPage:
                                signifcance = LogStrings.TransformedPage;
                                break;
                            case LogEntrySignificance.TargetSiteUrl:
                                signifcance = LogStrings.CrossSiteTransferToSite;
                                break;
                            case LogEntrySignificance.SharePointVersion:
                                signifcance = LogStrings.SourceSharePointVersion;
                                break;
                            case LogEntrySignificance.TransformMode:
                                signifcance = LogStrings.TransformMode;
                                break;
                            case LogEntrySignificance.WebServiceFallback:
                                signifcance = LogStrings.TransformFallback;
                                break;
                        }

                        report.AppendLine($"{UnorderedListItem} {signifcance} {log.Item2.Message}");
                    }

                    #endregion

                    #region Summary Page Transformation Information Settings
                    report.AppendLine();
                    report.AppendLine($"{Heading3} {LogStrings.Report_TransformationSettings}");
                    report.AppendLine();
                    report.AppendLine($"{LogStrings.Report_Property} {TableColumnSeperator} {LogStrings.Report_Settings}");
                    report.AppendLine($"{TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn}");

                    foreach (var setting in modernizedFileLog.TransformationSettings)
                    {
                        report.AppendLine($"{setting.Item1 ?? ""} {TableColumnSeperator} {setting.Item2 ?? LogStrings.Report_ValueNotSet}");
                    }
                    #endregion

                    #region Transformation Operation Details
                    report.AppendLine($"{Heading3} {LogStrings.Report_TransformDetails}");
                    report.AppendLine();

                    report.AppendLine(string.Format(LogStrings.Report_TransformDetailsTableHeader, TableColumnSeperator));
                    report.AppendLine($"{TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} ");

                    IEnumerable<Tuple<LogLevel, LogEntry>> filteredLogDetails = null;
                    if (_includeDebugEntries)
                    {
                        filteredLogDetails = modernizedFileLog.TransformationVerboseDetails.Where(l => l.Item1 == LogLevel.Debug ||
                                                                   l.Item1 == LogLevel.Information ||
                                                                   l.Item1 == LogLevel.Warning);
                    }
                    else
                    {
                        filteredLogDetails = modernizedFileLog.TransformationVerboseDetails.Where(l => l.Item1 == LogLevel.Information ||
                                                                   l.Item1 == LogLevel.Warning);
                    }

                    foreach (var log in filteredLogDetails)
                    {
                        switch (log.Item1)
                        {
                            case LogLevel.Information:
                                report.AppendLine($"{log.Item2.EntryTime} {TableColumnSeperator} {log.Item2.Heading} {TableColumnSeperator} {log.Item2.Message}");
                                break;
                            case LogLevel.Warning:
                                report.AppendLine($"{log.Item2.EntryTime} {TableColumnSeperator} {Bold}{log.Item2.Heading}{Bold} {TableColumnSeperator} {Bold}{log.Item2.Message}{Bold}");
                                break;
                            case LogLevel.Debug:
                                report.AppendLine($"{log.Item2.EntryTime} {TableColumnSeperator} {Italic}{log.Item2.Heading}{Italic} {TableColumnSeperator} {Italic}{log.Item2.Message}{Italic}");
                                break;
                        }
                    }

                    #endregion
                }
            }
            return report.ToString();
        }

        /// <summary>
        /// Generate a summary report
        /// </summary>
        /// <param name="report"></param>
        /// <param name="summaries"></param>
        private void GenerateIssueSummaryReport(StringBuilder report, List<TransformationLogAnalysis> summaries)
        {
            StringBuilder errorSummary = new StringBuilder();

            var anyErrors = summaries.Any(o => o.Errors.Any(e => e.Item2.IsCriticalException == false));
            var anyWarnings = summaries.Any(o => o.Warnings.Any());
            var anyCritical = summaries.Any(o => o.CriticalErrors.Any());

            if (anyWarnings)
            {
                report.AppendLine($"{Heading2} {LogStrings.Report_WarningsOccurred}");
                report.AppendLine();

                report.AppendLine(string.Format(LogStrings.Report_TransformIssuesTableHeader, TableColumnSeperator));
                report.AppendLine($"{TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn}");

                foreach (var summary in summaries)
                {
                    foreach (var log in summary.Warnings)
                    {
                        report.AppendLine($"{log.Item2.EntryTime} {TableColumnSeperator} {summary.SourcePage.StripRelativeUrlSectionString()} {TableColumnSeperator}  {log.Item2.Heading} {TableColumnSeperator} {log.Item2.Message}");
                    }
                }
            }

            if (anyErrors)
            {
                report.AppendLine($"{Heading2} {LogStrings.Report_ErrorsOccurred}");
                report.AppendLine();

                report.AppendLine(string.Format(LogStrings.Report_TransformIssuesTableHeader, TableColumnSeperator));
                report.AppendLine($"{TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn} {TableColumnSeperator} {TableHeaderColumn}");

                foreach (var summary in summaries)
                {
                    foreach (var log in summary.Errors)
                    {
                        report.AppendLine($"{log.Item2.EntryTime} {TableColumnSeperator} {summary.SourcePage.StripRelativeUrlSectionString()} {TableColumnSeperator}  {log.Item2.Heading} {TableColumnSeperator} {log.Item2.Message} {log.Item2.Exception?.StackTrace}");
                    }
                }
            }

            if (anyCritical)
            {
                report.AppendLine($"{Heading2} {LogStrings.Report_ErrorsCriticalOccurred}");
                report.AppendLine();


                foreach (var summary in summaries)
                {

                    var reportSrcPageUrl = summary.SourcePage.PrependIfNotNull(summary.BaseSourceUrl);
                    var reportSrcPageTitle = summary.SourcePage.StripRelativeUrlSectionString();

                    foreach (var log in summary.CriticalErrors) //In theory should only be one - showstoppers
                    {
                        report.AppendLine($"### {log.Item2.EntryTime} - [{reportSrcPageTitle}]({reportSrcPageUrl})");

                        report.AppendLine();
                        report.AppendFormat("_{1}{0}_ \n", log.Item2?.Exception?.StackTrace, log.Item2?.Exception?.Message);
                        report.AppendLine();
                    }

                }
            }
        }

        /// <summary>
        /// Analyses logs and extracts key information for reports
        /// </summary>
        /// <returns></returns>
        private TransformationLogAnalysis AnalyseLogsForReport(List<Tuple<LogLevel, LogEntry>> logs, string pageId)
        {
            // Log Groups
            var logEntriesToProcess = Logs.Where(p => p.Item2.PageId == pageId);
            var orderedLogs = logEntriesToProcess.OrderBy(l => l.Item2.EntryTime);
            var transformationSummary = orderedLogs.Where(l => l.Item2.Heading == LogStrings.Heading_Summary);
            var assetsTransferred = transformationSummary.Where(l => l.Item2.Significance == LogEntrySignificance.AssetTransferred);
            var logDetails = orderedLogs.Where(l => l.Item2.Heading != LogStrings.Heading_PageTransformationInfomation &&
                                                l.Item2.Heading != LogStrings.Heading_Summary);

            // Logs that contains error types
            var logErrors = orderedLogs.Where(l => l.Item1 == LogLevel.Error);
            var logWarnings = orderedLogs.Where(l => l.Item1 == LogLevel.Warning);
            var criticalErrors = transformationSummary.Where(l => l.Item2.IsCriticalException == true);

            // Extract key segments
            var sourcePage = transformationSummary.FirstOrDefault(l => l.Item2.Significance == LogEntrySignificance.SourcePage);
            var targetPage = transformationSummary.FirstOrDefault(l => l.Item2.Significance == LogEntrySignificance.TargetPage);
            var sourceSite = transformationSummary.FirstOrDefault(l => l.Item2.Significance == LogEntrySignificance.SourceSiteUrl);
            var targetSite = transformationSummary.FirstOrDefault(l => l.Item2.Significance == LogEntrySignificance.TargetSiteUrl);

            // Populate targetsite in case on an in-place transformation
            if (targetSite==null)
            {
                targetSite = sourceSite;
            }

            // Tenant Details
            var baseSourceUrl = GetBaseUrl(sourceSite); 
            var baseTargetUrl = GetBaseUrl(targetSite);
            
            // Calculate Tranform Duration from Log Timings
            var transformationDuration = default(TimeSpan);
            var logStart = orderedLogs.FirstOrDefault();
            var logEnd = orderedLogs.LastOrDefault();

            if (logStart != default(Tuple<LogLevel, LogEntry>) && logEnd != default(Tuple<LogLevel, LogEntry>))
            {
                TimeSpan span = logEnd.Item2.EntryTime.Subtract(logStart.Item2.EntryTime);
                transformationDuration = span;
            }


            // Summary Transformation Settings
            var transformationSettingsLogs = orderedLogs.Where(l => l.Item2.Heading == LogStrings.Heading_PageTransformationInfomation);
            var settings = new List<Tuple<string, string>>();
            foreach (var log in transformationSettingsLogs)
            {
                var keyValue = log.Item2.Message.Split(new string[] { LogStrings.KeyValueSeperatorToken }, StringSplitOptions.None);
                if (keyValue.Length == 2) //Protect output
                {
                    settings.Add(new Tuple<string, string>(keyValue[0], keyValue[1]));
                }
            }


            TransformationLogAnalysis logAnalysis = new TransformationLogAnalysis()
            {
                ReportDate = _reportDate,
                PageLogsOrdered = orderedLogs.ToList(),
                TransformationVerboseSummary = transformationSummary.ToList(),
                TransformationVerboseDetails = logDetails.ToList(),
                TransformationSettings = settings,
                AssetsTransferred = assetsTransferred.ToList(),
                CriticalErrors = criticalErrors.ToList(),
                Errors = logErrors.ToList(),
                Warnings = logWarnings.ToList(),
                PageId = pageId,

                SourcePage = sourcePage?.Item2.Message,
                TargetPage = targetPage?.Item2.Message,
                SourceSite = sourceSite?.Item2.Message,
                TargetSite = targetSite?.Item2.Message,
                BaseSourceUrl = baseSourceUrl,
                BaseTargetUrl = baseTargetUrl,
                TransformationDuration = transformationDuration

            };

            return logAnalysis;
        }

        /// <summary>
        /// Gets base url for report
        /// </summary>
        /// <param name="sourceSite"></param>
        /// <returns></returns>
        private static string GetBaseUrl(Tuple<LogLevel, LogEntry> sourceSite)
        {
            try
            {
                if (sourceSite != default(Tuple<LogLevel, LogEntry>) && (sourceSite.Item2.Message.ContainsIgnoringCasing("https://") ||
                    sourceSite.Item2.Message.ContainsIgnoringCasing("http://")))
                {
                    Uri siteUri = new Uri(sourceSite?.Item2.Message);
                    string host = $"{siteUri.Scheme}://{siteUri.DnsSafeHost}";
                    return host;
                }
            }
            catch (Exception)
            {
                //Swallow
            }

            return string.Empty;
        }

        /// <summary>
        /// Output the report when flush is called
        /// </summary>
        public virtual void Flush()
        {
            Flush(true);
        }

        /// <summary>
        /// Output the report when flush is called
        /// </summary>
        /// <param name="clearLogData">Also clear the log data</param>
        public virtual void Flush(bool clearLogData)
        {
            try
            {
                //var report = GenerateReportWithSummaryAtTop();
                var report = GenerateReportWithSummaryAtTop();

                // Dont want to assume locality here
                string logRunTime = _reportDate.ToString().Replace('/', '-').Replace(":", "-").Replace(" ", "-");
                string logFileName = $"Page-Transformation-Report-{logRunTime}{_reportFileName}";

                logFileName = $"{_reportFolder}\\{logFileName}.md";

                using (StreamWriter sw = new StreamWriter(logFileName, true))
                {
                    sw.WriteLine(report);
                }

                // Cleardown all logs
                if (clearLogData)
                {
                    var logs = _lazyLogInstance.Value;
                    logs.RemoveRange(0, logs.Count);
                }

                Console.WriteLine($"Report saved as: {logFileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing to log file: {0} {1}", ex.Message, ex.StackTrace);
            }
        }
    }
}