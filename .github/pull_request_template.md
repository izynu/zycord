## Summary

What this pull request changes, in one or two sentences.

## Why

The problem it solves, or the reason for the change. Link the issue it closes, if there is one.

Closes #

## What changed

- 

## How it was tested

- [ ] `python build.py` completes and the self test ends with `RESULT: PASS`
- [ ] `python site/build_site.py` was run if anything under `site/` changed
- [ ] `python brand/make_icon.py` was run if anything under `brand/` changed

Describe anything you tested by hand, and what you saw.

## Screenshots

For an interface change, include a before and after, or a screenshot of the new state.

## Checklist

- [ ] The change is focused on one thing, and the diff contains no unrelated reformatting
- [ ] No new dependency was added without a prior discussion
- [ ] No network call to Discord, no telemetry, no auto-updater, no remote catalog fetch
- [ ] Session stop still confirms the process points at the file Zycord created, and paths are still validated
- [ ] Any new backend behaviour has a check in `app/main/SelfTest.cs`
- [ ] The README, `docs/ARCHITECTURE.md` or the site page was updated if behaviour changed
- [ ] No emoji, and no em dashes in prose or tables
- [ ] No previous project name, previous author or AI attribution in the code, comments or copy
- [ ] The C# compiles with `-warn:4` and no warnings
- [ ] The renderer change is ES5 and respects the content security policy in `index.html`
