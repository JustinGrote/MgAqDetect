using System.Runtime.CompilerServices;
using System.Management.Automation;
using System.Management.Automation.Subsystem.Feedback;
using static System.Management.Automation.Subsystem.SubsystemManager;
using static System.Management.Automation.Subsystem.SubsystemKind;
using static JMg.MgAstQueries;
using System.Management.Automation.Language;
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
  public ErrorRecord? LastError { get; }
}

class PSFeedbackContextAdapter(FeedbackContext context) : FeedbackContextAdapter
{
  public Ast CommandLineAst => context.CommandLineAst;
  public string CommandLine => context.CommandLine;
  public IReadOnlyList<Token> CommandLineTokens => context.CommandLineTokens;
  public string CurrentLocation => context.CurrentLocation.ToString();
  public ErrorRecord? LastError => context.LastError;
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

  FeedbackTrigger IFeedbackProvider.Trigger { get; } = FeedbackTrigger.Success;

  public FeedbackItem? GetFeedback(FeedbackContext context, CancellationToken token)
    => GetFeedbackImpl(new PSFeedbackContextAdapter(context));
  #endregion

  /// <summary>
  /// This is the actual implementation of the feedback provider. It is separated for testing because the public interface has a <see langword="sealed"/> parameter that cannot be construted.
  /// </summary>
  ///
  internal FeedbackItem? GetFeedbackImpl(FeedbackContextAdapter context)
  {
    Console.WriteLine("GetFeedback!");
    var graphCommands = FindMicrosoftGraphCommands(context.CommandLineAst)
      .Where(TestAdvancedQueryNeeded);

    if (!graphCommands.Any())
      return null;

    return CreateAqFeedbackItem(
      graphCommands.Select(command => command.ToString())
    );
  }

  /// <summary>
  /// Generates an Advanced Query FeedBackItem with the header and footer pre-populated
  /// </summary>
  static public FeedbackItem CreateAqFeedbackItem(IEnumerable<string> commands)
    => new(
      AdvancedQueryFeedbackMessages.AdvancedQueryHeader,
      commands.ToList(),
      AdvancedQueryFeedbackMessages.AdvancedQueryFooter,
      FeedbackDisplayLayout.Portrait
    );
}

/// <summary>
/// A feedback provider specifically for handling cases where an error was returned
/// </summary>
public class AqErrorFeedbackProvider : AqFeedbackProvider, IFeedbackProvider
{
  new public string Name
  {
    get { return base.Name + " Errors"; }
  }
  FeedbackTrigger IFeedbackProvider.Trigger { get; } = FeedbackTrigger.Error;
}

public static class AdvancedQueryFeedbackMessages
{
  public const string AdvancedQueryHeader = "The following command combinations were detected as needing Advanced Query Capabilities. Ensure you add -CountVariable CountVar and -ConsistencyLevel Eventual to these commands or you may get unexpected errors or empty results";
  public const string AdvancedQueryFooter = "More: https://learn.microsoft.com/en-us/graph/aad-advanced-queries?tabs=powershell";
}

public class Init : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
  // Uniquely identifies this provider. This could be randomly generated but having it static helps with tracing/troubleshooting
  Guid ProviderId { get; } = new Guid("c58b84a7-bc73-4bbc-a540-5f5d031cfb0a");
  Guid ErrorProviderId { get; } = new Guid("8c8c1a25-22ae-46f9-bfb0-0571ae05ba8c");


  private AqFeedbackProvider? _provider;
  AqFeedbackProvider provider => _provider ??= new() { Id = ProviderId };
  private AqErrorFeedbackProvider? _errorProvider;
  AqErrorFeedbackProvider errorProvider => _errorProvider ??= new() { Id = ErrorProviderId };

  public void OnImport()
  {
    RegisterSubsystem(FeedbackProvider, provider);
    RegisterSubsystem(FeedbackProvider, errorProvider);
  }
  public void OnRemove(PSModuleInfo psModuleInfo)
  {
    UnregisterSubsystem<IFeedbackProvider>(ProviderId);
    UnregisterSubsystem<IFeedbackProvider>(ErrorProviderId);
  }
}
