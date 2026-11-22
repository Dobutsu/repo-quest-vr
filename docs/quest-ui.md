# Quest network interface

The main menu button is **Network play**.

The Cloud nickname step also shows the player ID and **Generate new ID**. Generation changes the draft only.
Review the ID, save, confirm the progress warning, and restart to apply it. Cancel keeps the existing identity.
Russian VR tutorial instructions cover inventory, the map and expressions with controller icons.
Russian menu repair fills missing gamepad, chat confirmation, custom translation and rotation sensitivity entries.
Check the VR lamp count, OpenXR runtime label, expression wheel and keyboard in Russian as well.
In VR controls, check action names, section headings, toggles and disconnected/unbound statuses.
Choose the language in the game's Settings, not in the Windows builder.
Russian uses the existing Cyrillic game fonts; other unsupported interface languages fall back to English.

## Screens and transitions

| State | Window and actions |
|---|---|
| Cloud active | Current mode and nickname; Connect, Create game, Cloud settings, Change, Back |
| Steam active | Current mode and account/sign-in state; Connect, Create game, Steam account, Change, Back |
| Legacy Steam migrated | One-time Cloud notice; Continue or Change |
| Settings saved, restart pending | Active and selected modes; Quit game, Change selection, Back |
| Configuration error | Cloud settings, Change, Back; no silent Steam login |
| Mode selection | Cloud, Steam, Back; Steam skips Cloud fields |
| Cloud setup | Realtime ID, Voice ID, nickname, optional website; four steps with Back and Cancel |
| Setup review | All four values; Save, Back, Cancel |
| Save error | Previous settings remain; Retry or Back to settings |
| Cloud connection | Room code field; Connect, Back, Paste, and Open website if configured |
| Connecting | Progress and Cancel; success continues into the lobby |
| Cloud creation | Host/save ownership information; Continue or Back, then native region/save screens |
| Creating | Progress and Cancel; ready room opens its invitation |
| Steam connection | Login if needed, then friends; an empty list offers Refresh and Back |
| Steam creation | Login if needed, then the native hosting flow |

Back preserves draft values inside the Cloud wizard. Cancel never writes them.
Default ID/address and Paste only populate the field. They never save or submit it.
All Cloud fields must pass validation before Save can update the configuration.
The game reports a changed mode only after the file write succeeds.

The runtime keeps an active settings snapshot. Editing saved settings cannot change an in-progress connection's IDs or nickname.
Restart applies the saved selection. Restoring the active settings before restart removes the restart requirement.

## Created-room window

```text
GAME CREATED
Mode: Cloud
You are the host.

Share this code with other players:
{RQ1 code}

[Copy code]  [Open website]
[Close]
```

Copy code confirms the copy. Open website appears when a Cloud address is configured and opens it without uploading the code.
The host can reopen this window through Invite in the pre-game waiting lobby.
Starting the game closes the invitation and removes the local `quest-room.txt`; the pause menu has no room-code button.
Connect all players before starting. The in-game truck is not the waiting lobby.
Closing the browser returns to the invitation; closing the room removes the invitation.
The guest code-entry window also preserves input when opening the website.

## Translation and layout

The shared `tools/Shared/QuestStrings.cs` catalog supplies English/Russian copy.
Rebuild patched RepoXR with this catalog, then standalone and the embedded kit.
Preflight rejects an older catalog without the Network play marker.

Standard two-option Yes/No buttons translate both the backing label and the visible TMP text.
The game otherwise restores the backing English label each frame.
Dialog buttons are recognized in both supported languages.
Named actions such as Connect or Save keep their own translations.

Custom modals measure their title, body, optional field and visible button rows.
The black Panel uses explicit size and scale, with padded two-column controls.
Long codes and website summaries use a bounded scroll viewport; the buttons remain outside the scrolling body.
The input prefab aliases its original header/body text, so field descriptions use a separate text object.
Errors appear with the description and preserve the typed value.
Native selectable buttons, controller navigation and the existing VR keyboard remain in use.

## Acceptance

- As host, create a room and reopen its invitation in the waiting lobby.
- Start the game: close the invitation, remove `quest-room.txt`, and keep the pause menu free of room-code actions.
- Check that guests cannot open a host invitation, and that a delayed copy/browser action does not run after start.

- Check every screen above in Russian and English.
- Check standard Yes/No dialogs and switch locale in both directions.
- On Realtime ID and Voice ID, keep Default ID, Paste, Back and Cancel inside the black Panel.
- On room code entry, keep Paste and the optional Open website button inside the panel.
- Check all button rows, hover outlines and controller navigation with the VR keyboard open.
- Scroll a long RQ1 code and a long website address; keep primary actions accessible.
- Check browser return, clipboard focus, invalid input, Back, Cancel and save errors.
- Check first launch, one-time migration, manual Steam selection and restart-pending screens.
- Check both connection paths and headset/PC host invitations on actual devices.

Unit tests cover geometry, translations and settings behavior. They do not establish rendered appearance or Android clipboard access.
The final EXE must also pass packaged-tool preparation and the Android Harmony bake.
