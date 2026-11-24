# Error Codes and Fixes

When something unexpected happens, FoxFix puts a code like `[FX-xxxx]` at the start of the message. This page explains what each code means and what you can do.

The first digit of the code tells which part of the system the problem comes from:

- **1:** The app and basic operations
- **2:** The FoxDPI module
- **3:** Scheduled tasks (Task Scheduler)
- **4:** System changes and configuration
- **5:** Logs and reports
- **6:** Network measurements
- **7:** Sound, microphone and camera checks
- **8:** Game overlay
- **9:** Unexpected errors

| Code | Meaning | What to do |
|---|---|---|
| `FX-1001` | Administrator rights required | Right-click the shortcut or the program file and choose "Run as administrator". |
| `FX-1002` | Administrator permission refused | Answer "Yes" to the User Account Control (UAC) prompt. Nothing was changed on the system; you can try again any time. |
| `FX-1003` | An external link could not be opened | Make sure a default web browser is set. You can also copy the link into your browser's address bar yourself. |
| `FX-1004` | Copying to the clipboard failed | Another app may be using the clipboard. Try again in a moment. |
| `FX-1005` | The undo records could not be read | The record file may be damaged. The app starts without them; earlier changes will not be listed on the "Report and history" page. |
| `FX-2001` | File integrity could not be verified | Security software (antivirus etc.) may have deleted or changed a core file. Add the FoxFix folder to its exclusions, run "DPI reset" and try again. |
| `FX-2002` | The FoxDPI module could not start | Make sure the app runs with administrator rights and is not blocked by antivirus. The error text in the message names the technical cause. |
| `FX-2003` | The FoxDPI process ended | Antivirus or another DPI tool running in the background may conflict with it. Run DPI reset and try again. |
| `FX-2004` | FoxDPI stopped unexpectedly | Start FoxDPI again. If it happens again, restart the computer and run DPI reset. |
| `FX-2005` | A conflicting DPI tool was found | End other GoodbyeDPI, Zapret or WinDivert processes in Task Manager, or remove them with DPI reset. |
| `FX-2006` | FoxDPI is already running | Close the FoxDPI window running in the background or stop the system service, then try again. |
| `FX-2007` | The system service could not be installed or started | What was done has been undone automatically. Check your antivirus, or run FoxDPI in its own window instead. |
| `FX-2008` | The trial stopped with a file error | The network driver may be stuck in memory. Restart the computer or run DPI reset and try again. |
| `FX-2009` | The trial could not start | If the first step cannot start, the cause is usually missing rights or antivirus. See the steps for FX-2002. |
| `FX-2010` | The status could not be read | The Windows service list could not be read. Restart the app with administrator rights. |
| `FX-2011` | FoxDPI could not be stopped | End goodbyedpi.exe or the related process in Task Manager, or use DPI reset. |
| `FX-2012` | First set-up needed | Before FoxDPI can run on its own, FoxFix.exe has to be opened at least once to set things up. |
| `FX-3001` | The scheduled task could not be created | Make sure the app runs with administrator rights and that the Windows Task Scheduler service is running. |
| `FX-3002` | The program folder could not be found | Run FoxFix.exe from a fixed folder (for example the install folder) and tick the option again. |
| `FX-4001` | The preview could not be prepared | Nothing was changed on the system. Try again; if it keeps happening, save the report and send it to the developer. |
| `FX-4002` | An error occurred while applying changes | Read the error details. Only part of the changes may have been made; undo them on the "Report and history" page. |
| `FX-4003` | Undo failed | Try again with administrator rights. If needed, set the old service or network/DNS settings back by hand. |
| `FX-4004` | The DNS setting could not be changed | The adapter name may have changed or the connection dropped. Check your active connection and try again. |
| `FX-4005` | The service could not be managed | The service may be in use (locked) by another process. Restart the computer and try again. |
| `FX-4006` | The start-up entry could not be changed | The registry entry may be protected. Start the app with administrator rights and try again. |
| `FX-4007` | File cleanup failed | The files may be in use by another process. Close the related apps and try again. |
| `FX-4008` | The restart command could not be processed | Restart the computer yourself. To cancel a pending restart, type shutdown /a at the command prompt. |
| `FX-4009` | A Windows setting could not be applied | Make sure you run with administrator rights. The registry key may be protected by a policy (group policy) or security software. If a change stops half way, FoxFix writes the old values back; if it cannot, the message says so and you can try Undo on the "Report and history" page. |
| `FX-4010` | Windows settings could not be saved | Records are written to the ayar-yedekleri folder in FoxFix's data folder. Make sure there is disk space and you can write to the folder; security software may be blocking it. |
| `FX-4011` | Saved Windows settings could not be restored | Make sure you run with administrator rights. If a restore stops half way, FoxFix writes the previous values back; if it cannot, the message says so and you can try Undo on the "Report and history" page. |
| `FX-4012` | A saved Windows setting could not be read | The record file may have been deleted, changed or damaged. Delete it from the ayar-yedekleri folder and save your settings again. |
| `FX-5001` | The report could not be created | Try again. If it keeps happening, close the app completely and start it again. |
| `FX-5002` | The report file could not be saved | Choose another folder, or make sure the file name uses only valid characters. |
| `FX-6001` | The speed test failed | Make sure your internet connection works and Cloudflare servers are not blocked, then try again. |
| `FX-6002` | A check failed while running | This does not affect the other checks. Run the scan again with "Check again". |
| `FX-6003` | The update check failed | Check your internet connection. The check only connects to GitHub (api.github.com); if it is blocked, try again with FoxDPI on. New versions are also listed on the repository's Releases page. |
| `FX-6004` | The update could not be downloaded or verified | Check your internet connection and try again. If the SHA-256 hash of the downloaded file does not match the published one, nothing is run. If it keeps happening, download and install the new version from the Releases page. |
| `FX-7001` | A sound or camera device could not be checked | Windows did not answer the question about the device. Nothing was changed. Reconnect the device, close apps that may be using it, and try the check again. |
| `FX-8001` | The overlay could not count frames | Windows' display events could not be listened to; FPS and frame time show N/A while the processor and graphics card numbers keep working. Make sure FoxFix runs as administrator, check that security software does not block it, and turn the overlay off and on. Nothing was changed. |
| `FX-9001` | Unexpected critical error | The details were written to crash.txt in the log folder. You can send this file to me on Discord to find the cause. |

## Feedback and support

For suggestions, feedback or errors you cannot solve, you can send me (`foxezol`) a direct message on [Discord](https://discord.com/users/381533632852131841). Attaching the system report from the "Report and history" page speeds things up.
