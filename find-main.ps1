  
$files = Get-ChildItem ./ -Recurse -File -Exclude *.map, *.txt, *.md, *.min.js, bootstrap-grid.css, *.min.css, bootstrap.js, bootstrap.bundle.js, bootstrap-reboot.css
$output = @()
foreach($file in $files) {
    $contentRaw = Get-Content -LiteralPath $file -Raw
    if ($contentRaw -match "master") {
        $content = Get-Content $file
        $lineNum = 0
        foreach ($line in $content) {
            $lineNum += 1
            if ($line -match "master") {
                if (($file.FullName.EndsWith("yml") -or 
                     $file.FullName.EndsWith("sh") -or
                     $file.FullName.EndsWith("CODEOWNERS") -or
                     $file.FullName.EndsWith("ps1")) -and $line.Trim().StartsWith("#"))
                {
                    continue
                }

                if ($file.FullName.EndsWith("cs") -and $line.Trim().StartsWith("//"))
                {
                    continue
                }
                
                if ($file.FullName.Contains("\Generated\") -or $file.FullName.Contains("\SessionRecords\"))
                {
                    continue
                }

                $logs = "$($file.FullName) : $lineNum"
                Write-Host $logs
                $output += $logs
            }
        }
    }
}
$output | Out-File -append ./java_mater.txt