#!/usr/bin/env pwsh
Push-Location ./publish/launcher

try {
    $env:Dir__Sir="bla_bla"
    ./Launcher
}
finally {
    Remove-Item env:Dir__Sir
    Pop-Location
}