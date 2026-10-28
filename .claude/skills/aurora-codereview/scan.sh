#!/usr/bin/env bash
# Greps the added C# lines for the aurora-code rules a pattern can catch. Prints file:line, rule, hit.
# Usage: scan.sh                working tree + untracked .cs against HEAD
#        scan.sh <commit>       what that commit added
#        scan.sh <a>..<b>       what the range added
#        scan.sh <file.cs>...   every line of those files
root="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$root" || exit 1

emit_diff()
{
    git diff -U0 --no-color "$@" -- '*.cs'
}

emit_whole()
{
    for f in "$@"; do
        printf '+++ b/%s\n@@ -0,0 +1 @@\n' "$f"
        sed 's/^/+/' "$f"
    done
}

{
    if [ $# -eq 0 ]; then
        emit_diff HEAD
        mapfile -t untracked < <(git ls-files --others --exclude-standard -- '*.cs')
        [ ${#untracked[@]} -gt 0 ] && emit_whole "${untracked[@]}"
    elif [ -f "$1" ]; then
        emit_whole "$@"
    elif [[ "$1" == *..* ]]; then
        emit_diff "$1"
    else
        emit_diff "$1^" "$1"
    fi
} | awk '
function hit(rule, msg)
{
    printf "%s:%d  rule %-3s %s | %s\n", file, line, rule, msg, substr(shown, 1, 100)
    count++
}
/^\+\+\+ / { file = substr($0, 7); next }
/^@@ / { split($3, a, ","); line = substr(a[1], 2) + 0; next }
/^\+/ {
    raw = substr($0, 2)
    sub(/\r$/, "", raw)
    shown = raw
    sub(/^[ \t]+/, "", shown)
    code = raw
    sub(/\/\/.*/, "", code)

    if (raw ~ /\t/) hit(49, "tab")
    if (code ~ /^[ \t]*namespace[ \t]+[A-Za-z0-9_.]+[ \t]*;/) hit(48, "file-scoped namespace")
    if (code ~ /(\)|else|try|finally|do)[ \t]*\{[ \t]*$/) hit(49, "brace not on its own line")
    if (code ~ /(^|[^A-Za-z0-9_.])var[ \t]+[A-Za-z_]/) hit(51, "var")
    if (code ~ /(^|[^A-Za-z0-9_])new[ \t]*\(/) hit(51, "target-typed new")
    if (code ~ /[ \t]is[ \t]+(not[ \t]+)?null/) hit(52, "is null")
    if (code ~ /^[ \t]*((public|private|protected|internal|static|readonly)[ \t]+)+[A-Za-z0-9_<>,.?\[\] ]+[ \t]+_[a-z][A-Za-z0-9]*[ \t]*(=|;)/) hit(43, "underscore field")
    if (code ~ /\[,+\]/) hit(9, "[,] array")
    if (code ~ /\[\][ \t]*\[\]/) hit(8, "jagged array")
    if (code ~ /List<[ \t]*(List<|[A-Za-z0-9_.]+[ \t]*\[\][ \t]*>)/) hit(8, "nested collection")
    if (code ~ /new[ \t]+List<.*>[ \t]*\([ \t]*\)/) hit(7, "List without capacity")
    if (code ~ /(^|[^A-Za-z0-9_])(IEnumerable|IList|IReadOnlyList|ICollection|IReadOnlyCollection)</) hit(11, "interface collection")
    if (code ~ /params[ \t]+object[ \t]*\[\]/) hit(35, "params object[]")
    if (code ~ /%[ \t]*4([^0-9u.]|$)/) hit(37, "% 4 - direction turn?")

    linq = code
    gsub(/(Math|MathF|Vector2|Vector3|Vector4|Array|string|String|Interlocked)\.[A-Za-z]+\(/, "", linq)
    if (linq ~ /\.(Where|Select|SelectMany|OrderBy|OrderByDescending|ThenBy|GroupBy|Distinct|Any|All|First|FirstOrDefault|Last|LastOrDefault|Single|SingleOrDefault|Sum|Min|Max|Average|Aggregate|Skip|SkipWhile|Take|TakeWhile|Zip|ToList|ToArray|ToDictionary|ToHashSet|Cast|OfType|Concat|Reverse)\(/) hit(29, "LINQ")
    if (linq ~ /\.Count\(\)/) hit(29, "LINQ Count()")
    if (code ~ /(^|[^A-Za-z0-9_])(from[ \t]+[A-Za-z_][A-Za-z0-9_]*[ \t]+in[ \t])/) hit(29, "query syntax")

    line++
}
END { printf "%d hit(s)\n", count }
'
