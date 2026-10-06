#!/usr/bin/env bash
# Builds Debug, runs Thorium's tests and prints only what is not a pass. Usage: test.sh [Suite] [--baseline]
root="$(cd "$(dirname "$0")/.." && pwd)"
suite=""
baseline=0
for a in "$@"; do
	case "$a" in
		--baseline) baseline=1 ;;
		*) suite="$a" ;;
	esac
done
out="${TEMP:-/tmp}/aurora-test"
mkdir -p "$out"
log="$out/run.txt"
base="$root/_Build/test-baseline.txt"

build=$(dotnet build "$root/AuroraEngine/ArctisAurora.sln" -v q -nologo 2>&1)
if [ $? -ne 0 ]; then
	echo "$build" | grep -E ' error ' | sort -u | head -20
	echo "BUILD FAILED"
	exit 255
fi

arg="--test"
[ -n "$suite" ] && arg="--test=$suite"
"$root/Thorium/bin/Debug/net10.0-windows10.0.22621.0/Thorium.exe" "$arg" > "$log" 2>&1
code=$?

grep -aE '\[Test\] (FAIL|NEW|APPROVED) ' "$log" | sed -E 's/^.*\[Test\] //; s/  @[^ ]+$//' | cut -c1-250

kinds=$(grep -aE ' (ERROR|FATAL) ' "$log" | sed -E 's/^[0-9:.]+ +(ERROR|FATAL) +[^ ]+ /\1 /' | sort -u)
if [ $baseline -eq 1 ]; then
	printf '%s\n' "$kinds" > "$base"
	echo "baseline written: $(grep -c . "$base") error kind(s)"
elif [ -f "$base" ]; then
	comm -13 <(tr -d '\r' < "$base" | sort -u) <(printf '%s\n' "$kinds") | grep . | sed 's/^/new error: /'
fi

summary=$(grep -aE '\[Test\] [0-9]+ passed' "$log" | sed -E 's/^.*\[Test\] //')
[ -z "$summary" ] && summary="no results line - the run died, log: $log"
echo "$summary"
exit $code
