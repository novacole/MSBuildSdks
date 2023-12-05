﻿// Copyright (c) Microsoft Corporation. All rights reserved.
//
// Licensed under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Microsoft.Build
{
    /// <summary>
    /// Runs tests with vstest.
    /// </summary>
    public class RunVSTestTask : Task
    {
        private const string CodeCoverageString = "Code Coverage";
        private static readonly HashSet<string> NormalTestLogging = new (new[] { "n", "normal", "d", "detailed", "diag", "diagnostic" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> QuietTestLogging = new (new[] { "q", "quiet" }, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets or Sets Full path to the test file.
        /// </summary>
        public string IsTestProject { get; set; }

        /// <summary>
        /// Gets or Sets Full path to the test file.
        /// </summary>
        public string TestFileFullPath { get; set; }

        /// <summary>
        /// Gets or Sets Settings for VSTest.
        /// </summary>
        public string VSTestSetting { get; set; }

        /// <summary>
        /// Gets or Sets Paths to test adapter DLLs.
        /// </summary>
        public string[] VSTestTestAdapterPath { get; set; }

        /// <summary>
        /// Gets or Sets Framework for VSTest.
        /// </summary>
        public string VSTestFramework { get; set; }

        /// <summary>
        /// Gets or Sets Platform for VSTest.
        /// </summary>
        public string VSTestPlatform { get; set; }

        /// <summary>
        /// Gets or Sets Filter used to select test cases.
        /// </summary>
        public string VSTestTestCaseFilter { get; set; }

        /// <summary>
        /// Gets or Sets Logger used for VSTest.
        /// </summary>
        public string[] VSTestLogger { get; set; }

        /// <summary>
        /// Gets or Sets Indicates whether to list test cases.
        /// </summary>
        public string VSTestListTests { get; set; }

        /// <summary>
        /// Gets or Sets Diagnostic data for VSTest.
        /// </summary>
        public string VSTestDiag { get; set; }

        /// <summary>
        /// Gets or Sets Command line options for VSTest.
        /// </summary>
        public string[] VSTestCLIRunSettings { get; set; }

        // Initialized to empty string to allow declaring as non-nullable, the property is marked as
        // required so we can ensure that the property is set to non-null before the task is executed.

        /// <summary>
        /// Gets or Sets Path to VSTest console executable.
        /// </summary>
        public string VSTestConsolePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or Sets Directory where VSTest results are saved.
        /// </summary>
        public string VSTestResultsDirectory { get; set; }

        /// <summary>
        /// Gets or Sets Verbosity level of VSTest output.
        /// </summary>
        public string VSTestVerbosity { get; set; }

        /// <summary>
        /// Gets or Sets Collectors for VSTest run.
        /// </summary>
        public string[] VSTestCollect { get; set; }

        /// <summary>
        /// Gets or Sets source blame on test failure.
        /// </summary>
        public string VSTestBlame { get; set; }

        /// <summary>
        /// Gets or Sets source blame on test crash.
        /// </summary>
        public string VSTestBlameCrash { get; set; }

        /// <summary>
        /// Gets or Sets Dumptype used for crash source blame.
        /// </summary>
        public string VSTestBlameCrashDumpType { get; set; }

        /// <summary>
        /// Gets or Sets source blame on test crash even if test pass.
        /// </summary>
        public string VSTestBlameCrashCollectAlways { get; set; }

        /// <summary>
        /// Gets or Sets source blame on test hang.
        /// </summary>
        public string VSTestBlameHang { get; set; }

        /// <summary>
        /// Gets or Sets Dumptype used for hang source blame.
        /// </summary>
        public string VSTestBlameHangDumpType { get; set; }

        /// <summary>
        /// Gets or Sets Time out for hang source blame.
        /// </summary>
        public string VSTestBlameHangTimeout { get; set; }

        /// <summary>
        /// Gets or Sets The directory path where trace data collector is.
        /// </summary>
        public string VSTestTraceDataCollectorDirectoryPath { get; set; }

        /// <summary>
        /// Gets or Sets disabling Microsoft logo while running test through VSTest.
        /// </summary>
        public string VSTestNoLogo { get; set; }

        /// <summary>
        /// Gets or Sets Test artifacts processing mode which is applicable for .NET 5.0 or later versions.
        /// </summary>
        public string VSTestArtifactsProcessingMode { get; set; }

        /// <summary>
        /// Gets or Sets Correlation Id of test session.
        /// </summary>
        public string VSTestSessionCorrelationId { get; set; }

        /// <summary>
        /// Gets or Sets Runner version of VSTest.
        /// </summary>
        [Required]
        public string VSTestRunnerVersion { get; set; }

        /// <summary>
        /// Gets or Sets Path to nuget package cache.
        /// </summary>
        [Required]
        public string NugetPath { get; set; }

        /// <summary>
        /// Executes the test.
        /// </summary>
        /// <returns>Returns true if the test was executed, otherwise false.</returns>
        public override bool Execute()
        {
            var debugEnabled = Environment.GetEnvironmentVariable("VSTEST_BUILD_DEBUG");
            if (!string.IsNullOrEmpty(debugEnabled) && debugEnabled.Equals("1", StringComparison.Ordinal))
            {
                Log.LogMessage("Waiting for debugger attach...");

                var currentProcess = Process.GetCurrentProcess();
                Log.LogMessage($"Process Id: {currentProcess.Id}, Name: {currentProcess.ProcessName}");

                while (!Debugger.IsAttached)
                {
                    Thread.Sleep(1000);
                }

                Debugger.Break();
            }

            return ExecuteTest() == 0;
        }

        internal IEnumerable<string> CreateArguments()
        {
            var allArgs = AddArgs();

            // VSTestCLIRunSettings should be last argument in allArgs as vstest.console ignore options after "--"(CLIRunSettings option).
            AddCliRunSettingsArgs(allArgs);

            return allArgs;
        }

        private void AddCliRunSettingsArgs(List<string> allArgs)
        {
            if (VSTestCLIRunSettings != null && VSTestCLIRunSettings.Length > 0)
            {
                allArgs.Add("--");
                foreach (var arg in VSTestCLIRunSettings)
                {
                    allArgs.Add(ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(arg));
                }
            }
        }

        private List<string> AddArgs()
        {
            var isConsoleLoggerSpecifiedByUser = false;
            var isCollectCodeCoverageEnabled = false;
            var isRunSettingsEnabled = false;
            var allArgs = new List<string>();

            // TODO log arguments in task
            if (!string.IsNullOrEmpty(VSTestSetting))
            {
                isRunSettingsEnabled = true;
                allArgs.Add("--settings:" + ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(VSTestSetting));
            }

            if (VSTestTestAdapterPath != null && VSTestTestAdapterPath.Length > 0)
            {
                foreach (var arg in VSTestTestAdapterPath)
                {
                    allArgs.Add("--testAdapterPath:" + ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(arg));
                }
            }

            if (!string.IsNullOrEmpty(VSTestFramework))
            {
                allArgs.Add("--framework:" + ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(VSTestFramework));
            }

            // vstest.console only support x86 and x64 for argument platform
            if (!string.IsNullOrEmpty(VSTestPlatform) && !VSTestPlatform.Contains("AnyCPU"))
            {
                allArgs.Add("--platform:" + ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(VSTestPlatform));
            }

            if (!string.IsNullOrEmpty(VSTestTestCaseFilter))
            {
                allArgs.Add("--testCaseFilter:" +
                            ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(VSTestTestCaseFilter));
            }

            if (VSTestLogger != null && VSTestLogger.Length > 0)
            {
                foreach (var arg in VSTestLogger)
                {
                    allArgs.Add("--logger:" + ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(arg));

                    if (arg.StartsWith("console", StringComparison.OrdinalIgnoreCase))
                    {
                        isConsoleLoggerSpecifiedByUser = true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(VSTestResultsDirectory))
            {
                allArgs.Add("--resultsDirectory:" +
                            ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(VSTestResultsDirectory));
            }

            if (!string.IsNullOrEmpty(VSTestListTests))
            {
                allArgs.Add("--listTests");
            }

            if (!string.IsNullOrEmpty(VSTestDiag))
            {
                allArgs.Add("--Diag:" + ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(VSTestDiag));
            }

            if (string.IsNullOrEmpty(TestFileFullPath))
            {
                Log.LogError("Test file path cannot be empty or null.");
            }
            else
            {
                allArgs.Add(ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(TestFileFullPath));
            }

            // Console logger was not specified by user, but verbosity was, hence add default console logger with verbosity as specified
            if (!string.IsNullOrEmpty(VSTestVerbosity) && !isConsoleLoggerSpecifiedByUser)
            {
                string vsTestVerbosity = "minimal";
                if (NormalTestLogging.Contains(VSTestVerbosity))
                {
                    vsTestVerbosity = "normal";
                }
                else if (QuietTestLogging.Contains(VSTestVerbosity))
                {
                    vsTestVerbosity = "quiet";
                }

                allArgs.Add("--logger:Console;Verbosity=" + vsTestVerbosity);
            }

            var blameCrash = !string.IsNullOrEmpty(VSTestBlameCrash);
            var blameHang = !string.IsNullOrEmpty(VSTestBlameHang);
            if (!string.IsNullOrEmpty(VSTestBlame) || blameCrash || blameHang)
            {
                var blameArgs = "--Blame";

                var dumpArgs = new List<string>();
                if (blameCrash || blameHang)
                {
                    if (blameCrash)
                    {
                        dumpArgs.Add("CollectDump");
                        if (!string.IsNullOrEmpty(VSTestBlameCrashCollectAlways))
                        {
                            dumpArgs.Add($"CollectAlways={string.IsNullOrEmpty(VSTestBlameCrashCollectAlways)}");
                        }

                        if (!string.IsNullOrEmpty(VSTestBlameCrashDumpType))
                        {
                            dumpArgs.Add($"DumpType={VSTestBlameCrashDumpType}");
                        }
                    }

                    if (blameHang)
                    {
                        dumpArgs.Add("CollectHangDump");

                        if (!string.IsNullOrEmpty(VSTestBlameHangDumpType))
                        {
                            dumpArgs.Add($"HangDumpType={VSTestBlameHangDumpType}");
                        }

                        if (!string.IsNullOrEmpty(VSTestBlameHangTimeout))
                        {
                            dumpArgs.Add($"TestTimeout={VSTestBlameHangTimeout}");
                        }
                    }

                    if (dumpArgs.Count != 0)
                    {
                        blameArgs += $":\"{string.Join(";", dumpArgs)}\"";
                    }
                }

                allArgs.Add(blameArgs);
            }

            if (VSTestCollect != null && VSTestCollect.Length > 0)
            {
                foreach (var arg in VSTestCollect)
                {
                    // For collecting code coverage, argument value can be either "Code Coverage" or "Code Coverage;a=b;c=d".
                    // Split the argument with ';' and compare first token value.
                    var tokens = arg.Split(';');

                    if (arg.Equals(CodeCoverageString, StringComparison.OrdinalIgnoreCase) ||
                        tokens[0].Equals(CodeCoverageString, StringComparison.OrdinalIgnoreCase))
                    {
                        isCollectCodeCoverageEnabled = true;
                    }

                    allArgs.Add("--collect:" + ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(arg));
                }
            }

            if (isCollectCodeCoverageEnabled || isRunSettingsEnabled)
            {
                // Pass TraceDataCollector path to vstest.console as TestAdapterPath if --collect "Code Coverage"
                // or --settings (User can enable code coverage from runsettings) option given.
                // Not parsing the runsettings for two reason:
                //    1. To keep no knowledge of runsettings structure in VSTestTask.
                //    2. Impact of adding adapter path always is minimal. (worst case: loads additional data collector assembly in datacollector process.)
                // This is required due to currently trace datacollector not ships with dotnet sdk, can be remove once we have
                // go code coverage x-plat.
                if (!string.IsNullOrEmpty(VSTestTraceDataCollectorDirectoryPath))
                {
                    allArgs.Add("--testAdapterPath:" +
                                ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(
                                    VSTestTraceDataCollectorDirectoryPath));
                }
            }

            if (!string.IsNullOrEmpty(VSTestNoLogo))
            {
                allArgs.Add("--nologo");
            }

            if (!string.IsNullOrEmpty(VSTestArtifactsProcessingMode) && VSTestArtifactsProcessingMode.Equals("collect", StringComparison.OrdinalIgnoreCase))
            {
                allArgs.Add("--artifactsProcessingMode-collect");
            }

            if (!string.IsNullOrEmpty(VSTestSessionCorrelationId))
            {
                allArgs.Add("--testSessionCorrelationId:" + ArgumentEscaper.HandleEscapeSequenceInArgForProcessStart(VSTestSessionCorrelationId));
            }

            return allArgs;
        }

        private int ExecuteTest()
        {
            string packagePath = $@"{NugetPath}\microsoft.testplatform\{VSTestRunnerVersion}\tools\net462\Common7\IDE\Extensions\TestPlatform\";

            var processInfo = new ProcessStartInfo
            {
                FileName = $"{packagePath}vstest.console.exe",
                Arguments = string.Join(" ", CreateArguments()),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var activeProcess = new Process { StartInfo = processInfo };
            activeProcess.Start();
            using StreamReader errReader = activeProcess.StandardError;
            _ = Log.LogMessagesFromStream(errReader, MessageImportance.Normal);

            using StreamReader outReader = activeProcess.StandardOutput;
            _ = Log.LogMessagesFromStream(outReader, MessageImportance.Normal);
            activeProcess.WaitForExit();

            return activeProcess.ExitCode;
        }
    }
}