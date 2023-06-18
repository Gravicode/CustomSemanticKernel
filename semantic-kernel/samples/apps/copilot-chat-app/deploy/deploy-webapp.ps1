<#
.SYNOPSIS
Deploy CopilotChat's WebApp to Azure
#>

param(
    [Parameter(Mandatory)]
    [string]
    # Subscription to which to make the deployment
    $Subscription,

    [Parameter(Mandatory)]
    [string]
    # Resource group to which to make the deployment
    $ResourceGroupName,
    
    [Parameter(Mandatory)]
    [string]
    # Name of the previously deployed Azure deployment 
    $DeploymentName,

    [Parameter(Mandatory)]
    [string]
    # Client application id 
    $ApplicationClientId
)

Write-Host "Setting up Azure credentials..."
az account show --output none
if ($LASTEXITCODE -ne 0) {
    Write-Host "Log into your Azure account"
    az login --output none
}

Write-Host "Setting subscription to '$Subscription'..."
az account set -s $Subscription
if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

Write-Host "Getting deployment outputs..."
$deployment=$(az deployment group show --name $DeploymentName --resource-group $ResourceGroupName --output json | ConvertFrom-Json)
$webappUrl=$deployment.properties.outputs.webappUrl.value
$webappName=$deployment.properties.outputs.webappName.value
$webapiUrl=$deployment.properties.outputs.webapiUrl.value
$webapiName=$deployment.properties.outputs.webapiName.value
$webapiApiKey=($(az webapp config appsettings list --name $webapiName --resource-group $ResourceGroupName | ConvertFrom-JSON) | Where-Object -Property name -EQ -Value Authorization:ApiKey).value
Write-Host "webappUrl: $webappUrl"
Write-Host "webappName: $webappName"
Write-Host "webapiName: $webapiName"
Write-Host "webapiUrl: $webapiUrl"

# Set UTF8 as default encoding for Out-File
$PSDefaultParameterValues['Out-File:Encoding'] = 'ascii'

$envFilePath="$PSScriptRoot/../webapp/.env"
Write-Host "Writing environment variables to '$envFilePath'..."
"REACT_APP_BACKEND_URI=https://$webapiUrl/" | Out-File -FilePath $envFilePath
"REACT_APP_AAD_AUTHORITY=https://login.microsoftonline.com/common" | Out-File -FilePath $envFilePath -Append
"REACT_APP_AAD_CLIENT_ID=$ApplicationClientId" | Out-File -FilePath $envFilePath -Append
"REACT_APP_SK_API_KEY=$webapiApiKey" | Out-File -FilePath $envFilePath -Append

$swaConfig = $(Get-Content "$PSScriptRoot/../webapp/template.swa-cli.config.json" -Raw) 
$swaConfig = $swaConfig.Replace("{{appDevserverUrl}}", "https://$webappUrl") 
$swaConfig | Out-File -FilePath "$PSScriptRoot/../webapp/swa-cli.config.json"
Write-Host $(Get-Content "$PSScriptRoot/../webapp/swa-cli.config.json" -Raw)

Push-Location -Path "$PSScriptRoot/../webapp"
Write-Host "Installing yarn dependencies..."
yarn install
if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

Write-Host "Building webapp..."
swa build
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Deploying webapp..."
swa deploy --subscription-id $Subscription --app-name $webappName --env production
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$origin = "https://$webappUrl"
Write-Host "Ensuring CORS origin '$origin' to webapi '$webapiName'..."
if (-not ((az webapp cors show --name webapiName --resource-group $ResourceGroupName --subscription $Subscription | ConvertFrom-Json).allowedOrigins -contains $origin)) {
    az webapp cors add --name $webapiName --resource-group $ResourceGroupName --subscription $Subscription --allowed-origins $origin
}

Pop-Location

Write-Host "To verify your deployment, go to 'https://$webappUrl' in your browser."
