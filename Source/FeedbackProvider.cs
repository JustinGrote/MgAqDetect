using System.Runtime.CompilerServices;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Subsystem.Feedback;
using static System.Management.Automation.Subsystem.SubsystemManager;
using static System.Management.Automation.Subsystem.SubsystemKind;
using static MicrosoftGraphAdvancedQueryFeedbackProvider.MgAstQueries;
using static MicrosoftGraphAdvancedQueryFeedbackProvider.Strings;

[assembly: InternalsVisibleTo("Test")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace MicrosoftGraphAdvancedQueryFeedbackProvider;

/// <summary>
/// A feedback provider that looks for Microsoft Graph commands that require additional Advanced Query parameters and warns you of the same
/// </summary>
public class AqFeedbackProvider : IFeedbackProvider
{
  public Guid Id { get; init; }

  #region IFeedbackProvider

  public string Name { get; } = "Microsoft Graph Advanced Query Detection";

  public string Description { get; } = "Detects when you have used Microsoft Graph commands that require additional Advanced Query parameters and warns you of the same";

  FeedbackTrigger IFeedbackProvider.Trigger { get; } = FeedbackTrigger.All;

  public FeedbackItem? GetFeedback(FeedbackContext context, CancellationToken token)
  {
    HashSet<string> graphCommands = new HashSet<string>();
    if (context.LastError is not null)
    {
      var result = InspectGraphError(context.LastError);
      if (result is not null)
        graphCommands.Add(result);
    }

    FindMicrosoftGraphCommands(context.CommandLineAst)
      .Where(TestAdvancedQueryNeeded)
      .Select(c => c.ToString())
      .ToList()
      .ForEach(c => graphCommands.Add(c));

    if (!graphCommands.Any())
      return null;

    return CreateAqFeedbackItem(
      graphCommands.Select(command => command.ToString())
    );
  }
  #endregion IFeedbackProvider

  /// <summary>
  /// Inspects the last error to see if it is a Graph error that needs Advanced Query
  /// </summary>
  string? InspectGraphError(ErrorRecord error)
  {
    var errorMessage = error.Exception.Message;
    var errorCommand = error.InvocationInfo.Statement;
    var errorCode = error.FullyQualifiedErrorId.Split(',')[0];

    if (errorCode == "Request_BadRequest")
    {
      // TargetObject is an anonyous type so we have to use reflection to get this value
      string? filter = error.TargetObject?.GetType().GetProperty("Filter")?.GetValue
      (error.TargetObject, null) as string;
      if (filter is not null)
      {
        // Use of $count in a filter expression
        if (filter.Contains("/$count"))
        {
          return errorCommand;
        }
      }
    }

    if (errorCode == "Request_UnsupportedQuery")
    {
      if (error.Exception.Message is not null)
      {
        // Use of $search
        if (errorMessage.Contains(SearchUnsupportedError))
          return errorCommand;

        // Use of $filter in endsWith
        // Use of $filter with not operator
        // Use of $filter with not and StartWith
        if (errorMessage.Contains(ConsistencyHeaderMissingError))
          return errorCommand;

        if (errorMessage.Contains(SortingNotSupportedError))
        {
          // Use of $filter and $orderby in the same query
          // TODO: This may match incorrectly in a complicated script, we should verify the commands we search are graph ones only.
          var parameters = ScriptBlock.Create(errorCommand).Ast.FindAll<CommandParameterAst>().Select(parameter => parameter.ParameterName).ToList();

          if (parameters.Contains("OrderBy", StringComparer.OrdinalIgnoreCase))
            return errorCommand;

          return null;
        }

        // Use of filter with ne operator
        if (errorMessage.Contains(NotEqualsMatch))
          return errorCommand;

        // Various property errors
        // TODO: Check the individual property name support
        if (errorMessage.Contains(UnsupportedOrInvalidError))
          return errorCommand;
      }
    }

    return null;
  }

  /// <summary>
  /// Generates an Advanced Query FeedBackItem with the header and footer pre-populated
  /// </summary>
  static public FeedbackItem CreateAqFeedbackItem(IEnumerable<string> commands)
  => new(
      AdvancedQueryHeader,
      commands.ToList(),
      AdvancedQueryFooter,
      FeedbackDisplayLayout.Portrait
    );
}

public static class Strings
{
  public const string AdvancedQueryHeader = "The following command combinations were detected as needing Advanced Query Capabilities. Ensure you add -CountVariable CountVar and -ConsistencyLevel Eventual to these commands or you may get unexpected errors or empty results";
  public const string AdvancedQueryFooter = "More: https://learn.microsoft.com/en-us/graph/aad-advanced-queries?tabs=powershell";
  public const string SearchUnsupportedError = @"Request with $search query parameter only works through MSGraph with a special request header: 'ConsistencyLevel: eventual'";
  public const string FilterEndsWithError = $"Operator 'endsWith' is not supported because the 'ConsistencyLevel:eventual' header is missing.";
  public const string SortingNotSupportedError = @"Sorting not supported for current query";
  public const string ConsistencyHeaderMissingError = @"is not supported because the 'ConsistencyLevel:eventual' header is missing.";
  public const string NotEqualsMatch = @"Filter operator 'NotEqualsMatch' is not supported.";
  public const string UnsupportedOrInvalidError = @"Unsupported or invalid query filter clause specified for property";
}

public class Init : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
  // Uniquely identifies this provider. This could be randomly generated but having it static helps with tracing/troubleshooting
  public Guid ProviderId { get; } = new Guid("c58b84a7-bc73-4bbc-a540-5f5d031cfb0a");
  private AqFeedbackProvider? _provider;
  AqFeedbackProvider provider => _provider ??= new() { Id = ProviderId };

  public void OnImport()
  {
    RegisterSubsystem(FeedbackProvider, provider);
  }
  public void OnRemove(PSModuleInfo? psModuleInfo)
  {
    UnregisterSubsystem<IFeedbackProvider>(ProviderId);
  }
}
