# Using Personal Media Player

The window has six places: **Library**, **Recordings**, **Download**, **Merge**, **Capture**, and **Settings**.

Your files live under `%LocalAppData%\PersonalMediaPlayer\Library`. Settings can open that folder. Import copies a file into the library, so later changes to the original on disk do not change the copy.

## Library

Import photos (`png`, `jpg`, `jpeg`, `webp`, `bmp`, `gif`) and videos (`mp4`, `mkv`, `mov`, `avi`, `wmv`, `webm`, `m4v`, `m4a`). Drop files on the page, or use **Import**. A music file saved from Download uses `m4a` and is listed with the videos.

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

**Size & rotate** opens the editor for dimensions and rotation. Leaving a photo with unsaved marks, or closing the window, asks you to stay or leave.

## Videos

The player, a recording preview, a download preview, and Merge use the same bar: time, timeline, mute, volume, skip 10 seconds, play, and, where it applies, speed and full screen. The listening volume is remembered on this PC. Mute is remembered too. That level is only what you hear. It is not written into a saved file.

Hover the timeline to see a small frame and the time under the pointer. Playback stays where it is until you click or drag.

**Mark** saves the current moment. The list sits on the right of the video and opens from the menu button once a mark exists. A long note shows a short preview; click it to read the rest. Removing a mark asks first. Marks stay with the file when you rename it.

The player resumes a video that was left in the middle, and it can play again after the end. **Trim** on the player drags the ends of the timeline, previews that span, and saves a new file at the source size. The camera button saves a still of the current moment and opens it as a screenshot preview. **Restore original** is available after an overwrite that replaced the video.

**CC** appears on the bar, to the left of speed, when the video has captions. It is on the download preview and on a saved video. The words sit above the bar. **CC** hides them, and shows them again. One caption track toggles. More than one opens a list. Playback does not pause. A subtitle file sitting next to the video is not painted as a second track.

Hover a word for its Turkish meaning. The meaning sits just above that word. If the words around it are a known phrase, such as “of course” or “a little”, the phrase meaning is shown. “can't” and “I've” are looked up with the apostrophe still in the word. A word ending in “-ing”, such as “using”, follows the base word when that base is at least three letters. The word list and the phrase list are stored in the app. Hovering does not use the internet, and it does not send the subtitle anywhere.

**Edit** opens the editor. The picture fills the left side. The playback bar sits under the picture, so it does not cover the crop frame. The right side is one tool at a time. **Speed** is open when you arrive. A dot under a button means that edit is already set.

| Tool | What it edits |
| --- | --- |
| Speed | 0.25× to 4×. Pitch changes with the speed. The card shows the kept length and the length the saved file will have |
| Sound | Loudness written into the file. 100% is the original. Mute, 50%, and 100% are shortcuts |
| Rotate | Turn left or right, flip horizontally or vertically, and reset |
| Fade | Separate video and audio fades at the start and end of the finished edit, each from 0.1 to 30 seconds |
| Crop | Drag the frame. **Preview** shows only the kept area. The frame is on screen only while Crop is selected |
| Trim | White handles on the timeline, plus start and end times. Nudge by 1 second or 0.1 seconds, or set the playhead with **Start here** and **End here** |
| Cut | Drag a span to remove. A short click only moves the playhead |

**Trim** is the only tool that shows the white handles. **Cut** is the only tool that arms remove-part. Every other tool leaves the timeline on the playhead. Under the timeline, **−** and **+** zoom to 1×, 2×, 4×, 8×, or 16×. Ctrl and the mouse wheel do the same. The bar scrolls sideways. The playhead, handles, and cut marks stay on the real video.

Cut accepts typed times in **From** and **To**. `1:05.4`, `0:12`, `1:02:03`, and a number of seconds such as `90` all work. The span shows as a yellow band. Parts already removed stay red. **Remove** drops the yellow span. **Put back** restores one removed part. Preview skips each removed part with the sound still in step. A removed piece has to be at least half a second, and the video that remains has to be at least half a second.

The volume slider on the bar is only for listening. **Sound** is what Save writes. Fades are measured after trim, cuts, and speed. The preview shows them. A video with no audio track skips the audio fade on save.

**Audio** in the header extracts the sound as MP3 or WAV. You choose the name, then a folder in this app or any folder on this PC. Trim, cuts, speed, the saved loudness, and the audio fades are applied. The video file is left where it is. A video with no audio track stops with a clear message.

**Undo** and **Redo** remember speed, sound, rotate, fade, crop, trim, and cuts. Ctrl+Z and Ctrl+Y do the same, except while a time box is focused.

**Save** asks for a new file or an overwrite. Overwrite keeps the previous file with your other originals, and bookmarks move onto the new times. A mark inside a removed span is dropped. Save as new names the copy from the changes you made: speed, loudness, rotation, flips, fades, `crop`, `trim`, and `cut`, in that order. A second copy of the same name is `2`, then `3`. Cancel, or leaving with unsaved changes, asks you to stay or leave. Closing the window asks the same question. A save that is still running keeps the window open until it finishes.

## Download

Paste a YouTube, Shorts, `youtu.be`, or YouTube Music link and press **Look up**. The page lists each quality with its size. An estimate is marked **about**. **Audio only** is included when the clip has a separate audio track. **Add to queue** stays off until the text in the box matches the looked-up link exactly, including capital letters.

When the video has subtitles, Look up asks whether to include one. The list has the uploaded languages and, when YouTube has it, the original-language automatic caption. That automatic choice is marked **Automatic**. Translated automatic languages are not listed. **No subtitles** leaves them out. An audio-only download does not embed subtitles. The preview and the saved video use the same **CC** button and the same hover meanings as the player.

The queue downloads one item at a time. Each row shows waiting, a download percentage, paused, ready, failed, or cancelled.

| Button | What it does |
| --- | --- |
| Pause | Stops that item and keeps the part already saved |
| Resume | Continues that same download. A kept partial file does not start again until you press this |
| Cancel | Drops the item and deletes its partial file |
| Retry | Puts a failed or cancelled item back in line |
| Preview | Opens a finished file on the same video bar |

**Save** asks for a name and a place. **A folder in this app** puts it in Library, in an album you already have, or in a new album. **A folder on this PC** opens the system save dialog for any folder and any name. Video is an `.mp4`. Audio only is an `.m4a`. **Discard** deletes the preview without saving.

**History** is the card row under the queue. Each card shows the picture, the file name, the link, the quality, the date, and the folder. **Open** plays it in the app. **Folder** selects it in File Explorer. **Remove** drops the card only. The saved file stays where it is.

Leaving Download while something is queued, downloading, paused, or not yet saved asks you to stay or leave. Leaving cancels the rest and deletes unsaved files. Closing the window asks the same question. If nothing is waiting, closing pauses the active download and keeps the partial file for **Resume**.

## Merge

**Add videos** takes clips from this PC, or from Library, Videos, Recordings, and the albums you created. Audio-only files are not listed. You can join up to 24 clips.

Each clip is a numbered row and a block on the bar. A wider block is a longer source clip. The bright middle is the part you keep. The dim ends are cut off. Drag a white handle to move the start or the end. Click the bright part to play from that spot. The number sits above the bar so it stays clear of the handles.

**Duplicate** copies a clip and places the copy directly after it, with the same trim and fades. Later edits to one copy do not change the other, and they do not change the file on disk. Drag a row, or use the up and down icons, to set the order. **Remove** takes a clip out of the list.

The preview on the left plays the kept parts in order. The slider under the picture covers the whole joined length. Seeking and the 10-second skips stay inside those kept parts, and a paused preview stays paused. A fade out at the end of one clip meets the fade in at the start of the next. Picture and sound fades are separate, from 0.1 to 10 seconds.

**Merge** asks for a name and a place. **A folder in this app** puts the MP4 in Library, in an album you already have, or in a new album. **A folder on this PC** opens the system save dialog. The clips you picked stay where they are. Picture and sound are joined clip by clip at the same frame size, 30 frames a second, with stereo sound. A clip with no audio gets silence for its kept length so the next clip still starts on time.

Leaving Merge before the video is saved asks you to **Save**, **Discard**, or **Stay**. Closing the window asks the same question. If a join is already running, the choice is **Stay** or **Discard**.

## Capture

Pick a mode, then capture. The shot stays in the preview until you save it. You can mark it first. Leaving the page, or closing the window, with an unsaved shot asks you to stay or leave. Saving puts it in the library and in Screenshots, and stores the words Windows OCR could read so Library search can find them.

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

Stop opens a preview on the same video bar. You can trim the ends before saving. Save copies the recording into the library and into the Recordings album. Discard deletes an unsaved take. Closing the page, or closing the window, with an unsaved take or a recording still running asks you to stay or leave.

## Settings

Settings chooses light, dark, or the Windows theme, shows the library folder, and stores the screenshot cursor and delay. The shortcut list is shown there as a reminder.
