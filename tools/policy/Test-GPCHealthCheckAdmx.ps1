[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$PolicyNamespace = 'http://schemas.microsoft.com/GroupPolicy/2006/07/PolicyDefinitions'

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
        [xml]$document = [System.IO.File]::ReadAllText($Path)
        return ,$document
    }
    catch {
        throw "Policy XML is not well-formed: $Path :: $($_.Exception.Message)"
    }
}

function Select-PolicyNode([xml]$Xml, [string]$XPath) {
    $match = Select-Xml -Xml $Xml -XPath $XPath -Namespace @{ p = $PolicyNamespace } | Select-Object -First 1
    if ($null -eq $match) { return $null }
    return $match.Node
}

$admx = Load-Xml $admxPath
$target = Select-PolicyNode $admx '/p:policyDefinitions/p:policyNamespaces/p:target'
Require ($null -ne $target) 'ADMX target namespace is missing.'
Require ($target.GetAttribute('prefix') -eq 'gpc') 'ADMX target prefix must be gpc.'
Require ($target.GetAttribute('namespace') -eq 'G.PcHealthCheck.Policies') 'ADMX target namespace drifted.'

$rootCategory = Select-PolicyNode $admx "/p:policyDefinitions/p:categories/p:category[@name='GPCHealthCheck']"
$securityCategory = Select-PolicyNode $admx "/p:policyDefinitions/p:categories/p:category[@name='GPCHealthCheckSecurity']"
Require ($null -ne $rootCategory) 'G PC Health Check ADMX root category is missing.'
Require ($null -ne $securityCategory) 'Security Posture ADMX category is missing.'
$categoryParent = Select-PolicyNode $admx "/p:policyDefinitions/p:categories/p:category[@name='GPCHealthCheckSecurity']/p:parentCategory[@ref='GPCHealthCheck']"
Require ($null -ne $categoryParent) 'Security Posture category must be under G PC Health Check.'

$policy = Select-PolicyNode $admx "/p:policyDefinitions/p:policies/p:policy[@name='AllowedLocalAdministrators']"
Require ($null -ne $policy) 'AllowedLocalAdministrators machine policy is missing.'
Require ($policy.GetAttribute('class') -eq 'Machine') 'AllowedLocalAdministrators must be machine-scoped.'
Require ($policy.GetAttribute('key') -eq 'SOFTWARE\Policies\GPCHealthCheck') 'ADMX policy Registry key drifted.'
Require ($policy.GetAttribute('displayName') -eq '$(string.Policy.AllowedLocalAdministrators)') 'ADMX displayName resource reference drifted.'
Require ($policy.GetAttribute('explainText') -eq '$(string.Policy.AllowedLocalAdministrators.Help)') 'ADMX explainText resource reference drifted.'
Require ($policy.GetAttribute('presentation') -eq '$(presentation.Policy.AllowedLocalAdministrators.Presentation)') 'ADMX presentation resource reference drifted.'
$parent = Select-PolicyNode $admx "/p:policyDefinitions/p:policies/p:policy[@name='AllowedLocalAdministrators']/p:parentCategory[@ref='GPCHealthCheckSecurity']"
Require ($null -ne $parent) 'AllowedLocalAdministrators must live under Security Posture.'

$multi = Select-PolicyNode $admx "/p:policyDefinitions/p:policies/p:policy[@name='AllowedLocalAdministrators']/p:elements/p:multiText[@id='AllowedLocalAdministratorsList']"
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
    foreach ($id in $requiredStringIds) {
        $node = Select-PolicyNode $adml "/p:policyDefinitionResources/p:resources/p:stringTable/p:string[@id='$id']"
        Require ($null -ne $node -and -not [string]::IsNullOrWhiteSpace($node.InnerText)) "Missing $($item.Language) ADML string: $id"
    }

    $presentation = Select-PolicyNode $adml "/p:policyDefinitionResources/p:resources/p:presentationTable/p:presentation[@id='Policy.AllowedLocalAdministrators.Presentation']"
    Require ($null -ne $presentation) "Missing $($item.Language) AllowedLocalAdministrators presentation."
    $box = Select-PolicyNode $adml "/p:policyDefinitionResources/p:resources/p:presentationTable/p:presentation[@id='Policy.AllowedLocalAdministrators.Presentation']/p:multiTextBox[@refId='AllowedLocalAdministratorsList']"
    Require ($null -ne $box) "$($item.Language) presentation must bind multiTextBox to AllowedLocalAdministratorsList."

    $helpNode = Select-PolicyNode $adml "/p:policyDefinitionResources/p:resources/p:stringTable/p:string[@id='Policy.AllowedLocalAdministrators.Help']"
    $help = $helpNode.InnerText
    Require ($help.Contains('*')) "$($item.Language) help must document * wildcard semantics."
    Require ($help.Contains('?')) "$($item.Language) help must document ? wildcard semantics."
    Require ($help.Contains('REG_MULTI_SZ')) "$($item.Language) help must document REG_MULTI_SZ delivery."
}

$allText = ([System.IO.File]::ReadAllText($admxPath) + "`n" + [System.IO.File]::ReadAllText($enPath) + "`n" + [System.IO.File]::ReadAllText($ruPath)).ToLowerInvariant()
foreach ($organizationSpecific in @('gradient.ru', 'градиент', 'indaspace', 'bajoicheg')) {
    Require (-not $allText.Contains($organizationSpecific)) "Organization-specific text is forbidden in policy artifacts: $organizationSpecific"
}

Write-Host 'G PC Health Check ADMX/ADML contract: PASS'
