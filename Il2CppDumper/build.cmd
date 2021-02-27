@echo off
call npx rimraf bin
call npx rimraf obj
dotnet build /property:GenerateFullPaths=true /p:configuration=Release
