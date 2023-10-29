
using MicrosoftGraphAdvancedQueryFeedbackProvider;
using static MicrosoftGraphAdvancedQueryFeedbackProvider.AqFeedbackProvider;
using System.Management.Automation;
using System.Management.Automation.Subsystem.Feedback;

namespace Test;

public class FeedbackProviderTests
{
  [Theory]
  [ClassData(typeof(FeedbackProviderTestsData))]
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

public class FeedbackProviderTestsData : TheoryData<string, FeedbackItem?>
{
  public FeedbackProviderTestsData()
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
