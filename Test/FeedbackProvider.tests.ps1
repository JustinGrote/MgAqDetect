#requires -version 7.4
using namespace System.Management.Automation
using namespace System.Management.Automation.Subsystem.Feedback

BeforeAll {
  <#
  .SYNOPSIS
  Adds a function to an initial session state for the purposes of testing. It can be pipelined.
  .EXAMPLE
  $state = Add-Fake 'Get-MgUser' { @{ displayName = 'Test User' } }
  .EXAMPLE
  $state = Add-Fake 'Get-MgUser' { @{ displayName = 'Test User' } }
  | Add-Fake 'Get-MgApplication' { @{ displayName = 'Test App' } }
  | Add-Fake 'Get-MgGroup' { @{ displayName = 'Test Group' } }
  #>
  function SCRIPT:Add-Fake {
    [CmdletBinding()]
    param(
      [Parameter(Mandatory)][string]$Name,
      [ScriptBlock]$Mock = {},
      [Parameter(ValueFromPipeline)][initialsessionstate]$state = [initialsessionstate]::CreateDefault()
    )

    $command = [Management.Automation.Runspaces.SessionStateFunctionEntry]::new($Name, $Mock)
    $state.Commands.Add($command)
    return $state
  }

  function SCRIPT:Test-FeedbackProvider {
    [OutputType([Management.Automation.Subsystem.Feedback.FeedbackResult])]
    param(
      [Parameter(Mandatory)][ScriptBlock]$Script,
      [string]$ProviderId,
      [Parameter(Mandatory, ValueFromPipeline)][initialsessionstate]$State
    )
    try {
      $ps = [powershell]::Create($state)
      [void]$ps.AddScript('Get-MgUser -CountVariable cv').Invoke(
        $null,
        [PSInvocationSettings]@{ AddToHistory = $true }
      )

      return [FeedbackHub]::GetFeedback($ps.runspace, [int]::MaxValue)
      | Where-Object Id -EQ $ProviderId
      | ForEach-Object Item
    } finally {
      $ps.Runspace.Dispose()
      $ps.Dispose()
    }
  }

  Add-Type -Path $PSScriptRoot/../Release/MgAqDetect.dll
  $SCRIPT:fpInit = [MicrosoftGraphAdvancedQueryFeedbackProvider.Init]::new()
}

Describe 'Feedback Provider Test' {
  BeforeAll {
    #Register the feedback provider via module init
    $fpInit.OnImport()
  }
  It 'Get-MgUser with only count variable should return feedback' {
    $feedbackItems = Add-Fake 'Get-MgUser'
    | Test-FeedbackProvider -Script { Get-MgUser -CountVariable cv } -ProviderId $fpInit.ProviderId

    $feedbackItems | Should -HaveCount 1
    $feedbackItems.RecommendedActions | Should -HaveCount 1
    $feedbackItems.RecommendedActions[0] | Should -Be 'Get-MgUser -CountVariable cv'
  }
  AfterAll {
    #Unregister the feedback provider via module unload
    $fpInit.OnRemove($null)
  }
}
