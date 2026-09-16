[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
else {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

$admxPath = Join-Path $RepositoryRoot 'policy\GPCHealthCheck.admx'
$enPath = Join-Path $RepositoryRoot 'policy\en-US\GPCHealthCheck.adml'
$ruPath = Join-Path $RepositoryRoot 'policy\ru-RU\GPCHealthCheck.adml'

foreach ($path in @($admxPath, $enPath, $ruPath)) {
    Require (Test-Path -LiteralPath $path -PathType Leaf) "Required policy artifact is missing: $path"
}

function Load-Xml([string]$Path) {
    try {
        return [xml][System.IO.File]::ReadAllText($Path)
    }
    catch {
        throw "Policy XML is not well-formed: $Path :: $($_.Exception.Message)"
    }
}

function New-PolicyNamespaceManager([xml]$Xml) {
    $manager = [System.Xml.XmlNamespaceManager]::new($Xml.NameTable)
    $manager.AddNamespace('p', 'http://schemas.microsoft.com/GroupPolicy/2006/07/PolicyDefinitions')
    return $manager
}

$admx = Load-Xml $admxPath
$admxNs = New-PolicyNamespaceManager $admx
$target = $admx.SelectSingleNode('/p:policyDefinitions/p:policyNamespaces/p:target', $admxNs)
Require ($null -ne $target) 'ADMX target namespace is missing.'
Require ($target.GetAttribute('prefix') -eq 'gpc') 'ADMX target prefix must be gpc.'
Require ($target.GetAttribute('namespace') -eq 'G.PcHealthCheck.Policies') 'ADMX target namespace drifted.'

$rootCategory = $admx.SelectSingleNode("/p:policyDefinitions/p:categories/p:category[@name='GPCHealthCheck']", $admxNs)
$securityCategory = $admx.SelectSingleNode("/p:policyDefinitions/p:categories/p:category[@name='GPCHealthCheckSecurity']", $admxNs)
Require ($null -ne $rootCategory) 'G PC Health Check ADMX root category is missing.'
Require ($null -ne $securityCategory) 'Security Posture ADMX category is missing.'
$categoryParent = $securityCategory.SelectSingleNode("p:parentCategory[@ref='GPCHealthCheck']", $admxNs)
Require ($null -ne $categoryParent) 'Security Posture category must be under G PC Health Check.'

$policy = $admx.SelectSingleNode("/p:policyDefinitions/p:policies/p:policy[@name='AllowedLocalAdministrators']", $admxNs)
Require ($null -ne $policy) 'AllowedLocalAdministrators machine policy is missing.'
Require ($policy.GetAttribute('class') -eq 'Machine') 'AllowedLocalAdministrators must be machine-scoped.'
Require ($policy.GetAttribute('key') -eq 'SOFTWARE\Policies\GPCHealthCheck') 'ADMX policy Registry key drifted.'
Require ($policy.GetAttribute('displayName') -eq '$(string.Policy.AllowedLocalAdministrators)') 'ADMX displayName resource reference drifted.'
Require ($policy.GetAttribute('explainText') -eq '$(string.Policy.AllowedLocalAdministrators.Help)') 'ADMX explainText resource reference drifted.'
Require ($policy.GetAttribute('presentation') -eq '$(presentation.Policy.AllowedLocalAdministrators.Presentation)') 'ADMX presentation resource reference drifted.'
$parent = $policy.SelectSingleNode("p:parentCategory[@ref='GPCHealthCheckSecurity']", $admxNs)
Require ($null -ne $parent) 'AllowedLocalAdministrators must live under Security Posture.'

$multi = $policy.SelectSingleNode("p:elements/p:multiText[@id='AllowedLocalAdministratorsList']", $admxNs)
Require ($null -ne $multi) 'AllowedLocalAdministrators must use a multiText element.'
Require ($multi.GetAttribute('valueName') -eq 'AllowedLocalAdministrators') 'multiText Registry value name drifted.'
Require ($multi.GetAttribute('required') -ne 'true') 'Allow-list editor must permit an intentionally empty effective list.'

$requiredStringIds = @(
    'Category.GPCHealthCheck',
    'Category.SecurityPosture',
    'Policy.AllowedLocalAdministrators',
    'Policy.AllowedLocalAdministrators.Help',
    'Policy.AllowedLocalAdministrators.Label'
)

foreach ($item in @(
    @{ Path = $enPath; Language = 'en-US' },
    @{ Path = $ruPath; Language = 'ru-RU' }
)) {
    $adml = Load-Xml $item.Path
    $admlNs = New-PolicyNamespaceManager $adml
    foreach ($id in $requiredStringIds) {
        $node = $adml.SelectSingleNode("/p:policyDefinitionResources/p:resources/p:stringTable/p:string[@id='$id']", $admlNs)
        Require ($null -ne $node -and -not [string]::IsNullOrWhiteSpace($node.InnerText)) "Missing $($item.Language) ADML string: $id"
    }

    $presentation = $adml.SelectSingleNode("/p:policyDefinitionResources/p:resources/p:presentationTable/p:presentation[@id='Policy.AllowedLocalAdministrators.Presentation']", $admlNs)
    Require ($null -ne $presentation) "Missing $($item.Language) AllowedLocalAdministrators presentation."
    $box = $presentation.SelectSingleNode("p:multiTextBox[@refId='AllowedLocalAdministratorsList']", $admlNs)
    Require ($null -ne $box) "$($item.Language) presentation must bind multiTextBox to AllowedLocalAdministratorsList."

    $help = $adml.SelectSingleNode("/p:policyDefinitionResources/p:resources/p:stringTable/p:string[@id='Policy.AllowedLocalAdministrators.Help']", $admlNs).InnerText
    Require ($help.Contains('*')) "$($item.Language) help must document * wildcard semantics."
    Require ($help.Contains('?')) "$($item.Language) help must document ? wildcard semantics."
    Require ($help.Contains('REG_MULTI_SZ')) "$($item.Language) help must document REG_MULTI_SZ delivery."
}

$allText = ([System.IO.File]::ReadAllText($admxPath) + "`n" + [System.IO.File]::ReadAllText($enPath) + "`n" + [System.IO.File]::ReadAllText($ruPath)).ToLowerInvariant()
foreach ($organizationSpecific in @('gradient.ru', 'градиент', 'indaspace', 'bajoicheg')) {
    Require (-not $allText.Contains($organizationSpecific)) "Organization-specific text is forbidden in policy artifacts: $organizationSpecific"
}

Write-Host 'G PC Health Check ADMX/ADML contract: PASS'
