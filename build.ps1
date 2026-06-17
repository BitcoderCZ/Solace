#!/usr/bin/env pwsh
$configuration = "Debug"
$architecture = "x64"
$os = "linux"

Copy-Item -Path "staticdata" -Destination "publish/staticdata" -Recurse -Force

dotnet publish src/Solace.Launcher -c $configuration --arch $architecture --os $os -o publish/launcher
#dotnet publish ./src/Solace.Launcher -c $configuration -o ./publish/launcher -p:PublishAot=true,StripSymbols=true,PublishTrimmed=true,TrimmerRemoveSymbols=true,DebuggerSupport=false,EnableUnsafeBinaryFormatterSerialization=false,EnableUnsafeUTF7Encoding=false,EventSourceSupport=false,Http3Support=false,InvariantGlobalization=true

$componentProperties = "UseSharedLibs=true"#,DebugSymbols=false,DebugType=none"

dotnet publish src/Solace.EventBus.Server -c $configuration --arch $architecture --os $os -o publish/components/event-bus -p:$componentProperties
dotnet publish src/Solace.ObjectStore.Server -c $configuration --arch $architecture --os $os -o publish/components/object-store -p:$componentProperties
dotnet publish src/Solace.Buildplate -c $configuration --arch $architecture --os $os -o publish/components/buildplate-launcher -p:$componentProperties
dotnet publish src/Solace.ApiServer -c $configuration --arch $architecture --os $os -o publish/components/api-server -p:$componentProperties
dotnet publish src/Solace.Locator -c $configuration --arch $architecture --os $os -o publish/components/locator -p:$componentProperties
dotnet publish src/Solace.TappablesGenerator -c $configuration --arch $architecture --os $os -o publish/components/tappable-generator -p:$componentProperties
dotnet publish src/Solace.TileRenderer -c $configuration --arch $architecture --os $os -o publish/components/tile-renderer -p:$componentProperties