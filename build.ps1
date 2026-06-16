#!/usr/bin/env pwsh
$configuration = "Debug"

Copy-Item -Path "staticdata" -Destination "publish/staticdata" -Recurse -Force

dotnet publish src/Solace.Launcher -c $configuration -o publish/launcher
#dotnet publish ./src/Solace.Launcher -c $configuration -o ./publish/launcher -p:PublishAot=true,StripSymbols=true,PublishTrimmed=true,TrimmerRemoveSymbols=true,DebuggerSupport=false,EnableUnsafeBinaryFormatterSerialization=false,EnableUnsafeUTF7Encoding=false,EventSourceSupport=false,Http3Support=false,InvariantGlobalization=true
dotnet publish src/Solace.EventBus.Server -c $configuration -o publish/components/event-bus -p:UseSharedLibs=true
dotnet publish src/Solace.ObjectStore.Server -c $configuration -o publish/components/object-store -p:UseSharedLibs=true
dotnet publish src/Solace.ApiServer -c $configuration -o publish/components/api-server -p:UseSharedLibs=true