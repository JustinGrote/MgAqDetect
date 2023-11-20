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
      var invokeResult = ps.AddScript(script)
        .Invoke(input: null, new() { AddToHistory = true });

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

public class FeedbackProviderTestsData : TheoryData<string, string, FeedbackItem?>
{
  public FeedbackProviderTestsData()
  {
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
    // Add(
    //   @"function Get-MgUser {Write-Error -ErrorRecord $([ErrorRecord]::new([Exception]::new(),
    //     'Request_BadRequest',
    //     'InvalidOperation',
    //     'assignedLicenses/$count eq 0'
    //   ))}",
    //   @"Write-Error 'oops'",
    //   CreateAqFeedbackItem(["Get-MgUser -Filter assignedLicenses/$count eq 0"])
    // );
  }
}

public class FeedbackProviderErrorTestsData : TheoryData<string, string, string, string, FeedbackItem?>
{
  public FeedbackProviderErrorTestsData()
  {
    // Use of $count in a filter expression
    Add(
      @"Get-MgUser -Filter assignedLicenses/$count eq 0",
      "assignedLicenses/$count eq 0",
      "Request_BadRequest,Get_MgUser",
      "FakeMessage",
      CreateAqFeedbackItem(["Get-MgUser -Filter assignedLicenses/$count eq 0"])
    );
    Add(
      @"Get-MgUser -Filter assignedLicenses/$count eq 0 -CountVariable cv -ConsistencyLevel Eventual",
      string.Empty,
      string.Empty,
      string.Empty,
      null
    );

    // Use of $search
    Add(
      @"Get-MgUser -Search ""displayName:John""",
      string.Empty,
      "Request_UnsupportedQuery,Get_MgUser",
      SearchUnsupportedError,
      CreateAqFeedbackItem([@"Get-MgUser -Search ""displayName:John"""])
    );
    Add(
      @"Get-MgUser -Search ""displayName:John"" -CountVariable cv -ConsistencyLevel Eventual",
      string.Empty,
      string.Empty,
      string.Empty,
      null
    );

    // Use of $endsWith
    Add(
      @"Get-MgUser -filter ""endsWith(mail, '@outlook.com')""",
      string.Empty,
      "Request_UnsupportedQuery,Get_MgUser",
      FilterEndsWithError,
      CreateAqFeedbackItem([@"Get-MgUser -filter ""endsWith(mail, '@outlook.com')"""])
    );
    Add(
      @"Get-MgUser -filter ""endsWith(mail, '@outlook.com')"" -CountVariable cv -ConsistencyLevel Eventual",
      string.Empty,
      string.Empty,
      string.Empty,
      null
    );

    // Use of $filter and $orderby in the same query
    Add(
      @"Get-MgUser -filter ""displayname eq 'test'"" -orderby ""displayname""",
      string.Empty,
      "Request_UnsupportedQuery,Get_MgUser",
      SortingNotSupportedError,
      CreateAqFeedbackItem([@"Get-MgUser -filter ""displayname eq 'test'"" -orderby ""displayname"""])
    );
    Add(
      @"Get-MgUser -filter ""displayname eq 'test'"" -orderby ""displayname"" -CountVariable cv -ConsistencyLevel Eventual",
      string.Empty,
      string.Empty,
      string.Empty,
      null
    );

    // Use of ne operator
    Add(
      @"Get-MgUser -filter ""displayname ne null""",
      string.Empty,
      "Request_UnsupportedQuery,Get_MgUser",
      NotEqualsMatch,
      CreateAqFeedbackItem([@"Get-MgUser -filter ""displayname ne null"""])
    );
    Add(
      @"Get - MgUser - filter ""displayname ne null""-CountVariable cv -ConsistencyLevel Eventual",
      string.Empty,
      string.Empty,
      string.Empty,
      null
    );

    // Use of NOT operator
    Add(
      @"Get-MgUser -filter ""NOT(displayname eq 'test')""",
      string.Empty,
      "Request_UnsupportedQuery,Get_MgUser",
      ConsistencyHeaderMissingError,
      CreateAqFeedbackItem([@"Get-MgUser -filter ""NOT(displayname eq 'test')"""])
    );
    Add(
      @"Get-MgUser -filter ""NOT(displayname eq 'test')"" -CountVariable cv -ConsistencyLevel Eventual",
      string.Empty,
      string.Empty,
      string.Empty,
      null
    );

    // Use of NOT and StartsWith operator
    Add(
      @"Get-MgUser -filter ""NOT (startswith(displayname, 'test'))""",
      string.Empty,
      "Request_UnsupportedQuery,Get_MgUser",
      ConsistencyHeaderMissingError,
      CreateAqFeedbackItem([@"Get-MgUser -filter ""NOT (startswith(displayname, 'test'))"""])
    );
    Add(
      @"Get-MgUser -filter ""NOT (startswith(displayname, 'test'))"" -CountVariable cv -ConsistencyLevel Eventual",
      string.Empty,
      string.Empty,
      string.Empty,
      null
    );

  }
}