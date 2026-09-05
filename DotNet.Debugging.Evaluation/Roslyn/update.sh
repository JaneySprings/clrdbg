#!/usr/bin/env bash
# Replaces the Roslyn expression compiler sources vendored under Roslyn/src with the ones of a Microsoft.CodeAnalysis.CSharp
# package version, taken from the exact dotnet/roslyn commit the package was built from (its nuspec records it), so the
# sources match the internals of the referenced assemblies. Defaults to the CodeAnalysisVersion of Directory.Packages.props.
#
#   Roslyn/update.sh [version]
#
# The copy is verbatim and nothing is edited afterwards: the files that need the Visual Studio debugger (Dkm) are excluded
# from the build by the project file and the few types they provided are stood in for by Shims/. Review the diff, build,
# run the evaluation tests and commit the result together with the CodeAnalysisVersion bump.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
version="${1:-$(sed -n 's/.*<CodeAnalysisVersion>\(.*\)<\/CodeAnalysisVersion>.*/\1/p' "$root/Directory.Packages.props")}"
if [ -z "$version" ]; then
  echo "No version given and no CodeAnalysisVersion found in Directory.Packages.props" >&2
  exit 1
fi
lower="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"

echo "Resolving the source commit of Microsoft.CodeAnalysis.CSharp $version"
nuspec="$(curl -fsSL "https://api.nuget.org/v3-flatcontainer/microsoft.codeanalysis.csharp/$lower/microsoft.codeanalysis.csharp.nuspec")"
commit="$(printf '%s' "$nuspec" | sed -n 's/.*<repository [^>]*commit="\([0-9a-f]*\)".*/\1/p' | head -n 1)"
if [ -z "$commit" ]; then
  echo "The nuspec of Microsoft.CodeAnalysis.CSharp $version names no source commit" >&2
  exit 1
fi
echo "  dotnet/roslyn@$commit"

# What is vendored: the language neutral and the C# expression compiler, the one file they link from elsewhere, the license
paths=(
  src/ExpressionEvaluator/Core/Source/ExpressionCompiler
  src/ExpressionEvaluator/CSharp/Source/ExpressionCompiler
  src/Test/PdbUtilities/Shared/DateTimeUtilities.cs
  License.txt
)

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
echo "Fetching the sources"
patterns=()
for path in "${paths[@]}"; do
  patterns+=("/$path")
done
git -C "$work" init -q
git -C "$work" remote add origin https://github.com/dotnet/roslyn
git -C "$work" sparse-checkout set --no-cone "${patterns[@]}"
git -C "$work" fetch -q --depth 1 --filter=blob:none origin "$commit"
git -C "$work" -c advice.detachedHead=false checkout -q FETCH_HEAD

echo "Replacing $here/src"
rm -rf "$here/src" "$here/License.txt"
for path in "${paths[@]}"; do
  mkdir -p "$here/$(dirname "$path")"
  cp -R "$work/$path" "$here/$path"
done

cat > "$here/Roslyn.props" <<PROPS
<!-- Written by update.sh: the Roslyn version vendored under src/ and the dotnet/roslyn commit it was copied from -->
<Project>
  <PropertyGroup>
    <RoslynVendoredVersion>$version</RoslynVendoredVersion>
    <RoslynVendoredCommit>$commit</RoslynVendoredCommit>
  </PropertyGroup>
</Project>
PROPS
echo "Done: Roslyn $version ($commit). Review 'git status', build and run the evaluation tests"
