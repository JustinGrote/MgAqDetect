using MicrosoftGraphAdvancedQueryFeedbackProvider;

using static MicrosoftGraphAdvancedQueryFeedbackProvider.AqFeedbackProvider;
using static MicrosoftGraphAdvancedQueryFeedbackProvider.Strings;

using System.Management.Automation;
using System.Management.Automation.Subsystem.Feedback;

namespace Test;

public class FeedbackProviderTests
{
  [Theory]
  [ClassData(typeof(FeedbackProviderTestsData))]
  public void FeedbackProviderE2E(string mock, string script, FeedbackItem? expected)
  {
    // Arrange
    using var ps = PowerShell.Create();

    // Add the mock function to the runspace.
    ps.AddScript(mock).Invoke();
    ps.Commands.Clear();

    // Register the feedback provider using its own module import mechanism.
    Init init = new();
    init.OnImport();

    try
    {
      // Act
      try
      {
        var invokeResult = ps.AddScript(script)
          .Invoke(input: null, new() { AddToHistory = true });
      }
      catch (Exception err)
      {
        // Ignore terminating errors in the Invocation, any that occur should get captured into the LastError feedback. You can breakpoint this step to debug what error is occuring if necessary.
        var error = err;
      }

      //MaxValue timeout is used to allow for debugging
      var feedbackResult = FeedbackHub.GetFeedback(ps.Runspace, int.MaxValue) ?? [];

      // Assert
      if (expected is null)
      {
        feedbackResult.Should().BeEmpty();
        return;
      }

      var feedbackItems = from f in feedbackResult
                          where f.Id == init.ProviderId
                          select f.Item;
      feedbackItems.Should().HaveCount(1);
      feedbackItems.Single().Should().BeEquivalentTo(expected);
    }
    finally
    {
      // Subsystem cleanup
      init.OnRemove(null);
    }
  }
}

// This is used inside the runspace to emulate a TargetObject
public record FakeTargetObject(string Filter);

public class FeedbackProviderTestsData : TheoryData<string, string, FeedbackItem?>
{
  /// <summary>
  /// Creates a function mock that accepts any parameters
  /// </summary>
  const string CommandMockTemplate = @"function {0} {{
    [CmdletBinding()]
    param(
      [Parameter(ValueFromRemainingArguments=$true)]
      [string]$CapturedArgs
    )
    {1}
  }}";

  /// <summary>
  /// This is needed to make the InvocationInfo get generated properly for the command test
  /// </summary>
  const string ErrorActionTemplate = @"
    $ErrorRecord = [Management.Automation.ErrorRecord]::new(
      [Exception]::new('{0}'),
      '{1}',
      'InvalidOperation',
      $({2})
    )
    $PSCmdlet.ThrowTerminatingError($ErrorRecord)
  ";

  string GetCommandMock(string command, string? action)
  => string.Format(CommandMockTemplate, command, action);

  string GetErrorMock(string command, string? message, string? errorId, string? targetObjectScript)
  => GetCommandMock(command, string.Format(ErrorActionTemplate, message?.Replace("'", "''"), errorId, targetObjectScript));

  string GetErrorMockWithFilter(string command, string? message, string? errorId, string? filter)
  => GetErrorMock(command, message, errorId, $"[Test.FakeTargetObject]::new('{filter?.Replace("'", "''")}')");

  public FeedbackProviderTestsData()
  {
    string script;
    string getMgUserMock = GetCommandMock("Get-MgUser", null);
    // No feedback needed
    Add(
      "function Get-Nothing {}",
      "Get-Nothing",
      null
    );
    // No feedback needed
    Add(
      "function Get-MgUser {}",
      "Get-MgUser",
      null
    );
    // Already using correct params
    Add(
      "function Get-MgUser {}",
      "Get-MgUser -CountVariable cv -ConsistencyLevel Eventual",
      null
    );
    // Missing ConsistencyLevel Eventual and will error
    Add(
      "function Get-MgUser {}",
      "Get-MgUser -CountVariable cv",
      CreateAqFeedbackItem(["Get-MgUser -CountVariable cv"])
    );

    // Error: Use of $count in a filter expression
    Add(
      GetErrorMockWithFilter(
        "Get-MgUser",
        "FakeMessage",
        "Request_BadRequest,Get_MgUser",
        "assignedLicenses/$count eq 0"
      ),
      @"Get-MgUser -Filter 'assignedLicenses/$count eq 0'",
      CreateAqFeedbackItem(["Get-MgUser -Filter 'assignedLicenses/$count eq 0'"])
    );
    // Fixed
    Add(
      getMgUserMock,
      "Get-MgUser -Filter assignedLicenses/$count eq 0 -CountVariable cv -ConsistencyLevel Eventual",
      null
    );

    // Error: Use of $search
    script = @"Get-MgUser -Search ""displayName:John""";
    Add(
      GetErrorMockWithFilter(
        "Get-MgUser",
        SearchUnsupportedError,
        "Request_UnsupportedQuery,Get_MgUser",
        null
      ),
      script,
      CreateAqFeedbackItem([script])
    );
    // Fixed
    Add(
      getMgUserMock,
      $"{script} -CountVariable cv -ConsistencyLevel Eventual",
      null
    );

    // Error: Use of $endsWith
    script = @"Get-MgUser -filter ""endsWith(mail, '@outlook.com')""";
    Add(
      GetErrorMockWithFilter(
        "Get-MgUser",
        FilterEndsWithError,
        "Request_UnsupportedQuery,Get_MgUser",
        null
      ),
      script,
      CreateAqFeedbackItem([script])
    );
    // Fixed
    Add(
      getMgUserMock,
      $"{script} -CountVariable cv -ConsistencyLevel Eventual",
      null
    );

    // Error: Use of $filter and $orderby in the same query
    script = @"Get-MgUser -filter ""displayname eq 'test'"" -orderby ""displayname""";
    Add(
      GetErrorMockWithFilter(
        "Get-MgUser",
        SortingNotSupportedError,
        "Request_UnsupportedQuery,Get_MgUser",
        null
      ),
      script,
      CreateAqFeedbackItem([script])
    );
    // Fixed
    Add(
      getMgUserMock,
      $"{script} -CountVariable cv -ConsistencyLevel Eventual",
      null
    );

    // Error: Use of ne operator
    script = @"Get-MgUser -filter ""displayname ne null""";
    Add(
      GetErrorMockWithFilter(
        "Get-MgUser",
        NotEqualsMatch,
        "Request_UnsupportedQuery,Get_MgUser",
        null
      ),
      script,
      CreateAqFeedbackItem([script])
    );
    // Fixed
    Add(
      getMgUserMock,
      $"{script} -CountVariable cv -ConsistencyLevel Eventual",
      null
    );

    // Error: Use of NOT operator
    script = @"Get-MgUser -filter ""NOT(displayname eq 'test')""";
    Add(
      GetErrorMockWithFilter(
        "Get-MgUser",
        ConsistencyHeaderMissingError,
        "Request_UnsupportedQuery,Get_MgUser",
        null
      ),
      script,
      CreateAqFeedbackItem([script])
    );
    // Fixed
    Add(
      getMgUserMock,
      $"{script} -CountVariable cv -ConsistencyLevel Eventual",
      null
    );

    // Error: Use of NOT and StartsWith operator
    script = @"Get-MgUser -filter ""NOT (startswith(displayname, 'test'))""";
    Add(
      GetErrorMockWithFilter(
        "Get-MgUser",
        ConsistencyHeaderMissingError,
        "Request_UnsupportedQuery,Get_MgUser",
        null
      ),
      script,
      CreateAqFeedbackItem([script])
    );
    // Fixed
    Add(
      getMgUserMock,
      $"{script} -CountVariable cv -ConsistencyLevel Eventual",
      null
    );

  }
}