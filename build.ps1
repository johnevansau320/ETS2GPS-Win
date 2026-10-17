# Builds the single-file, self-contained Ets2Gps.exe (nothing else to install on the target PC). Needs the .NET 8 SDK.
dotnet publish "$PSScriptRoot\Ets2Gps.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$PSScriptRoot\publish"
Write-Host "Built: $PSScriptRoot\publish\Ets2Gps.exe"
