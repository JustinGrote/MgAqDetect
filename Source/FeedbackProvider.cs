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
/// This adapter is needed because FeedbackContext is <see langword="sealed"/> and not constructable for testing.
/// </summary>
interface FeedbackContextAdapter
{
  public string CommandLine { get; }
  public Ast CommandLineAst { get; }
  public IReadOnlyList<Token> CommandLineTokens { get; }
  public string CurrentLocation { get; }
  // ErrorRecord Invocation is not easily constructed so we abstract these to make it more testable.
  public string? ErrorCommand { get; }
  public string? ErrorMessage { get; }
  public string? ErrorId { get; }
  public object? ErrorTarget { get; }
}

class PSFeedbackContextAdapter(FeedbackContext context) : FeedbackContextAdapter
{
  public Ast CommandLineAst => context.CommandLineAst;
  public string CommandLine => context.CommandLine;
  public IReadOnlyList<Token> CommandLineTokens => context.CommandLineTokens;
  public string CurrentLocation => context.CurrentLocation.ToString();
  public string? ErrorCommand => context.LastError?.InvocationInfo.Statement;
  public string? ErrorMessage => context.LastError?.ErrorDetails.Message;
  public string? ErrorId => context.LastError?.FullyQualifiedErrorId;
  public object? ErrorTarget => context.LastError?.TargetObject;
}

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
    => GetFeedbackImpl(new PSFeedbackContextAdapter(context));
  #endregion

  /// <summary>
  /// This is the actual implementation of the feedback provider. It is separated for testing because the public interface has a <see langword="sealed"/> parameter that cannot be construted.
  /// </summary>
  ///
  internal FeedbackItem? GetFeedbackImpl(FeedbackContextAdapter context)
  {
    HashSet<string> graphCommands = new HashSet<string>();
    if (context.ErrorId is not null)
    {
      var result = InspectGraphError(context.ErrorId, context.ErrorMessage, context.ErrorTarget, context.ErrorCommand);
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

  /// <summary>
  /// Inspects the last error to see if it is a Graph error that needs Advanced Query
  /// </summary>
  string? InspectGraphError(string fullyQualifiedErrorId, string? errorMessage, object? targetObject, string? statement)
  {
    var errorCode = fullyQualifiedErrorId.Split(',')[0];

    if (errorCode == "Request_BadRequest")
    {
      // TargetObject is an anonyous type so we have to use reflection to get this value
      string? filter = targetObject?.GetType().GetProperty("Filter")?.GetValue
      (targetObject, null) as string;
      if (filter is not null)
      {
        // Use of $count in a filter expression
        if (filter.Contains("/$count"))
        {
          return statement;
        }
      }
    }

    if (errorCode == "Request_UnsupportedQuery")
    {
      if (errorMessage is not null)
      {
        // Use of $search
        if (errorMessage.Contains(SearchUnsupportedError))
          return statement;

        // Use of $filter in endsWith
        if (errorMessage.Contains(FilterEndsWithError))
          return statement;

        if (errorMessage.Contains(SortingNotSupportedError))
        {
          // Use of $filter and $orderby in the same query
          // TODO: This may match incorrectly in a complicated script, we should verify the commands we search are graph ones only.
          var parameters = ScriptBlock.Create(statement).Ast.FindAll<CommandParameterAst>().Select(parameter => parameter.ParameterName).ToList();

          if (parameters.Contains("OrderBy", StringComparer.OrdinalIgnoreCase))
            return statement;

          return null;
        }
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
  public const string FilterEndsWithError = @"Operator 'endsWith' is not supported because the 'ConsistencyLevel:eventual' header is missing.";
  public const string SortingNotSupportedError = @"Sorting not supported for current query";
}

public class Init : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
  // Uniquely identifies this provider. This could be randomly generated but having it static helps with tracing/troubleshooting
  Guid ProviderId { get; } = new Guid("c58b84a7-bc73-4bbc-a540-5f5d031cfb0a");
  private AqFeedbackProvider? _provider;
  AqFeedbackProvider provider => _provider ??= new() { Id = ProviderId };

  public void OnImport()
  {
    RegisterSubsystem(FeedbackProvider, provider);
  }
  public void OnRemove(PSModuleInfo psModuleInfo)
  {
    UnregisterSubsystem<IFeedbackProvider>(ProviderId);
  }
}
