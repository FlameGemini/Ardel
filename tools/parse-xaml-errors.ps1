param([string]$OutputJson)
$j = Get-Content $OutputJson -Raw | ConvertFrom-Json
$errs = $j.MSBuildLogEntries | Where-Object { $_.ErrorCode }
if ($errs) {
    $errs | ForEach-Object { "{0} {1}:{2}:{3} - {4}" -f $_.ErrorCode, $_.File, $_.LineNumber, $_.ColumnNumber, $_.Message }
} else {
    Write-Output "No structured errors in output.json"
}
