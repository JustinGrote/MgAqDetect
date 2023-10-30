
using MicrosoftGraphAdvancedQueryFeedbackProvider;
using static MicrosoftGraphAdvancedQueryFeedbackProvider.AqFeedbackProvider;
using System.Management.Automation;
using System.Management.Automation.Subsystem.Feedback;
using System.Management.Automation.Language;

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
  public void GetFeedback_ReturnsCorrectResult(string script, string filter, string errorId, FeedbackItem? expected)
  {
    // Arrange
    AqFeedbackProvider provider = new();

    FeedbackContextAdapter contextMock = Substitute.For<FeedbackContextAdapter>();
    contextMock.CommandLineAst.Returns(ScriptBlock.Create(script).Ast);
    contextMock.ErrorId.Returns(errorId);
    contextMock.ErrorCommand.Returns(script);
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

public class FeedbackProviderErrorTestsData : TheoryData<string, string, string, FeedbackItem?>
{
  public FeedbackProviderErrorTestsData()
  {
    Add(@"Get-MgUser -Filter assignedLicenses/$count eq 0", "assignedLicenses/$count eq 0", "Request_BadRequest,Get_MgUser", CreateAqFeedbackItem(["Get-MgUser -Filter assignedLicenses/$count eq 0"]));
  }
}