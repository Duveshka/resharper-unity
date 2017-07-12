param (
    [Parameter(Mandatory = $true)]
    [string]$version, # will be written in header
    [Parameter(Mandatory = $true)]
    [string]$inputDir # directory with .cs files to merge
)

$content = {}.Invoke()
$usings = {}.Invoke()
$outputName = "Unity3DRider.cs"
$output = Join-Path $inputDir $outputName

Write-Host ""
Write-Host "Generating $outputName using..."
Get-ChildItem $inputDir -Filter *.cs | # LiteralPath fixes strange behavior of Filter + Exclude
ForEach-Object { # files
    $file = $_
    if ($file.Name -ne $outputName){
        Write-Host $file.FullName
        Get-Content $file.FullName | # lines
        ForEach-Object{
            $line = $_
            if ($line -match "using .*;" -and $line -notmatch "using *\(") { # careful with using (x) constructs
                $usings.Add($line)
            }
            else{
                $content.Add($line)
            }
        }
    }
}
Write-Host ""

New-Item $output -Force > $null

Add-Content $output @"
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Version: $version
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
"@

foreach($line in $usings | Sort-Object | Get-Unique){
    Add-Content $output $line
}

foreach($line in $content){
    Add-Content $output $line
}
