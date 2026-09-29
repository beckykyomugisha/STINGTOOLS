# STING Tools - tester guide

STING Tools is a Revit add-in for ISO 19650 asset tagging, drawing production and MEP
engineering. This kit installs the current test build for **90 days** on your PC.

Please do not share the kit or your licence file. A licence only works on the PC it was
issued for.

## What you need

- Windows 10 or 11.
- Autodesk Revit 2025 or 2026. The build targets Revit 2025's API, which Revit 2026 also
  loads. Revit 2027 is registered too, but it has not been tried.
- No administrator rights. Everything installs under your own user profile.

## Install

1. **Extract the whole ZIP** to a folder, for example `Documents\STING-Tester`. Do not
   run anything from inside the ZIP.
2. **Get your machine code.** Double-click `Get-MachineCode.cmd`. The code
   (`XXXX-XXXX-XXXX-XXXX-XXXX`) is shown, copied to the clipboard and saved as
   `MachineCode.txt` on your Desktop. Send it to Planscape.
3. **Close Revit**, then double-click `Install-STING.cmd`. It copies the plugin to
   `%LOCALAPPDATA%\Planscape\STING-Tester\Plugin` and registers it with every Revit 2025,
   2026 or 2027 it finds.
4. **When your licence arrives** (a file called `StingTools.lic`), put it in the kit folder
   and double-click `Install-Licence.cmd`. You can also start Revit, choose
   **STING > Activate** and paste its contents.
5. **Start Revit.** If the STING panel is not docked, open it from
   **View > User Interface**.

If Windows SmartScreen warns about a `.cmd` file, choose **More info > Run anyway**.
The scripts are plain text, and you can open them in Notepad to read what they do.

Without a licence, every STING command opens the Activate dialog. That is expected.

## What to test

The `SmokeTests` folder has the checklists. Work through them in this order:

1. `TAG_TEST_PROTOCOL.md`: tagging, bindings and rooms/spaces (T1-T15), the tag library
   (section L: loading, updating, Repair Lib, Promote Library), then section U.
2. `SYMBOL_SLD_Revit_Smoke_Test_Checklist.docx` (same content as `SYMBOL_SLD_REVIT_SMOKE_TEST.md`):
   the symbol library, single line diagrams and the symbols workflow.

Use a **copy** of a real project, never a live one. Re-tagging changes some system codes
and sequence numbers.

Mark each step as pass, fail or **BLOCKED** (could not run, with the reason). Please do
not leave a step blank: a blank reads as "not tried".

## Reporting a problem

Send:

- what you clicked, and what you expected to happen;
- a screenshot of any message;
- the log file `StingTools_yyyyMMdd.log` (with today's date) from
  `%LOCALAPPDATA%\Planscape\STING-Tester\Plugin`;
- the Revit version and, if you can, the model (or a cut-down copy).

A command that does nothing and says nothing counts as a bug too. Please report those as well.

## Updating, uninstalling and expiry

- **Update:** extract the new kit and run `Install-STING.cmd` again. Your licence and
  settings are kept.
- **Uninstall:** close Revit and run `Uninstall-STING.cmd`. If STING was already
  registered before the kit, your previous registration is restored.
- **Expiry:** the licence stops working 90 days after it was issued. **STING > Activate**
  shows the date. Ask Planscape for a new one if testing runs longer.

## Known limits of this build

- **Not yet tested in Revit.** It has passed the automated build, tests and checks only,
  which is why your testing matters.
- **25 of the newly added symbols have no real size yet.** The symbol builder warns about
  each one; that warning is expected.
- **Export drafts are not native files.** ETAP, EasyPower and DIALux exports are data
  hand-off drafts, and the target programs will not import them as they are.
- **Arc flash results are estimates.** Do not use them to specify PPE.
