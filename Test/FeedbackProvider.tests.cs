
using MicrosoftGraphAdvancedQueryFeedbackProvider;

using System.Management.Automation;
using System.Management.Automation.Subsystem.Feedback;

using static MicrosoftGraphAdvancedQueryFeedbackProvider.AqFeedbackProvider;
using static MicrosoftGraphAdvancedQueryFeedbackProvider.Strings;

namespace Test;

public class FeedbackProviderSuccessTests
{
  [Theory]
  [ClassData(typeof(FeedbackProviderSuccessTestsData))]
  public void GetFeedback_ReturnsCorrectResult(string script, FeedbackItem? expected)
  {
    // Arrange
    AqFeedbackProvider provider = new();
    var contextMock = Substitute.For<FeedbackContextAdapter>();
    contextMock.CommandLineAst.Returns(ScriptBlock.Create(script).Ast);

    // Act
    var actual = provider.GetFeedbackImpl(contextMock);

    // Assert
    actual.Should().BeEquivalentTo(expected);
  }
}

public class FeedbackProviderSuccessTestsData : TheoryData<string, FeedbackItem?>
{
  public FeedbackProviderSuccessTestsData()
  {
    Add("Get-Nothing", null);
    Add("Get-MgUser", null);
    Add("Get-MgUser -CountVariable cv -ConsistencyLevel Eventual", null);
    Add(
      "Get-MgUser -CountVariable",
      CreateAqFeedbackItem(["Get-MgUser -CountVariable"])
    );
  }
}

public class FeedbackProviderErrorTests
{
  [Theory]
  [ClassData(typeof(FeedbackProviderErrorTestsData))]
  public void GetFeedback_ReturnsCorrectResult(string script, string filter, string errorId, string errorMessage, FeedbackItem? expected)
  {
    // Arrange
    AqFeedbackProvider provider = new();

    FeedbackContextAdapter contextMock = Substitute.For<FeedbackContextAdapter>();
    contextMock.CommandLineAst.Returns(ScriptBlock.Create(script).Ast);
    contextMock.ErrorId.Returns(errorId);
    contextMock.ErrorCommand.Returns(script);
    contextMock.ErrorMessage.Returns(errorMessage);
    contextMock.ErrorTarget.Returns(new
    {
      Filter = filter
    });

    // Act
    var actual = provider.GetFeedbackImpl(contextMock);

    // Assert
    actual.Should().BeEquivalentTo(expected);
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


  }
}