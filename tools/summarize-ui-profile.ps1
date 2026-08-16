param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$rows = Import-Csv -LiteralPath $resolvedPath -Delimiter '|'
$spans = @($rows | Where-Object { $_.kind -eq 'span' })

if ($spans.Count -eq 0)
{
    Write-Error "No span rows were found in $resolvedPath"
    exit 1
}

$summary = foreach ($group in ($spans | Group-Object feature, operation))
{
    $durations = @($group.Group | ForEach-Object { [long]$_.duration_us } | Sort-Object)
    $lastIndex = $durations.Count - 1
    $p50Index = [Math]::Floor($lastIndex * 0.50)
    $p95Index = [Math]::Ceiling($lastIndex * 0.95)
    [pscustomobject]@{
        Feature = $group.Group[0].feature
        Operation = $group.Group[0].operation
        Count = $durations.Count
        P50_us = $durations[$p50Index]
        P95_us = $durations[$p95Index]
        Max_us = $durations[$lastIndex]
        GC0 = ($group.Group | Measure-Object -Property gc0 -Sum).Sum
        GC1 = ($group.Group | Measure-Object -Property gc1 -Sum).Sum
        GC2 = ($group.Group | Measure-Object -Property gc2 -Sum).Sum
    }
}

$summary |
    Sort-Object -Property @{ Expression = 'P95_us'; Descending = $true }, @{ Expression = 'Max_us'; Descending = $true } |
    Format-Table -AutoSize

Write-Output ""
Write-Output "State transitions:"
$rows |
    Where-Object { $_.kind -eq 'state' } |
    Group-Object feature, operation |
    Sort-Object Count -Descending |
    Select-Object Count, Name |
    Format-Table -AutoSize
