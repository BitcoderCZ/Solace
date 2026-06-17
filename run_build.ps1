#!/usr/bin/env pwsh
Push-Location ./build/launcher

try {
    ./Launcher
}
finally {
    Pop-Location
}