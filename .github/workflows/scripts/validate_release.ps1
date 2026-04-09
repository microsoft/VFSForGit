param(
	[Parameter(Mandatory=$true)]
	[string]$Tag,

	[Parameter(Mandatory=$true)]
	[string]$Repository
)

function Write-GitHubActionsCommand {
	param(
		[Parameter(Mandatory=$true)]
		[string]$Command,

		[Parameter(Mandatory=$true)]
		[string]$Message,

		[Parameter(Mandatory=$true)]
		[string]$Title
	)

	Write-Host "::$Command title=$Title::$Message"
}


function Write-GitHubActionsWarning {
	param(
		[Parameter(Mandatory=$true)]
		[string]$Message,

		[Parameter(Mandatory=$false)]
		[string]$Title = "Warning"
	)

	if ($env:GITHUB_ACTIONS -eq "true") {
		Write-GitHubActionsCommand -Command "warning" -Message $Message -Title $Title
	} else {
		Write-Host "! Warning: $Message" -ForegroundColor Yellow
	}
}

function Write-GitHubActionsError {
	param(
		[Parameter(Mandatory=$true)]
		[string]$Message,

		[Parameter(Mandatory=$false)]
		[string]$Title = "Error"
	)

	if ($env:GITHUB_ACTIONS -eq "true") {
		Write-GitHubActionsCommand -Command "error" -Message $Message -Title $Title
	} else {
		Write-Host "x Error: $Message" -ForegroundColor Red
	}
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
	Write-GitHubActionsError -Message "Tag parameter is required"
	exit 1
}

if ([string]::IsNullOrWhiteSpace($Repository)) {
	Write-GitHubActionsError -Message "Repository parameter is required"
	exit 1
}

Write-Host "Validating $Repository release '$Tag'..."

# Prepare headers for GitHub API
$headers = @{
	'Accept' = 'application/vnd.github.v3+json'
	'User-Agent' = 'VFSForGit-Build'
}

if ($env:GITHUB_TOKEN) {
	$headers['Authorization'] = "Bearer $env:GITHUB_TOKEN"
}

# Check if the tag exists in microsoft/git repository
try {
	$releaseResponse = Invoke-RestMethod `
		-Uri "https://api.github.com/repos/$Repository/releases/tags/$Tag" `
		-Headers $headers

	Write-Host "✓ Tag '$Tag' found in $Repository" -ForegroundColor Green
	Write-Host "  Release   : $($releaseResponse.name)"
	Write-Host "  Published : $($releaseResponse.published_at.ToString('u'))"
	
	# Check if this a pre-release
	if ($releaseResponse.prerelease -eq $true) {
		Write-GitHubActionsWarning `
			-Message "Using a pre-released version of $Repository" `
			-Title "Pre-release $Repository version"
	}

	# Get the latest release for comparison
	try {
		$latestResponse = Invoke-RestMethod `
			-Uri "https://api.github.com/repos/$Repository/releases/latest" `
			-Headers $headers
		$latestTag = $latestResponse.tag_name

		# Check if this is the latest release
		if ($Tag -eq $latestTag) {
			Write-Host "✓ Using the latest release" -ForegroundColor Green
			exit 0
		}

		# Not the latest!
		$warningTitle = "Outdated $Repository release"
		$warningMsg = "Not using latest release of $Repository (latest: $latestTag)"
		Write-GitHubActionsWarning -Message $warningMsg -Title $warningTitle
	} catch {
		Write-GitHubActionsWarning -Message "Could not check latest release info for ${Repository}: $($_.Exception.Message)"
	}
} catch {
	if ($_.Exception.Response.StatusCode -eq 404) {
		Write-GitHubActionsError -Message "Tag '$Tag' does not exist in $Repository"
		exit 1
	} else {
		Write-GitHubActionsError -Message "Error validating release '$Tag': $($_.Exception.Message)"
		exit 1
	}
}
