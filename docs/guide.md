# Using Personal Media Player

The window has four places: **Library**, **Recordings**, **Capture**, and **Settings**.

Your files live under `%LocalAppData%\PersonalMediaPlayer\Library`. Settings can open that folder. Import copies a file into the library, so later changes to the original on disk do not change the copy.

## Library

Import photos (`png`, `jpg`, `jpeg`, `webp`, `bmp`, `gif`) and videos (`mp4`, `mkv`, `mov`, `avi`, `wmv`, `webm`, `m4v`). Drop files on the page, or use **Import**.

The left column is a mix of real albums and views:

| Entry | What it shows |
| --- | --- |
| All media | Every photo, video, screenshot, and recording |
| Photos | Images, including screenshots |
| Videos | Videos, including recordings |
| This month | Files imported during the current calendar month |
| Favorites | Items you marked with the heart |
| Unfiled | Files that are not in an album you created |
| Duplicates | Groups of files whose bytes match, even when the names differ |
| Screenshots | Captures you saved |
| Recordings | Recordings you saved |
| Your albums | Membership lists you create |
| Recently deleted | Files removed in the last 7 days |

**Add to** keeps the file in the album you are viewing and also adds it to another album. **Move to** takes it out of the album you are viewing. Dragging a file onto an album adds it. Screenshots and Recordings stay in those lists until the file is deleted.

A heart appears at the top-right of a thumbnail while the pointer is over it. In Favorites, remove only clears the heart. The file stays in the library.

Search matches the name, the type, the album, and text previously read from a screenshot. Opening a screenshot from a text search draws boxes around the matching words. Those boxes are only on the preview.

**Slideshow** plays the photos in the current view, starting from the selected photo. Left and Right change the photo. Esc leaves the slideshow.

Deleting from All media, Photos, Videos, This month, Screenshots, Recordings, or Duplicates moves the file to Recently deleted. Deleting from an album you created only removes it from that album. Restore and permanent delete are on the Recently deleted page. Items older than 7 days are removed on the next launch.

## Photos

Open a photo to mark it. The tools sit under the picture: pen, highlight, arrow, rectangle, text, blur, crop, eraser, and **Read text in an area**. Drag a rectangle with that last tool to read only that part. **Read text** on a fresh screenshot reads the whole image. Neither action changes the file.

**Save marks** asks whether to keep a new image or overwrite this one. Overwrite stores a backup of the file as it was before the edit. **Compare** appears when that backup exists. Drag the line or the slider: the left side is the original, the right side is the saved photo. **Restore original** copies the backup back over the photo.

**Size & rotate** opens the editor for dimensions and rotation. Leaving a photo with unsaved marks asks you to stay or leave before the window changes page.

## Videos

The player and a recording preview use the same bar: time, timeline, mute, volume, skip 10 seconds, play, speed, and full screen.

Hover the timeline to see a small frame and the time under the pointer. Playback stays where it is until you click or drag.

**Mark** saves the current moment. The list sits on the right of the video and opens from the menu button once a mark exists. A long note shows a short preview; click it to read the rest. Removing a mark asks first. Marks stay with the file when you rename it.

The player resumes a video that was left in the middle, and it can play again after the end. **Trim** on the player drags the ends of the timeline, previews that span, and saves a new file at the source size. The camera button saves a still of the current moment and opens it as a screenshot preview. **Restore original** is available after an overwrite that replaced the video.

**Edit** opens the editor. The picture keeps playing, and the tools sit in the column on the right. The line above the picture chooses what a drag on the timeline does:

| Timeline | Drag does this |
| --- | --- |
| Playhead | Moves through the video. Click and drag both seek |
| Trim ends | The white handles are the first and last frames you keep. Dragging the middle stays inside that span |
| Remove part | Drags the piece to delete. A short click only moves the playhead |

Trim can also be set from the playhead, or nudged by 1 second or 0.1 seconds. The card shows those times to a tenth of a second. Playback, picture and sound, stays inside the kept span.

Remove section accepts typed times in **From** and **To**. `1:05.4`, `0:12`, `1:02:03`, and a number of seconds such as `90` all work. The span shows as a yellow band on the timeline. Parts already removed stay red. **Remove this part** drops the yellow span. **Put back** restores one removed part. Preview skips each removed part with the sound still in step. A removed piece has to be at least half a second, and the video that remains has to be at least half a second.

Speed runs from 0.25× to 4×. The pitch changes with the speed. The card shows the length of the kept picture and the length the saved file will have. Volume runs from muted to 100%, and 100% is the original loudness. The editor’s own volume replaces the volume slider on the playback bar.

**Save** asks for a new file or an overwrite. Overwrite keeps the previous file with your other originals, and bookmarks move onto the new times. A mark inside a removed span is dropped. Save as new names the copy from the changes you made: `2x`, `80%` or `muted`, `trim`, and `cut`, in that order. A second copy of the same name is `2`, then `3`. Cancel, or leaving with unsaved changes, asks you to stay or leave. A save that is still running asks you to wait.

## Capture

Pick a mode, then capture. The shot stays in the preview until you save it. You can mark it first. Saving puts it in the library and in Screenshots, and stores the words Windows OCR could read so Library search can find them.

| Mode | What it captures |
| --- | --- |
| Selected area | The rectangle you drag |
| Single window | The window you click |
| Single monitor | The display you click |
| All monitors | Every display in one image |
| Fullscreen | The display under the pointer |
| Text file | A `.txt` file drawn as one tall image |

Esc or Alt+F4 cancels a selection. Delay can be 0, 3, 5, or 10 seconds and runs before the window hides. The cursor switch includes the pointer in screen shots. Text file mode does not wait or draw the cursor.

These shortcuts work while the app is in the background. They do not replace Print Screen.

| Shortcut | Mode |
| --- | --- |
| Ctrl+Alt+Shift+A | Selected area |
| Ctrl+Alt+Shift+W | Single window |
| Ctrl+Alt+Shift+M | Single monitor |
| Ctrl+Alt+Shift+D | All monitors |
| Ctrl+Alt+Shift+F | Fullscreen |

OCR uses the languages installed for Windows. Small or faint text is enlarged and darkened before it is read. If a word is still missing, install that language under Windows language options, then capture or save again.

## Recordings

Choose **Monitor**, **Window**, or **Selected area**. For an area, drag the rectangle first. The control bar appears after the selection, and recording starts when you press Start. The bar prefers a spot outside the recorded rectangle.

**Include system audio** records the default speakers. There is no microphone. Silence at the start of a take is kept, so the picture and the sound stay lined up.

Stop opens a preview on the same video bar. You can trim the ends before saving. Save copies the recording into the library and into the Recordings album. Discard deletes an unsaved take. Closing the page with an unsaved take asks you to stay or leave.

## Settings

Settings chooses light, dark, or the Windows theme, shows the library folder, and stores the screenshot cursor and delay. The shortcut list is shown there as a reminder.
