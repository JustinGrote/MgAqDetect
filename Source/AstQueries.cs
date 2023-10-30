using System.Management.Automation.Language;

using static System.StringComparison;

namespace MicrosoftGraphAdvancedQueryFeedbackProvider;

public static class AstExtensions
{
  /// <summary>
  /// Finds the first item that matches the predicate
  /// </summary>
  public static T? Find<T>(
    this Ast ast,
    Func<T, bool> predicate,
    bool searchNestedScriptBlock
  ) where T : Ast
  => (T)ast.Find(
    astItem => astItem is T typed
      && predicate(typed),
    searchNestedScriptBlock
  );


  /// <summary>
  /// Finds all items that match the predicate
  /// </summary>
  public static IEnumerable<T> FindAll<T>(
    this Ast ast,
    Func<T, bool> predicate,
    bool searchNestedScriptBlock
  ) where T : Ast
  {
    return ast.FindAll(
      astItem => astItem is T typedItem && predicate(typedItem),
      searchNestedScriptBlock
    ).Select(astItem => (T)astItem);
  }

  /// <summary>
  /// Find items in the AST that match type T
  /// </summary>
  public static IEnumerable<T> FindAll<T>(this Ast ast) where T : Ast => FindAll<T>(ast, a => true, true);
}

public static class MgAstQueries
{
  public static IEnumerable<CommandAst> FindMicrosoftGraphCommands(Ast ast)
  {
    return ast.FindAll<CommandAst>(ast =>
    {
      return ast.CommandElements[0].Extent.Text.Contains("-mg", OrdinalIgnoreCase);
    }, true);
  }


  /// <summary>
  /// Determine if an advanced query is needed for the given command
  /// Source: https://learn.microsoft.com/en-us/graph/aad-advanced-queries?tabs=powershell#query-scenarios-that-require-advanced-query-capabilities
  /// </summary>
  public static bool TestAdvancedQueryNeeded(CommandAst ast)
  {
    var parameters = ast.FindAll<CommandParameterAst>().Select(parameter => parameter.ParameterName).ToList();

    // If the command has ConsistencyLevel and CountVariable already specified return false
    if (parameters.Contains("ConsistencyLevel") && parameters.Contains("CountVariable"))
      return false;

    // If the command only has CountVariable specified, it also needs ConsistencyLevel
    if (parameters.Contains("CountVariable"))
      return true;

    return false;
  }
}
