function Add-ReleaseLicenses([string]$destination, [string]$project, [string]$tagLibVersion) {
    $licenseSources = Join-Path $PSScriptRoot 'licenses'
    $repo = Split-Path $PSScriptRoot -Parent
    $player = Split-Path $project -Parent
    $appLicense = Join-Path $repo 'LICENSE'
    $deps = Get-Content -LiteralPath (Join-Path $destination 'KebuzPlayer.deps.json') -Raw | ConvertFrom-Json
    $assets = Get-Content -LiteralPath (Join-Path $player 'obj\project.assets.json') -Raw | ConvertFrom-Json
    $packageFolders = $assets.packageFolders.PSObject.Properties.Name
    $licenseDir = New-Item -ItemType Directory -Path (Join-Path $destination 'licenses')

    if (-not $deps.libraries.PSObject.Properties["TagLibSharp/$tagLibVersion"]) {
        throw "Published TagLibSharp version does not match the corresponding source archive ($tagLibVersion)"
    }

    Copy-Item -LiteralPath $appLicense -Destination $destination
    foreach ($name in 'Archivo-OFL.txt', 'IBM-Plex-Mono-OFL.txt') {
        Copy-Item -LiteralPath (Join-Path $player "Assets\Fonts\$name") -Destination $licenseDir.FullName
    }
    Copy-Item -LiteralPath (Join-Path $licenseSources 'Apache-2.0.txt') -Destination $licenseDir.FullName

    $notices = foreach ($package in $deps.libraries.PSObject.Properties | Sort-Object Name) {
        if ($package.Value.type -notin 'package', 'runtimepack') { continue }
        $id, $version = ($package.Name -replace '^runtimepack\.', '') -split '/', 2
        $relative = "$($id.ToLowerInvariant())\$($version.ToLowerInvariant())"
        $packageDir = $packageFolders | ForEach-Object { Join-Path $_ $relative } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
        if (-not $packageDir) { throw "NuGet package for $($package.Name) was not found" }

        $spec = Join-Path $packageDir "$($id.ToLowerInvariant()).nuspec"
        $metadata = ([xml](Get-Content -LiteralPath $spec -Raw -Encoding UTF8)).package.metadata
        $license = $metadata.license
        $packageLicenses = New-Item -ItemType Directory -Path (Join-Path $licenseDir.FullName $id)
        $legalFiles = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object {
            $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD[-_]?PARTY[-_]?NOTICES)(\.(md|txt))?$'
        })
        foreach ($file in $legalFiles) {
            Copy-Item -LiteralPath $file.FullName -Destination $packageLicenses.FullName
        }

        if ($package.Value.type -eq 'runtimepack' -or $id -in 'SkiaSharp.NativeAssets.Win32', 'HarfBuzzSharp.NativeAssets.Win32') {
            if (-not ($legalFiles.Name -match '^THIRD[-_]?PARTY[-_]?NOTICES')) {
                throw "Third-party notices are missing from $id $version"
            }
        }

        if ($license.type -eq 'file') {
            $licenseName = $license.InnerText
            Copy-Item -LiteralPath (Join-Path $packageDir $licenseName) -Destination $packageLicenses.FullName
        }
        elseif ($license.type -eq 'expression') {
            $licenseName = switch ($license.InnerText) {
                'MIT' {
                    $original = switch -Wildcard ($id) {
                        'Avalonia*' { Join-Path $licenseSources 'Avalonia.txt'; break }
                        'Microsoft.Data.Sqlite*' { Join-Path $licenseSources 'Microsoft.Data.Sqlite.txt'; break }
                        { $_ -in 'MicroCom.Runtime', 'NAudio.Vorbis', 'Tmds.DBus.Protocol' } {
                            Join-Path $licenseSources "$id.txt"; break
                        }
                        'NAudio.*' {
                            # NAudio ships one upstream license for its modules.
                            $packageRoot = Split-Path (Split-Path $packageDir -Parent) -Parent
                            Join-Path $packageRoot "naudio\$version\license.txt"
                            break
                        }
                    }
                    if ($original) {
                        Copy-Item -LiteralPath $original -Destination (Join-Path $packageLicenses.FullName 'MIT.txt')
                        'MIT.txt'
                    }
                    elseif ($legalFiles.Name -match '^LICEN[CS]E') {
                        $legalFiles | Where-Object Name -Match '^LICEN[CS]E' | Select-Object -First 1 -ExpandProperty Name
                    }
                    else { throw "MIT license text is missing for $id $version" }
                }
                'Apache-2.0' {
                    Copy-Item -LiteralPath (Join-Path $licenseSources 'Apache-2.0.txt') -Destination $packageLicenses.FullName
                    'Apache-2.0.txt'
                }
                'LGPL-2.1-only' {
                    Copy-Item -LiteralPath (Join-Path $licenseSources 'LGPL-2.1.txt') -Destination $packageLicenses.FullName
                    'LGPL-2.1.txt'
                }
                default { throw "No release license text is configured for $id $version ($($license.InnerText))" }
            }
        }
        else { throw "No NuGet license declared for $id $version" }

        "$id $version - licenses/$id/$licenseName"
        if ($metadata.copyright) { "  $($metadata.copyright)" }
        foreach ($file in $legalFiles | Where-Object Name -ne $licenseName) {
            "  licenses/$id/$($file.Name)"
        }
        ''
    }

    $noticeTemplate = Get-Content -LiteralPath (Join-Path $licenseSources 'NOTICE.txt') -Raw -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $destination 'THIRD-PARTY-NOTICES.txt') -Encoding UTF8 -Value ($noticeTemplate + ($notices -join "`n"))
}
