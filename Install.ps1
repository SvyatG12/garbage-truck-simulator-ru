[CmdletBinding()]
param(
    [string]$GamePath,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$gameName = 'Garbage Truck Simulator'
$originalHash = '23ACF0ADFE11C3FA1C3BE5727791CD9DC57418FF0F7065AEA1FBE5DBB2F8217B'

function Find-Game {
    $steamFolders = @(
        (Get-ItemPropertyValue -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue)
        (Get-ItemPropertyValue -Path 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -Name InstallPath -ErrorAction SilentlyContinue)
        (Join-Path ${env:ProgramFiles(x86)} 'Steam')
    ) | Where-Object { $_ } | Select-Object -Unique

    foreach ($steam in $steamFolders) {
        $libraries = @($steam)
        $foldersFile = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $foldersFile -PathType Leaf) {
            $content = Get-Content -LiteralPath $foldersFile -Raw -Encoding UTF8
            foreach ($entry in [regex]::Matches($content, '"path"\s+"((?:\\.|[^"\\])*)"')) {
                $libraries += $entry.Groups[1].Value.Replace('\\', '\')
            }
        }

        foreach ($library in $libraries | Select-Object -Unique) {
            $candidate = Join-Path $library "steamapps\common\$gameName"
            if (Test-Path -LiteralPath (Join-Path $candidate "$gameName.exe") -PathType Leaf) {
                return $candidate
            }
        }
    }

    $answer = Read-Host 'Вставьте путь к папке Garbage Truck Simulator'
    return $answer.Trim().Trim('"')
}

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Copy-Atomically([string]$Source, [string]$Destination) {
    $pending = $Destination + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Copy-Item -LiteralPath $Source -Destination $pending
        if ([IO.File]::Exists($Destination)) {
            [IO.File]::Replace($pending, $Destination, [NullString]::Value)
        } else {
            [IO.File]::Move($pending, $Destination)
        }
    } finally {
        if ([IO.File]::Exists($pending)) { [IO.File]::Delete($pending) }
    }
}

function Build-Patch([string]$Stage, [string]$Managed, [string]$Original) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
        throw 'Для установки требуется .NET Framework 4.x для Windows.'
    }

    $references = @('mscorlib', 'netstandard', 'System', 'System.Core', 'UnityEngine.CoreModule',
        'UnityEngine.UIModule', 'UnityEngine.UI', 'UnityEngine.TextRenderingModule',
        'UnityEngine.TextCoreFontEngineModule', 'UnityEngine.TextCoreTextEngineModule',
        'Unity.TextMeshPro', 'UnityEngine.IMGUIModule') | ForEach-Object {
        '/reference:' + (Join-Path $Managed "$_.dll")
    }
    $runtime = Join-Path $Stage 'GarbageTruckRu.dll'
    $patched = Join-Path $Stage 'Assembly-CSharp.dll'
    $cecil = Join-Path $Stage 'Unity.Burst.Cecil.dll'
    $patcher = Join-Path $Stage 'Patch.exe'
    Copy-Item -LiteralPath (Join-Path $Managed 'Unity.Burst.Cecil.dll') -Destination $cecil
    & $compiler /nologo /noconfig /nostdlib /target:library /optimize+ /codepage:65001 "/out:$runtime" @references (Join-Path $PSScriptRoot 'src\RussianUi.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Сборка модуля локализации завершилась с ошибкой.' }
    & $compiler /nologo /target:exe /optimize+ "/out:$patcher" "/reference:$cecil" (Join-Path $PSScriptRoot 'src\Patch.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Сборка установщика завершилась с ошибкой.' }
    & $patcher $Original $runtime $patched
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось подготовить файлы русификатора.' }
}

$gate = New-Object System.Threading.Mutex($false, 'Local\GarbageTruckRuInstall')
$locked = $false
$stage = $null
try {
    try { $locked = $gate.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Уже запущен другой экземпляр установщика.' }

    if (-not $GamePath) { $GamePath = Find-Game }
    $root = (Resolve-Path -LiteralPath $GamePath).ProviderPath
    $managed = Join-Path $root "$gameName`_Data\Managed"
    $mod = Join-Path $root 'GarbageTruckRu'
    $active = Join-Path $managed 'Assembly-CSharp.dll'
    $original = Join-Path $mod 'Assembly-CSharp.original.dll'
    $hashes = Join-Path $mod 'installed-sha256.txt'
    $runtime = Join-Path $managed 'GarbageTruckRu.dll'
    $catalog = Join-Path $mod 'ru.tsv'

    if (-not (Test-Path -LiteralPath (Join-Path $root "$gameName.exe") -PathType Leaf) -or
        -not (Test-Path -LiteralPath $active -PathType Leaf)) {
        throw 'В выбранной папке нет установленной игры Garbage Truck Simulator.'
    }
    if (Get-Process -Name $gameName -ErrorAction SilentlyContinue) {
        throw 'Закройте игру и повторите операцию.'
    }

    $activeHash = Get-Hash $active
    $known = @()
    if (Test-Path -LiteralPath $hashes -PathType Leaf) {
        $known = @(Get-Content -LiteralPath $hashes -Encoding ASCII)
    }
    if ($activeHash -ne $originalHash -and $known -notcontains $activeHash) {
        throw 'Эта версия игры отличается от поддерживаемой сборки Steam 11076101 или основной файл изменён другим модом.'
    }

    if ($Uninstall) {
        if ($activeHash -ne $originalHash) {
            if (-not (Test-Path -LiteralPath $original -PathType Leaf) -or (Get-Hash $original) -ne $originalHash) {
                throw 'Резервная копия отсутствует или повреждена. В Steam откройте свойства игры и проверьте целостность файлов.'
            }
            Copy-Atomically $original $active
        }
        if (Test-Path -LiteralPath $hashes -PathType Leaf) {
            foreach ($file in @($runtime, $catalog, $original, $hashes)) {
                if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
            }
            if (@(Get-ChildItem -LiteralPath $mod -Force).Count -eq 0) { [IO.Directory]::Delete($mod) }
        }
        Write-Host 'Русификатор снят. Игра готова к запуску с исходным интерфейсом.'
    } else {
        if ((Test-Path -LiteralPath $original -PathType Leaf) -and (Get-Hash $original) -ne $originalHash) {
            throw 'Найдена повреждённая резервная копия. Установщик не будет её перезаписывать.'
        }

        $buildSource = if ($activeHash -eq $originalHash) { $active } else { $original }
        $stage = Join-Path ([IO.Path]::GetTempPath()) ('GarbageTruckRu-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage | Out-Null
        Build-Patch $stage $managed $buildSource

        New-Item -ItemType Directory -Path $mod -Force | Out-Null
        if (-not (Test-Path -LiteralPath $original -PathType Leaf)) { Copy-Atomically $active $original }
        $patched = Join-Path $stage 'Assembly-CSharp.dll'
        $newHash = Get-Hash $patched
        $newHashes = @($known + $newHash | Select-Object -Unique)
        $stagedHashes = Join-Path $stage 'installed-sha256.txt'
        Set-Content -LiteralPath $stagedHashes -Value $newHashes -Encoding ASCII

        Copy-Atomically $stagedHashes $hashes
        Copy-Atomically (Join-Path $PSScriptRoot 'localization\ru.tsv') $catalog
        Copy-Atomically (Join-Path $stage 'GarbageTruckRu.dll') $runtime
        Copy-Atomically $patched $active
        Write-Host 'Русификатор установлен. Запускайте игру как обычно через Steam.'
    }
} catch {
    Write-Host ("Ошибка: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
} finally {
    if ($stage -and [IO.Directory]::Exists($stage)) {
        # The staging folder is created above and contains only these build outputs.
        foreach ($file in @('GarbageTruckRu.dll', 'Assembly-CSharp.dll', 'Unity.Burst.Cecil.dll', 'Patch.exe', 'installed-sha256.txt')) {
            [IO.File]::Delete((Join-Path $stage $file))
        }
        [IO.Directory]::Delete($stage)
    }
    if ($locked) { $gate.ReleaseMutex() }
    $gate.Dispose()
}

