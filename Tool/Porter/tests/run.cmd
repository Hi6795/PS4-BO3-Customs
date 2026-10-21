@echo off
setlocal
cd /d "%~dp0"
dotnet publish UpdateProbe\UpdateProbe.csproj -c Release -p:ProbeLabel=one -p:NuGetAudit=false -o work\probe_one -v:q -nologo || exit /b 1
dotnet publish UpdateProbe\UpdateProbe.csproj -c Release -p:ProbeLabel=two -p:NuGetAudit=false -o work\probe_two -v:q -nologo || exit /b 1
dotnet build FFPorter.Checks\FFPorter.Checks.csproj -c Debug -p:NuGetAudit=false -v:q -nologo || exit /b 1
FFPorter.Checks\bin\Debug\net10.0-windows\FFPorter.Checks.exe %*
