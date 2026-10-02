# Using Personal Media Player

The window opens on **Home**. The other places are **Library**, **Playlists**, **Saved words**, **Recordings**, **Download**, **Merge**, **Capture**, and **Settings**.

## Home

Home is the start page. A row is hidden when it has nothing to show.

**Continue watching** is the row of videos left in the middle. A place is kept after 5 seconds, and it is dropped in the last 10 seconds or when the video ends. Up to 16 items are shown, newest first. **All** opens every one of them, and Back returns to Home. A library file shows its picture from the file and opens at the saved time. An online video shows that video’s thumbnail. A YouTube page uses its YouTube picture. Another page uses the picture found when it was opened. Opening it looks the page up again and continues when that stream can seek.

**Playlists** shows each list as a card. The cover uses up to four videos. One picture fills it. Two sit side by side. Three put the first picture on the left and the other two on the right. Four form a grid. A library file uses its own picture. A YouTube page uses its normal picture. Another site uses the picture saved after that page has been opened. A list with no picture shows the playlist mark. The name and the video count sit under the cover. **Play** on the cover starts the first video that can be opened. It stays hidden when nothing in the list can be opened. Clicking the rest of the card opens that playlist. Hovering a list slides in cards for its videos. The cards stay while the pointer is over that list or the cards, and they leave when the pointer does. Choosing a preview card opens that video. **More** opens that same playlist. **All** opens Playlists.

**Recent** shows the latest files added to the library, including screenshots and recordings. Clicking one opens it. A video shows a play mark. A photo does not. **All** opens Library.

**Saved words** shows the latest words that can still be opened, one line each. Clicking a word opens that video at that moment. **All** opens Saved words.

**Downloads** appears while something is queued, downloading, paused, ready to save, or failed. Clicking it, or **All**, opens Download.

When none of those rows have anything, Home says so and offers **Import** and **Open link**.

Your files live under `%LocalAppData%\PersonalMediaPlayer\Library`. Settings can open that folder. Import and drop ask whether to copy a file into the library or use it where it is. A copy is unchanged by later edits to the original. A linked file is listed with the other files and is not copied. Removing a linked file from the library leaves the original in place. If that file moves, **Locate** points the same library item at the new place and keeps its saved words, bookmarks, and playback position.

## Library

Import photos (`png`, `jpg`, `jpeg`, `webp`, `bmp`, `gif`) and videos (`mp4`, `mkv`, `mov`, `avi`, `wmv`, `webm`, `m4v`, `m4a`). Drop files on the page, or use **Import**. Choose **Copy** or **Use where it is**. **Copy** puts a new file in the library. **Use where it is** lists the file from its current folder. A music file saved from Download uses `m4a` and is listed with the videos. Download, Merge, the editor, and Recordings still save a copy.

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

Ctrl+click, Shift+click, and Ctrl+A select the files in the current view. A bar shows how many are selected. **Add to playlist** adds the videos that are on this PC. Photos and missing files stay out. **Add to favorites** and **Remove from favorites** change the heart for every selected file. Recently deleted does not offer those actions. Ctrl+A in the search box still selects the search text.

Search matches the name, the type, the album, and text previously read from a screenshot. Opening a screenshot from a text search draws boxes around the matching words. Those boxes are only on the preview.

**Slideshow** plays the photos in the current view, starting from the selected photo. Left and Right change the photo. Esc leaves the slideshow.

Renaming a library video or a recording keeps its place in Continue watching on Home. Locating a linked file that moved does the same for saved words, bookmarks, and the playback position. Locate accepts another video for a video, or another photo for a photo. Choosing the other type shows a warning and leaves the library entry and its saved data unchanged. A missing linked file stays in the library so it can be located. Its card says **Missing**.

**Open link** asks for an `http` or `https` address that is already the video. The app checks the response, then plays it. The address can leave off a file ending. A web page shows an error and Library stays open. The video is played here and is kept out of the library. While the check runs, the page says **Checking the link…**, and the rest of the window still accepts input.

Deleting a copied file from All media, Photos, Videos, This month, Screenshots, Recordings, or Duplicates moves that copy to Recently deleted. Deleting a linked file only removes it from the library. The original stays where it is. Deleting from an album you created only removes it from that album. Restore and permanent delete are on the Recently deleted page. Items older than 7 days are removed on the next launch. Overwriting a linked photo or video changes that file. The previous bytes are kept as a backup. Saving a linked photo under a new name writes the new file beside it and leaves the old file on disk.

## Playlists

Playlists collect library videos and online videos, and play them in order. Type a name and press **Create**, then **Add videos**. The picker hides videos already in that list, and you can select several. While a page is streaming, **Add to playlist** saves that page link. The saved link is the page address, not the temporary stream address. Opening it later looks the page up again and shows that page’s captions. Audio and quality appear when the stream opens. **Play** starts the first visible video that can be opened. **Rename** and **Delete** apply to the list. Removing a video takes it out of the list and leaves a library file where it is.

Search by name filters the rows. The videos stay in the playlist, and clearing the search shows them again. Drag the grip to rearrange a row. **Name** sorts A to Z. **Date added** puts the newest first. The new order is saved and is the same after a restart. Each row can still move up, move down, or leave the list. Up and down move among the rows the search is showing. A file that is gone says **Not on this PC**. An online video says **Online**.

Opening a playlist video shows the rest beside the player: **Previous**, **Next**, and **Play the next video when this one ends**. That switch stays off until you turn it on. Each row shows a picture when one is available. A file uses its own picture, a YouTube page uses its normal picture, and another site shows a picture after that page has been opened. Drag the grip, or use the arrows, to change the order while a video plays. The video that is playing stays on screen, and the new order is saved. Clicking a row switches video without leaving the player. Renaming a library video, a recording, or a saved download keeps it in the playlist. Locating a moved linked file does too. An online link is left as it is.

## Saved words

Click a caption word. A card shows the English word or phrase, the Turkish meaning, and the sentence. The small hover tip still hides when the pointer leaves. The card stays until **Close** or a click outside it. Playback continues.

**Save** stores the word on this PC, with the video, the cue time, and the audio and caption languages selected at that moment. Opening the word later goes to the same moment and restores those languages. A word saved on a stream stores the page, or the direct video address, and opens that address again. On the same page, choosing the word seeks there. A stream that cannot seek shows **This stream cannot seek.**

The **Saved words** page lists one word per line, grouped by video. The columns are the word, the Turkish meaning, the sentence, and the time. Search matches the English word and the Turkish meaning. Click a word to open its video and show the list beside the player, with the matching word highlighted. **Words** opens that list. Clicking **Words** again, or the hide button on the list, closes it. While it is hidden, the video uses the full width. Opening another video does not bring the list back. Remove asks first. A word saved before it had a place stays in the list. Saving it again while that video is open fills in the place and time. A missing file stays listed.

## Photos

Open a photo to mark it. The tools sit under the picture: pen, highlight, arrow, rectangle, text, blur, crop, eraser, and **Read text in an area**. Drag a rectangle with that last tool to read only that part. **Read text** on a fresh screenshot reads the whole image. Neither action changes the file.

**Save marks** asks whether to keep a new image or overwrite this one. Overwrite stores a backup of the file as it was before the edit. **Compare** appears when that backup exists. Drag the line or the slider: the left side is the original, the right side is the saved photo. **Restore original** copies the backup back over the photo.

**Size & rotate** opens the editor for dimensions and rotation. Leaving a photo with unsaved marks, or closing the window, asks you to stay or leave.

## Videos

The player, a recording preview, a download preview, and Merge use the same bar: time, timeline, mute, volume, skip 10 seconds, play, and full screen where that page has it. The gear just left of full screen is **Playback**. It holds speed. On the player it also holds quality, audio language, captions, and section repeat. Editor and Merge keep speed on their own tools, so the gear stays hidden there. `F` toggles full screen on the player when the cursor is outside a text box. The listening volume is remembered on this PC. Mute is remembered too. That level is only what you hear. It is not written into a saved file.

Hover the timeline to see a small frame and the time under the pointer. Playback stays where it is until you click or drag.

**Mark** saves the current moment. The list sits on the right of the video and opens from the menu button once a mark exists. A long note shows a short preview; click it to read the rest. Removing a mark asks first. Marks stay with the file when you rename it.

The player resumes a video that was left in the middle, and it can play again after the end. **Trim** on the player drags the ends of the timeline, previews that span, and saves a new file at the source size. The camera button saves a still of the current moment and opens it as a screenshot preview. **Restore original** is available after an overwrite that replaced the video.

**CC** is inside the gear when the video has captions. It is on the download preview and on a saved video. The words sit above the bar. **CC** hides them, and shows them again. One caption track toggles. More than one opens a list. Playback does not pause. A subtitle file sitting next to the video is not painted as a second track.

Uploaded captions are listed first. YouTube automatic captions follow, including translations, with one English name for each language. A translation can take about a minute the first time you choose it. A caption already stored on this PC opens immediately. Audio names in the gear are English as well. An original track and a dubbed track keep that word.

**Start** and **End** in the gear mark a section, and playback loops between them. The marks need at least 0.4 seconds between them. **Clear** removes the loop.

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

Save keeps the first caption track. Trim, cut, and speed move the cue times with the picture. Fade, crop, and rotate leave the cue times where they are. The captions are written back into the saved MP4. A video with no captions is unchanged. If that caption step cannot finish, the picture is still saved.

## Watch a link

**Open link** on Library plays a direct video address after the check described above. The browser can send a page into the same player.

`extension/` is a Chrome and Edge package. Load that folder unpacked. It is left out of the app build. The toolbar action is **Open in Personal Media Player**.

| Page | What opens |
| --- | --- |
| One direct video | That file. Playback starts at the current time when the stream can seek |
| Several direct videos | A picker, then the video you choose |
| A tab that is itself a video file | That address |
| A page that hides the file, such as YouTube | The page is resolved and played as a stream. The window says **Opening…** |
| Nothing the app can play | The action stays off, titled **This page has no direct video file yet.** |

The app keeps one window. A second open uses that window. A page that needs a license shows **This video is protected by a license and stays in the browser.** The extension asks only for the tab address.

On a stream, **Edit**, **Trim**, **Restore original**, and the still-frame button are hidden. **Playlist** appears when that stream was opened from a playlist, and that list stays beside the player. **Words** stays. The saved-words list stays closed until you open it. Bookmarks and **Save a copy** appear when the stream was opened from a page. **Save a copy** adds that page to the Download queue, starts the download, and leaves the stream playing. **Add to playlist** saves that same page address in a playlist. A stream that cannot seek keeps play, pause, volume, speed, and full screen, and leaves the timeline and section repeat off. The line under the title says **Streaming**, or **Streaming. This video cannot seek.** Reaching the end and pressing play looks a page up again. A direct file is checked again and starts from the beginning.

Playback settings on a resolved stream can change quality and audio language. Captions for that page use the same list as Download. **Settings** can prefer the original audio or one language, and a subtitle language or no subtitles. The extension and a playlist use those choices when the page opens. A missing audio language plays the original track. A missing subtitle language leaves subtitles off. A saved word still opens with the languages stored on that word.

## Download

Paste a YouTube, Shorts, `youtu.be`, or YouTube Music link and press **Look up**. The page lists each quality with its size. An estimate is marked **about**. **Audio only** is included when the clip has a separate audio track. **Add to queue** stays off until the text in the box matches the looked-up link exactly, including capital letters.

A playlist address, or a link that has `list=` and no video id, lists up to 200 episodes. Check the ones to add and pick one quality for all of them: Best available, 1080p, 720p, 480p, 360p, or Audio only. **Add to queue** puts each chosen episode on its own. Captions are chosen on a single video. A normal watch link stays one video, including a watch link that also carries `list=`.

When a single video has subtitles, Look up asks whether to include one. Uploaded captions come first, then YouTube’s automatic captions, including translations, with one English name per language. A translation can take about a minute. **No subtitles** leaves them out. An audio-only download stays without subtitles. The preview and the saved video use the same **CC** control and the same hover meanings as the player.

The queue downloads one item at a time and keeps going after you leave this page. Each row shows waiting, a download percentage, paused, ready, failed, or cancelled.

| Button | What it does |
| --- | --- |
| Pause | Stops that item and keeps the part already saved |
| Resume | Continues that same download. A kept partial file does not start again until you press this |
| Cancel | Drops the item and deletes its partial file |
| Retry | Puts a failed or cancelled item back in line |
| Preview | Opens a finished file on the same video bar |

**Save** asks for a name and a place. **A folder in this app** puts it in Library, in an album you already have, or in a new album. **A folder on this PC** opens the system save dialog for any folder and any name. Video is an `.mp4`. Audio only is an `.m4a`. **Discard** deletes the preview without saving.

**History** is the card row under the queue. Each card shows the picture, the file name, the link, the quality, the date, and the folder. **Open** plays it in the app. **Folder** selects it in File Explorer. **Remove** drops the card only. The saved file stays where it is.

Leaving Download does not stop the queue. Pause, resume, cancel, and save stay on this page. A finished file stays ready until you save or discard it. Closing the app pauses the active download and any that are still waiting, and keeps the partial file for **Resume**.

## Merge

**Add videos** takes clips from this PC, or from Library, Videos, Recordings, and the albums you created. Audio-only files are not listed. You can join up to 24 clips.

Each clip is a numbered row and a block on the bar. A wider block is a longer source clip. The bright middle is the part you keep. The dim ends are cut off. Drag a white handle to move the start or the end. Click the bright part to play from that spot. The number sits above the bar so it stays clear of the handles.

**Duplicate** copies a clip and places the copy directly after it, with the same trim and fades. Later edits to one copy do not change the other, and they do not change the file on disk. Drag a row, or use the up and down icons, to set the order. **Remove** takes a clip out of the list.

The preview on the left plays the kept parts in order. The slider under the picture covers the whole joined length. Seeking and the 10-second skips stay inside those kept parts, and a paused preview stays paused. A fade out at the end of one clip meets the fade in at the start of the next. Picture and sound fades are separate, from 0.1 to 10 seconds.

**Merge** asks for a name and a place. **A folder in this app** puts the MP4 in Library, in an album you already have, or in a new album. **A folder on this PC** opens the system save dialog. The clips you picked stay where they are. Picture and sound are joined clip by clip at the same frame size, 30 frames a second, with stereo sound. A clip with no audio gets silence for its kept length so the next clip still starts on time. The first caption track of each clip is kept. Its cue times follow that clip’s in and out points, then those captions are joined in order and written into the saved MP4.

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

Settings chooses light, dark, or the Windows theme, shows the library folder, and stores the screenshot cursor and delay. **Online videos** stores the default audio and subtitles for the extension and for playlists. The shortcut list is shown there as a reminder.
