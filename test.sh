#!/bin/sh

dotnet test -v n /p:Exclude="[xunit*]*" /p:ExcludeByFile=\"**/Program.cs,**/Extractor/Logger.cs\" /p:CollectCoverage=true /p:CoverletOutputFormat=lcov /p:CoverletOutput='../coverage.lcov'
