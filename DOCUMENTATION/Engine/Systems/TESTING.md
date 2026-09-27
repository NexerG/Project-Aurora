---
date: 2026-09-27
tags:
  - d_System
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[TESTING]]"
Dependencies:
  - "[[Bootstrapper]]"
  - "[[LOGGING]]"
  - "[[THREADING]]"
  - "[[SETTINGS]]"
Implementors:
  - "[[TESTING]]"
Namespace: ArctisAurora.Core.Testing
SourceFiles: AuroraEngine/Core/Testing/*.cs, AuroraEngine/Tests/*.cs
VerifiedAgainst: 2026-09-27
---
## Overview

Tests run inside the real application. Launching a host with `--test` boots it exactly as normal, then a runner takes over the primary window, runs the test suites one test at a time over real frames, writes down what passed and what failed, and exits with the number of failures as its exit code.

There is no unit-test framework. A test is a coroutine: it builds a fixture, lets frames pass, and checks what the engine did with it. A test that lets no frames pass is simply a function check.

## Running

```
Thorium.exe --test
Thorium.exe --test=Layout
```

Run it from the host's own `bin` folder, like any launch. `--test` runs every suite and `--test=<Suite>` runs one. Each test prints a `PASS` or `FAIL` line, and the run ends with a summary line and the path of its results file, `%APPDATA%\<Host>\Tests\<yyyyMMdd-HHmmss>\results.xml`.

The first entry is always **Boot**: the host's own UI runs for 60 ticks, and Boot fails if anything logged an error since launch.

## Writing a test

A test is a static method tagged as a `Test` action. It is handed a `TestContext`, and every `yield return n` lets n ticks pass.

```csharp
[A_XSDActionDependency("Layout.StarChildCrossSize", "Test")]
private static IEnumerator<int> StarChildCrossSize(TestContext t)
{
    StackPanelControl bar = new StackPanelControl { orientation = StackPanelControl.Orientation.Horizontal, preferredHeight = 32f };
    bar.AddChild(new LabelControl { text = "Untitled", widthStar = 1f });
    t.Show(bar);
    yield return 2;
    t.Check(bar.arrangedRect.height == 32f, "the bar is arranged 32 px tall");
}
```

`Show` makes a control the primary window's whole tree, destroying whatever was there before. `Check` records a failure together with its file and line, and the test carries on.

A suite lists its tests in order, one file per suite, and the file's name is the suite's name.

```xml
<!-- Data/XML/Documents/Tests/Layout.tests.xml -->
<TestSuite xmlns="http://arctisaurora/AuroraTestTypes">
	<Test Action="Layout.StackPanelArranges"/>
	<Test Action="Layout.StarChildCrossSize" Timeout="5000"/>
</TestSuite>
```

`Timeout` is in milliseconds of test time and defaults to 10000. Engine tests live in the engine and run in any host; a host's own tests live in that host and its `Data` folder.

## What fails a test

- a `Check` that came out false
- an exception, reported with its type, message and the line that threw
- running past its `Timeout`
- any error or fatal logged while it ran
- a suite line naming a test that does not exist, or `--test=<Suite>` naming a suite that does not exist

## What a run changes

- **Time is fixed.** Every tick is exactly 1/60 s of engine time, so key repeat, double-click windows, caret blink, animations and shader effects advance identically on every run, however fast the machine is.
- **Desktop input does not reach it.** No window gets mouse or keyboard callbacks, so moving the mouse or typing while a run is on screen changes nothing.
- **Frames run flat out.** Idle waiting is switched off for the run.
- **Settings are the host's defaults.** A run reads and writes a `TestSettings` folder beside the host's own settings, emptied at the start of every run, so it never touches the user's settings, layout or vault list.

## Runner

```
Arm()
	read --test or --test=<Suite> from the command line, otherwise return
	fix the clock at 1/60 s
	empty TestSettings and make it the settings folder
	collect every method tagged as a Test action
	for each <Suite>.tests.xml across the data folders
		skip it unless it is the suite asked for
		queue each <Test> with its Timeout
	if a suite was asked for and none matched
		record a failure
	post the session to the first main tick

Session tick
	if Boot is still running
		count the tick
		on tick 60 record Boot from the errors logged since launch
	else if no test is running and the queue is empty
		write results.xml
		set the exit code to the number of failures
		request shutdown
	else
		if no test is running
			start the next queued test, recording any whose name resolves to nothing
		fail the test if it has run past its Timeout
		wait out the ticks it asked for
		advance it
		when it finishes, record its failed checks, its exception and the errors logged while it ran
```

## Results file

```xml
<TestRun Host="Thorium" Build="Debug" Started="2026-09-27T15:47:31.1758827+03:00">
  <Test Suite="" Name="Boot" Result="Fail" Ticks="60">
    <Failure Message="55 error(s) logged since launch" File="" Line="0" />
  </Test>
  <Test Suite="Layout" Name="Layout.StackPanelArranges" Result="Pass" Ticks="3" />
  <Test Suite="Layout" Name="Layout.StarChildCrossSize" Result="Pass" Ticks="3" />
</TestRun>
```

## Not built yet

A Tests view in Carbon that reads these results, pointer and keyboard helpers for tests, performance checks against budgets and baselines, and golden-image checks. The plan is in `ClaudeMemory/Context/test-framework-plan.md`.
