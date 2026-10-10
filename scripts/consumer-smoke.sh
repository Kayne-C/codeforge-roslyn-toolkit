#!/usr/bin/env bash
set -euo pipefail

PACKAGE_DIR="$(cd "$1" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"

cat > Consumer.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreSources>$PACKAGE_DIR;https://api.nuget.org/v3/index.json</RestoreSources>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CodeForge" Version="0.1.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
EOF

cat > Program.cs <<'EOF'
using CodeForge;

var id = OrderId.New();
Console.WriteLine(id.ToString().Length + " " + Color.Red.ToStringFast());
Task.Delay(1).Wait();

[StronglyTypedId] readonly partial struct OrderId;
[EnumExtensions] enum Color { Red, Blue }
EOF

output="$(dotnet build 2>&1)"
echo "$output" | tail -5
echo "$output" | grep -q "warning CF0002" || { echo "expected CF0002 from the packaged analyzer"; exit 1; }
dotnet run --no-build | grep -q "36 Red" || { echo "generated code did not run as expected"; exit 1; }
echo "consumer smoke test passed"
