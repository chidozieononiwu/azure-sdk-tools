// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.CommandLine;
using System.CommandLine.Invocation;
using System.ComponentModel;
using System.Text.Json;
using Azure.Core;
using Azure.Sdk.Tools.Cli.Commands;
using Azure.Sdk.Tools.Cli.Contract;
using Azure.Sdk.Tools.Cli.Helpers;
using Azure.Sdk.Tools.Cli.Models;
using Azure.Sdk.Tools.Cli.Services;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.TestManagement.WebApi;
using Microsoft.VisualStudio.Services.OAuth;
using Microsoft.VisualStudio.Services.TestResults.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using ModelContextProtocol.Server;

namespace Azure.Sdk.Tools.Cli.Tools;

[McpServerToolType, Description("Fetches data from an Azure Pipelines run.")]
public class AnalyzePipelinesTool : MCPTool
{
    private BuildHttpClient buildClient;
    private TestResultsHttpClient testClient;
    private IAzureAgentService azureAgentService;
    private TokenUsageHelper usage;
    private readonly bool initialized = false;

    private IAzureService azureService;
    private IAzureAgentServiceFactory azureAgentServiceFactory;
    private IOutputService output;
    private ILogger<AnalyzePipelinesTool> logger;

    // Options
    private readonly Option<int> buildIdOpt = new(["--build-id", "-b"], "Pipeline/Build ID") { IsRequired = true };
    private readonly Option<int> logIdOpt = new(["--log-id"], "ID of the pipeline task log");
    private readonly Option<string> projectOpt = new(["--project", "-p"], "Pipeline project name");
    private readonly Option<string> projectEndpointOpt = new(["--ai-endpoint", "-e"], "The ai foundry project endpoint for the Azure AI Agent service");
    private readonly Option<string> aiModelOpt = new(["--ai-model"], "The model to use for the Azure AI Agent");

    public AnalyzePipelinesTool(
        IAzureService azureService,
        IAzureAgentServiceFactory azureAgentServiceFactory,
        IOutputService output,
        ILogger<AnalyzePipelinesTool> logger
    ) : base()
    {
        this.azureService = azureService;
        this.azureAgentServiceFactory = azureAgentServiceFactory;
        this.output = output;
        this.logger = logger;

        CommandHierarchy =
        [
            SharedCommandGroups.AzurePipelines   // azsdk azp
        ];
    }

    public override Command GetCommand()
    {
        var analyzePipelineCommand = new Command("analyze", "Analyze a pipeline run") {
            buildIdOpt, projectOpt, logIdOpt, projectEndpointOpt, aiModelOpt
        };
        analyzePipelineCommand.SetHandler(async ctx => { await HandleCommand(ctx, ctx.GetCancellationToken()); });

        return analyzePipelineCommand;
    }

    public override async Task HandleCommand(InvocationContext ctx, CancellationToken ct)
    {
        Initialize();

        var cmd = ctx.ParseResult.CommandResult.Command.Name;
        var buildId = ctx.ParseResult.GetValueForOption(buildIdOpt);
        var project = ctx.ParseResult.GetValueForOption(projectOpt);

        var logId = ctx.ParseResult.GetValueForOption(logIdOpt);
        var projectEndpoint = ctx.ParseResult.GetValueForOption(projectEndpointOpt);
        var aiModel = ctx.ParseResult.GetValueForOption(aiModelOpt);

        logger.LogInformation("Analyzing pipeline {buildId}...", buildId);
        azureAgentService = azureAgentServiceFactory.Create(projectEndpoint, aiModel);

        if (logId != 0)
        {
            var result = await AnalyzePipelineFailureLog(project, buildId, [logId], ct);
            ctx.ExitCode = ExitCode;
            usage?.LogCost();
            output.Output(result);
        }
        else
        {
            var result = await AnalyzePipeline(project, buildId, ct);
            ctx.ExitCode = ExitCode;
            usage?.LogCost();
            output.Output(result);
        }
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }
        var tokenScope = new[] { "499b84ac-1321-427f-aa17-267ca6975798/.default" };  // Azure DevOps scope
        var msftCorpTenant = "72f988bf-86f1-41af-91ab-2d7cd011db47";
        var token = azureService.GetCredential(msftCorpTenant).GetToken(new TokenRequestContext(tokenScope));
        var tokenCredential = new VssOAuthAccessTokenCredential(token.Token);
        var connection = new VssConnection(new Uri($"https://dev.azure.com/azure-sdk"), tokenCredential);
        buildClient = connection.GetClient<BuildHttpClient>();
        testClient = connection.GetClient<TestResultsHttpClient>();
    }

    public async Task<Build> GetPipelineRun(string? project, int buildId)
    {
        try
        {
            var _project = project ?? "public";
            logger.LogDebug("Getting pipeline run for {project} {buildId}", _project, buildId);
            var build = await buildClient.GetBuildAsync(_project, buildId);
            return build;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(project))
            {
                throw new Exception($"Failed to find build {buildId} in project 'public': {ex.Message}");
            }
            // If project is not specified, try both azure sdk public and internal devops projects
            return await GetPipelineRun("internal", buildId);
        }
    }

    public async Task<List<TimelineRecord>?> GetPipelineTaskFailures(string project, int buildId, CancellationToken ct = default)
    {
        try
        {
            logger.LogDebug("Getting pipeline task failures for {project} {buildId}", project, buildId);
            var timeline = await buildClient.GetBuildTimelineAsync(project, buildId, cancellationToken: ct);
            var failedNonTests = timeline.Records.Where(
                                    r => r.Result == TaskResult.Failed
                                    && r.RecordType == "Task"
                                    && !IsTestStep(r.Name))
                                .ToList();
            logger.LogDebug("Found {count} failed tasks", failedNonTests.Count);
            return failedNonTests;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to get pipeline task failures {buildId}: {exception}", buildId, ex.Message);
            SetFailure();
            return null;
        }
    }

    [McpServerTool, Description(@"
        Analyze and diagnose the failed test results from a pipeline.
        Include relevant data like test name and environment, error type, error messages, functions and error lines.
        Provide suggested next steps.
    ")]
    public async Task<List<FailedTestRunResponse>> GetPipelineFailedTestResults(string project, int buildId, CancellationToken ct = default)
    {
        try
        {
            logger.LogDebug("Getting pipeline failed test results for {project} {buildId}", project, buildId);
            var results = new List<ShallowTestCaseResult>();
            var testRuns = await testClient.GetTestResultsByPipelineAsync(project, buildId, cancellationToken: ct);
            results.AddRange(testRuns);
            while (testRuns.ContinuationToken != null)
            {
                var nextResults = await testClient.GetTestResultsByPipelineAsync(project, buildId, continuationToken: testRuns.ContinuationToken, cancellationToken: ct);
                results.AddRange(nextResults);
                testRuns.ContinuationToken = nextResults.ContinuationToken;
            }

            var failedRuns = results.Where(
                r => r.Outcome == TestOutcome.Failed.ToString()
                || r.Outcome == TestOutcome.Aborted.ToString())
            .Select(r => r.RunId)
            .Distinct()
            .ToList();

            logger.LogDebug("Getting test results for {count} failed test runs", failedRuns.Count);

            var failedRunData = new List<FailedTestRunResponse>();

            foreach (var runId in failedRuns)
            {
                var testCases = await testClient.GetTestResultsAsync(
                                    project,
                                    runId,
                                    outcomes: [TestOutcome.Failed, TestOutcome.Aborted],
                                    cancellationToken: ct);

                foreach (var tc in testCases)
                {
                    failedRunData.Add(new FailedTestRunResponse
                    {
                        RunId = runId,
                        TestCaseTitle = tc.TestCaseTitle,
                        ErrorMessage = tc.ErrorMessage,
                        StackTrace = tc.StackTrace,
                        Outcome = tc.Outcome,
                        Url = tc.Url
                    });
                }
            }

            return failedRunData;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to get pipeline failed test results {buildId}: {exception}", buildId, ex.Message);
            SetFailure();
            return new List<FailedTestRunResponse>()
            {
                new FailedTestRunResponse()
                {
                    ResponseError = $"Failed to get pipeline failed test results {buildId}: {ex.Message}",
                }
            };
        }
    }

    public async Task<LogAnalysisResponse> AnalyzePipelineFailureLog(string? project, int buildId, List<int> logIds, CancellationToken ct)
    {
        try
        {
            project ??= "public";
            var session = $"{project}-{buildId}";
            List<string> logs = [];

            foreach (var logId in logIds)
            {
                logger.LogDebug("Downloading pipeline failure log for {project} {buildId} {logId}", project, buildId, logId);

                var logContent = await buildClient.GetBuildLogLinesAsync(project, buildId, logId, cancellationToken: ct);
                var logText = string.Join("\n", logContent);
                var tempPath = Path.GetTempFileName() + ".txt";
                logger.LogDebug("Writing log id {logId} to temporary file {tempPath}", logId, tempPath);
                await File.WriteAllTextAsync(tempPath, logText, ct);
                var filename = $"{session}-{logId}.txt";
                logs.Add(tempPath);
            }

            var (result, _usage) = await azureAgentService.QueryFiles(logs, session, "Why did this pipeline fail?", ct);

            foreach (var log in logs)
            {
                File.Delete(log);
            }

            if (usage != null)
            {
                usage += _usage;
            }
            else
            {
                usage = _usage;
            }

            // Sometimes chat gpt likes to wrap the json in markdown
            if (result.StartsWith("```json")
                && result.EndsWith("```"))
            {
                result = result[7..^3].Trim();
            }

            try
            {
                return JsonSerializer.Deserialize<LogAnalysisResponse>(result);
            }
            catch (JsonException ex)
            {
                logger.LogError("Failed to deserialize log analysis response: {exception}", ex.Message);
                logger.LogError("Response:\n{result}", result);

                SetFailure();

                return new LogAnalysisResponse()
                {
                    ResponseError = "Failed to deserialize log analysis response. Check the logs for more details.",
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to analyze pipeline {buildId}: {error}", buildId, ex.Message);
            SetFailure();
            return new LogAnalysisResponse()
            {
                ResponseError = $"Failed to analyze pipeline {buildId}: {ex.Message}",
            };
        }
    }

    [McpServerTool, Description("Analyze azure pipeline for failures")]
    public async Task<AnalyzePipelineResponse> AnalyzePipeline(string? project, int buildId, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(project))
            {
                var pipeline = await GetPipelineRun(project, buildId);
                project = pipeline.Project.Name;
            }
            var failedTasks = await GetPipelineTaskFailures(project, buildId, ct);
            var failedTests = await GetPipelineFailedTestResults(project, buildId, ct);

            var taskAnalysis = new List<LogAnalysisResponse>();

            List<int> logIds = failedTasks?
                .Where(t => t.Log != null)
                .Select(t => t.Log.Id)
                .Distinct()
                .ToList() ?? [];

            var analysis = await AnalyzePipelineFailureLog(project, buildId, logIds, ct);
            taskAnalysis.Add(analysis);

            return new AnalyzePipelineResponse()
            {
                FailedTasks = taskAnalysis,
                FailedTests = failedTests
            };
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to analyze pipeline {buildId}: {exception}", buildId, ex.Message);
            SetFailure();
            return new AnalyzePipelineResponse()
            {
                ResponseError = $"Failed to analyze pipeline {buildId}: {ex.Message}",
            };
        }
    }

    public bool IsTestStep(string stepName)
    {
        return stepName.Contains("test", StringComparison.OrdinalIgnoreCase);
    }
}
