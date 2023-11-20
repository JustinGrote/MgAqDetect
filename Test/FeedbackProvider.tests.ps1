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
      [void]$ps.AddScript($Script).Invoke(
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

Describe 'Feedback Provider Tests' {
  BeforeAll {
    #Register the feedback provider via module init
    $fpInit.OnImport()
  }

  Context 'AST Based Tests' {
    It 'Non Mg Command' {
      $actual = Add-Fake 'Get-ChildItem' {}
      | Test-FeedbackProvider -Script { Get-ChildItem -Path 'C:\' } -ProviderId $fpInit.ProviderId

      $actual | Should -BeNullOrEmpty
    }
    It 'No Feedback Needed' {
      $actual = Add-Fake 'Get-MgUser' {}
      | Test-FeedbackProvider -Script { Get-MgUser } -ProviderId $fpInit.ProviderId
      $actual | Should -BeNullOrEmpty
    }
    It 'Get-MgUser with only count variable' {
      $actual = Add-Fake 'Get-MgUser'
      | Test-FeedbackProvider -Script { Get-MgUser -CountVariable cv } -ProviderId $fpInit.ProviderId

      $actual | Should -HaveCount 1
      $actual.RecommendedActions | Should -HaveCount 1
      $actual.RecommendedActions[0] | Should -Be 'Get-MgUser -CountVariable cv'
    }
    It 'Get-MgUser with only count variable: FIXED' {
      $actual = Add-Fake 'Get-MgUser'
      | Test-FeedbackProvider -Script { Get-MgUser -CountVariable cv -ConsistencyLevel eventual } -ProviderId $fpInit.ProviderId

      $actual | Should -BeNullOrEmpty
    }
  }

  AfterAll {
    #Unregister the feedback provider via module unload
    $fpInit.OnRemove($null)
  }
}
