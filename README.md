## Mini Keyboard Studio

Open **Mini Keyboard Studio.exe** for the refreshed Windows interface:

- Dark theme with clear selection states and a live USB connection badge.
- A keypad and dial layout beside a dedicated assignment preview.
- Keyboard, modifier, media, lighting, and mouse action categories.
- A separate **Send to keyboard** action to write your configuration.
- Scrollable content when the window is smaller than the configuration surface.
- DPI-aware rendering and a centered logo that stays inside its bounds.
- Live output inspection from the physical macropad, with no saved history.

Select a key or dial action, build its assignment using the action library, then
send the configuration to your keyboard. The assignment fields are previews;
use the library to edit them. **Clear assignment** clears the selected assignment.

### Inspect the real output

Select a control and click **Inspect this control's live output**, then press that
key or turn/press that dial on the physical macropad. The live inspector displays
the keyboard, media, or mouse events actually emitted by USB device `1189:8890`.
Other keyboards and mice are excluded. Inspection stops after ten seconds, when
you click Stop, or when Studio loses focus. It never writes the device or saves
input history. Shortcuts emitted by the macropad still run in Windows.

This is live output inspection, not a flash configuration dump: the known protocol
for this model has no assignment-read command. Studio therefore cannot populate
all existing assignments automatically. See the [protocol compatibility notes](https://github.com/hobbyworker/MINIKeyboardLinux#readme).

Keep the launcher beside `MINI KeyBoard.exe`, its configuration, resource folders,
and DLLs. The Studio reuses the original Windows Forms controls and their HID
handlers. The original executable and device protocol are unchanged.

The bundled executable wires up 12 keys and two dials. Its extra key and third-dial
controls have no click handlers, so Studio hides those inactive controls. Layer
and lighting support still depend on the connected hardware and vendor controller.

### Building the UI

The launcher source is in `src/Studio.cs`. Run this from PowerShell:

```powershell
.\build.ps1
```

The script uses the .NET Framework 4 C# compiler included with Windows; no .NET SDK
or package restore is needed. Close Studio before rebuilding. The launcher is
compiled for x86 to match the vendor executable.

Run `.\test.ps1` to verify device filtering, modifiers, and emitted key decoding.

Validation: the launcher was compiled and visually checked on Windows, including
key selection, category navigation, live connection status, native DPI rendering,
and the full action library. The live inspector received `End → Insert` from the
physical macropad. Its device filtering and keyboard decoding also pass 13 test
assertions. Writing a configuration to hardware was not part of UI verification.

## Original software notes

software for https://www.amazon.de/gp/product/B0B1QTDY66/ref=ppx_yo_dt_b_search_asin_title?ie=UTF8&psc=1
12 key + 2 knobs macro keyboard

looks like this but should also work up to 4x4 keys

<img src="616FCMGdXoL._AC_SL1500_.jpg"
     alt="Markdown Monster icon"
     style="height:200px" />
<br>

## source is just downlaoded from some google drive zip or something - but it worked so i at least put it here for documentation and SEO (cause it was annoying to find as well , you can probably take the amount of stars as a credibility score 😅)

according to this scan:
https://www.virustotal.com/gui/file/4ae53d3a40921e4f57444c489fa1b38fe91a2e907feda17630450fc97a7cd7b1
there are 2 findings:
   - 1 as a "hackertool" which probably makes sense considering the program has to do manual usb protocol
   - from a site called "MAxSecure" which i googled and all results i checked indicated its not a serious security scan site 
     > so personally i dont think its a real finding (whatever u make of that )
=> but if there are any doubts i recommend setting up a vm/old PC for programming the macros and then wiping it afterwards

if you have a different keyboard for which this works please send me a pr to include it in the readme for SEO

confirmed working:
1189:8890 / ch57x / maybe ch552 keyboards from aliexpress like https://www.aliexpress.us/item/3256804794211891.html and similar

<img src="image.png"
     alt="Markdown Monster icon"
     style="height:200px" />

ignore layout on software, position knobs on the right side and count keys off from left to right for each row starting at the top (eg if you have 6 keys total in 2 rows, use keys 1 thru 6 in the software not just the first 3 on the first 2 rows)
any keyboard with vendor id 1189/product id 8890 and max of 16 keys and 3 knobs should work even if layers and rgb aren't supported

https://github.com/jonnytest1/minikeyboard/issues/5: "4 Keys USB Mechanical Keyboard Hot Swap Mini Keyboard Custom Programming Knob Keyboard RGB Programming Macro"
<img width="220" height="220" alt="image" src="https://github.com/user-attachments/assets/51bee264-2ac7-413a-8e51-604fa9c6eefd" />


