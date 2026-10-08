# Using Personal Media Player

The window opens on **Home**. The other places are **Library**, **Playlists**, **Saved words**, **Recordings**, **Download**, **Merge**, **Capture**, and **Settings**. The left menu groups **Media** and **Tools**. The chosen item is a rounded highlight, and those group names hide when the menu is collapsed. Choosing a place fades that page in.

## Home

Home is the start page. A row is hidden when it has nothing to show. Drag a row of cards sideways to scroll it. The bar at the bottom still works. A short press still opens the card.

**Continue watching** is the row of videos left in the middle. A place is kept after 5 seconds, and it is dropped in the last 10 seconds or when the video ends. Up to 16 items are shown, newest first. **All** opens every one of them, and Back returns to Home. A downloaded video shows that video’s thumbnail. Another library file shows its picture from the file. Either one opens at the saved time. An online video shows that video’s thumbnail. A YouTube page uses its YouTube picture. Another page uses the picture found when it was opened. Opening it looks the page up again and continues when that stream can seek.

**Playlists** shows each list as a card. The cover uses up to four videos. One picture fills it. Two sit side by side. Three put the first picture on the left and the other two on the right. Four form a grid. A downloaded video uses that video’s thumbnail. Another library file uses its own picture. A YouTube page uses its normal picture. Another site uses the picture saved after that page has been opened. A list with no picture shows the playlist mark. The name and the video count sit under the cover. **Play** on the cover starts the first video that can be opened. It stays hidden when nothing in the list can be opened. Clicking the rest of the card opens that playlist. Hovering a list slides in cards for its videos. The cards stay while the pointer is over that list or the cards, and they leave when the pointer does. Choosing a preview card opens that video. **More** opens that same playlist. **All** opens Playlists.

**Recent** shows the latest files added to the library, including screenshots and recordings. Clicking one opens it. A video shows a play mark. A photo does not. **All** opens Library.

**Saved words** shows the latest words that can still be opened, one line each. Clicking a word opens that video at that moment. **All** opens Saved words.

**Downloads** appears while something is queued, downloading, paused, ready to save, or failed. Clicking it, or **All**, opens Download.

**Search** sits under the title. It looks through library file names and the titles of online videos saved in playlists. Each result shows a thumbnail, the name, and whether it is a video or photo. A result with no picture shows a video or photo icon. A library file keeps the **Library** label. When that video is also in playlists, the same row lists those playlist names. An online video appears once, with every playlist it is in. A photo opens as a photo. A video opens in the player. The same search lists caption lines already stored for library videos. A caption file beside the video is included, and so is a caption saved the last time that video was opened. Each caption row shows the video name, the time, and the line. Clicking it opens the player at that moment. Up to 8 lines from one video are shown, and up to 80 lines in all. When there are more, Home says **More caption matches are not listed.** An online video opens from the first playlist in that list. Right-click a video result, or press **Play next** or **Add to queue**. **Play next** puts that video first in the queue. **Add to queue** puts it at the end. The current video keeps playing. A photo has no queue command. If nothing matches, Home says **Nothing matches.** Clearing the box shows the rows again. Opening a result and coming back keeps the text, the results, and your place in the list until you close the app. Library, each playlist, and Saved words, including the list beside the player, do the same.

When none of those rows have anything, Home says so and offers **Import** and **Open link**.

Your files live under `%LocalAppData%\PersonalMediaPlayer\Library`. Settings can open that folder. Import and drop ask whether to copy a file into the library or use it where it is. A copy is unchanged by later edits to the original. A linked file is listed with the other files and is not copied. Removing a linked file from the library leaves the original in place. If that file moves, **Locate** points the same library item at the new place and keeps its saved words, bookmarks, the playback position, and playlist entries. **Locate in folder** does that for several missing files after showing the matches. Library can also watch a folder on this PC. New photos and videos there show up while the app is open. **Refresh** looks again.

## Library

Import photos (`png`, `jpg`, `jpeg`, `webp`, `bmp`, `gif`) and videos (`mp4`, `mkv`, `mov`, `avi`, `wmv`, `webm`, `m4v`, `m4a`). Drop files on the page, or use **Import**. Choose **Copy** or **Use where it is**. **Copy** puts a new file in the library. **Use where it is** lists the file from its current folder. A music file saved from Download uses `m4a` and is listed with the videos. Download, Merge, the editor, and Recordings still save a copy.

**Connect folder**, under **On this PC**, watches a folder and its subfolders while the app is open. A new photo or video appears in Library on its own after its size has stayed the same for a few seconds. If the size changes again, the file drops out until it stays still. A folder moved into the connected folder brings the photos and videos inside it. A connected folder that is missing when the app opens is watched again when it comes back. The same path is listed once. Renaming or moving a file inside that folder keeps its playlists, bookmarks, saved words, and playback position. If the app cannot tell that it is the same file, it leaves the old item unchanged. A deleted file stays in the library and its card says **Missing**, so **Locate** can find it later. **Refresh** looks through the folder again. **Disconnect** stops watching that folder. The files stay on this PC and in the library. Import still asks **Copy** or **Use where it is**. A folder inside the library, a folder that contains the library, or a folder that overlaps one already connected cannot be connected.

Folders, views, and albums are listed together. A wide window keeps that list on the left, beside the files and the details. A narrower window keeps the list on the left and moves the details below the files:

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

Ctrl+click, Shift+click, and Ctrl+A select the files in the current view. A bar shows how many are selected. **Add to playlist** adds the videos that are on this PC. **Play next** puts those videos at the front of the queue. **Add to queue** adds them at the end. The video that is playing keeps going. Photos and missing files stay out of both. **Add to favorites** and **Remove from favorites** change the heart for every selected file. Recently deleted does not offer those actions. Ctrl+A in the search box still selects the search text.

Search matches the name, the type, the album, and text previously read from a screenshot. Opening a screenshot from a text search draws boxes around the matching words. Those boxes are only on the preview. Coming back keeps this search, the album you had open, and your place in the results until you close the app.

**Slideshow** plays the photos in the current view, starting from the selected photo. Left and Right change the photo. Esc leaves the slideshow.

Renaming a library video or a recording keeps its place in Continue watching on Home. Locating a linked file that moved does the same for saved words, bookmarks, the playback position, and playlist entries. Locate accepts another video for a video, or another photo for a photo. Choosing the other type shows a warning and leaves the library entry and its saved data unchanged. A missing linked file stays in the library so it can be located. Its card says **Missing**. Refreshing a connected folder leaves that missing file in place.

Select the missing files and choose **Locate in folder**. Point at the folder they moved to, including its subfolders. The app lists each match before it changes anything. A file matches when its name appears once, or when one path inside that folder repeats the old folders. When more than one file could fit, choose the right one. Nothing is guessed. **Locate** still picks one file.

**Open link** asks for an `http` or `https` address that is already the video. The app checks the response, then plays it. The address can leave off a file ending. A web page shows an error and Library stays open. The video is played here and is kept out of the library. While the check runs, the page says **Checking the link…**, and the rest of the window still accepts input.

Deleting a copied file from All media, Photos, Videos, This month, Screenshots, Recordings, or Duplicates moves that copy to Recently deleted. Deleting a linked file only removes it from the library. The original stays where it is. Deleting from an album you created only removes it from that album. Restore and permanent delete are on the Recently deleted page. Items older than 7 days are removed on the next launch. Overwriting a linked photo or video changes that file. The previous bytes are kept as a backup. Saving a linked photo under a new name writes the new file beside it and leaves the old file on disk.

## Playlists

Playlists collect library videos and online videos, and play them in order. Type a name and press **Create**, then **Add videos**. The picker hides videos already in that list, and you can select several. While a page is streaming, **Add to playlist** saves that page link. The saved link is the page address, not the temporary stream address. Opening it later looks the page up again and shows that page’s captions. Audio and quality appear when the stream opens. **Play** starts the first visible video that can be opened. **Rename** and **Delete** apply to the list. Removing a video takes it out of the list and leaves a library file where it is.

Each row shows a picture when one is available. A downloaded video uses that video’s thumbnail. Another file on this PC uses its own picture. A YouTube page uses its normal picture. Another site shows a picture after that page has been opened. A missing file says **Not on this PC** and shows no picture. Search by name filters the rows. The videos stay in the playlist, and clearing the search shows them again. Each playlist keeps its own search and place in the list until you close the app. Drag the grip to rearrange a row. **Name** sorts A to Z. **Date added** puts the newest first. The new order is saved and is the same after a restart. Shuffle does not change that saved order. Each row can still move up, move down, or leave the list. Up and down move among the rows that are showing. Removing a video, moving it, or sorting shows a message with **Undo**. Undo puts that list back. Closing the message leaves the change in place. A file that is gone says **Not on this PC**. An online video says **Online**. A video is marked watched when it plays to the end. Right-click a row to mark it watched or unwatched. The row says **Watched**, and the list shows a count such as 6 of 17 watched. **Unwatched only** hides videos that are already watched. The marks are saved and stay after a restart. This works for a library video and an online video.

Opening a playlist video shows the rest beside the player: **Previous**, **Next**, **Shuffle**, **Repeat**, and **Play the next video when this one ends**. That switch stays off until you turn it on. **Shuffle** plays the videos in a random order, and each video waits until the others have played. **Repeat** replays this video, replays the playlist, or stays off. A filled button means that choice is on, and the line under the buttons says so. The card on the video shows it while the list is closed. Library videos and online videos both follow it. Closing the app turns shuffle and repeat off. Each row shows a picture when one is available. A downloaded video uses that video’s thumbnail. Another file uses its own picture, a YouTube page uses its normal picture, and another site shows a picture after that page has been opened. Drag the grip, or use the arrows, to change the order while a video plays. The video that is playing stays on screen, and the new order is saved. The same message appears here. **Undo** puts the list back and leaves that video playing. Right-click a row to mark it watched or unwatched. The watched count is shown above the list. **Unwatched only** hides the ones already watched, and Previous, Next, shuffle, and play next then skip those. Closing the list leaves a small card on the video with the playlist name and the watched count. Opening a video that is not in that playlist hides the list, that card, and the Playlist button together. Another video from the same playlist keeps them, and a video started from the queue does too. The video uses the full width while that card is showing. Choosing the card opens the list again. Clicking a row switches video without leaving the player. Renaming a library video, a recording, or a saved download keeps it in the playlist. Locating a moved linked file does too. An online link is left as it is.

**Play next** and **Add to queue** are on a playlist row’s right-click menu, in Library, and on a Home search result. **Play next** puts the video first in the waiting queue. **Add to queue** puts it at the end. The current video keeps playing, and nothing starts if nothing is already playing. Drag the grip beside a queued video to change its place. That order is the order **Next** and the end of the video use, before shuffle and repeat. **Previous** still moves through the playlist. The list sits beside the player. **Remove** drops one row. **Clear** removes every waiting video at once, and the video that is playing keeps going. A message offers **Undo**, which puts the cleared list, or the video you just removed, back in the same order. Closing the message leaves the change in place. Adding a video, dragging a row, or playing the next queued video replaces that undo. The list stays open after **Clear** so **Undo** can be pressed. The small player hides it until you return. When the queue finishes, the playlist continues with shuffle, repeat, and play the next video when this one ends, as they already are. If no playlist is open, playback stops. A library file and an online page both work. The page address is kept, not a temporary stream address. Adding, reordering, and clearing write only the waiting queue. **Save as playlist** asks for a name and copies those videos into a new playlist, in the same order. A video queued more than once is stored once. A missing file stays in the new playlist. An online video keeps its page address. The waiting queue stays in its own list. The download queue is left alone. Closing the app keeps these waiting videos in the same order, including ones put first with **Play next**. Opening the app does not start them. A file that is no longer on this PC stays in the list and says **Missing**. The undo message does not come back. The video that was open is saved with that list. Closing the app and opening it again shows that video paused in the small player at the bottom right, at the same moment. The waiting videos do not start. **Full player** shows that video with the waiting queue beside it. Press play to continue. You may hear the video for a moment while it finds that place. **Close** on the player clears the open video, so it does not come back, and leaves the waiting queue. A file that is no longer on this PC still appears and says **Missing**.

## Saved words

Click a caption word. A card shows the English word or phrase, the Turkish meaning, and the sentence. The small hover tip still hides when the pointer leaves. The card stays until **Close** or a click outside it. Playback continues.

**Save** stores the word on this PC, with the video, the cue time, and the audio and caption languages selected at that moment. Opening the word later goes to the same moment and restores those languages. **Save** on the caption line stores that whole line. **Save** on a subtitle search result stores that result. The line is kept as the word and the sentence, the Turkish meaning is left empty, and the time and languages are kept. Opening it later goes to that moment. Removing that line on Saved words clears **Saved** on the caption and on the search result. A word saved on a stream stores the page, or the direct video address, and opens that address again. On the same page, choosing the word seeks there. A stream that cannot seek shows **This stream cannot seek.**

The **Saved words** page lists one word per line, grouped by video. A wide window uses columns for the word, the Turkish meaning, the sentence, and the time. A narrower window stacks those on one card per word. Search matches the English word and the Turkish meaning. Coming back keeps this search until you close the app. The list beside the player keeps a separate search for each video. Choosing a word clears that video’s search in the player list so the word stays visible. Click a word to open its video and show the list beside the player, with the matching word highlighted. **Words** opens that list. Clicking **Words** again, or the hide button on the list, closes it. While it is hidden, the video uses the full width. Opening another video does not bring the list back. Remove asks first. A word saved before it had a place stays in the list. Saving it again while that video is open fills in the place and time. A missing file stays listed.

## Photos

Open a photo to mark it. The tools sit under the picture: pen, highlight, arrow, rectangle, text, blur, crop, eraser, and **Read text in an area**. Drag a rectangle with that last tool to read only that part. **Read text** on a fresh screenshot reads the whole image. Neither action changes the file.

**Save marks** asks whether to keep a new image or overwrite this one. Overwrite stores a backup of the file as it was before the edit. **Compare** appears when that backup exists. Drag the line or the slider: the left side is the original, the right side is the saved photo. **Restore original** copies the backup back over the photo.

**Size & rotate** opens the editor for dimensions and rotation. Leaving a photo with unsaved marks, or closing the window, asks you to stay or leave.

## Videos

The player, a recording preview, a download preview, and Merge use the same bar: time, timeline, mute, volume, skip 10 seconds, play, and full screen where that page has it. The gear just left of full screen is **Playback**. It holds speed. On the player it also holds quality, audio language, captions, and section repeat. Editor and Merge keep speed on their own tools, so the gear stays hidden there. `F` toggles full screen on the player when the cursor is outside a text box. The full player and the small player share one listening volume, and that level is remembered on this PC. A download preview keeps its own volume while the app is open, starting at 80 until you move it. Moving one volume does not change the other. Mute on the player is remembered too. That level is only what you hear. It is not written into a saved file.

Leaving the player for another page keeps the video going in a small player at the bottom right. That player has play, pause, a timeline, volume, **Back 10 seconds**, **Forward 10 seconds**, **Next**, **Close**, and **Full player**. **Back 10 seconds** and **Forward 10 seconds** move inside the video that is playing. A stream that cannot seek leaves those two off. **Next** plays the next waiting video, and after the queue it plays the next playlist video. The small player stays on screen. Adding a video to that playlist turns **Next** on. Removing the videos after the current one turns it off. Its volume is the same as the full player. Drag the timeline to move through the video. A click on the picture also returns to the large player, at the same moment. The page behind the full player is hidden, so it does not show through the video name. The name stays on one line and shortens before it meets the buttons. Audio, captions, and the playlist choices stay as they were. Opening a different video, or playing a download, editor, merge, or recording preview, pauses the other one. Each video keeps its own place. Returning to the full player pauses a preview that would otherwise keep playing out of sight. A saved word from the same video jumps to that time. **Close** stops the small player and clears the open video, so opening the app again does not bring it back. The waiting queue stays. Closing the app while a video is open brings that video back paused in this small player, at the same moment. **Full player** shows the waiting queue with it. This works for a file on this PC and for an online video. The queue keeps playing while the small player is showing. Its list appears again when you return to the full player, including a clear or a removal you can still undo.

The download preview sits on a solid card with a colored edge. Its playback controls are above the picture, in a dark bar with light icons, so they stay readable in a light or dark theme.

Hover the timeline to see a small frame and the time under the pointer. Playback stays where it is until you click or drag.

**Mark** saves the current moment. The list sits on the right of the video and opens from the menu button once a mark exists. A long note shows a short preview; click it to read the rest. Removing a mark asks first. Marks stay with the file when you rename it.

**Chapters**, beside **Words**, shows or hides the chapter list. Playback keeps going. The list starts open, and the app remembers that choice until you close it. Each row shows the chapter name and its start time. The chapter that is playing stays marked, including when you open the list again. Click a row to jump there. A file uses the chapters stored in that file. An online video uses the chapters that came back with the page. A video with no chapters hides the button and the list. Bookmarks are left as they are. The list hides while the small player is showing and comes back with the full player when you had left it open.

The player resumes a video that was left in the middle, and it can play again after the end. **Trim** on the player drags the ends of the timeline, previews that span, and saves a new file at the source size. The camera button saves a still of the current moment and opens it as a screenshot preview. **Restore original** is available after an overwrite that replaced the video.

**CC** is inside the gear when the video has captions. It is on the download preview and on a saved video. **Export** sits with it and saves the current captions as an SRT file. You choose the name and the folder. A video on this PC saves the captions stored for it. An online video saves the language that is selected. The download preview does the same. The words sit on a dark plate above the bar, on top of the picture. **CC** hides them, and shows them again. One caption track toggles. More than one opens a list. Playback does not pause. A subtitle file sitting next to the video is not painted as a second track. The video starts before those words are ready. A caption file beside the video, or one already stored, shows up as soon as it has been read. A video with no captions is remembered, so the next open does not scan it again.

**Search**, beside **Words**, opens a subtitle search for the video that is playing. Type a word to see the matching lines and their times. Click a line to jump there. **Save** on a result stores that line and leaves the video where it is. This works for a video on this PC and for an online video when subtitles are available. Changing the subtitle language searches that language. A video with no subtitles says so, and turning subtitles off says they are off. The list hides while the small player is showing and comes back with the full player when you had left it open. Home searches the captions already stored on this PC across the library.

Uploaded captions are listed first. YouTube automatic captions follow, including translations, with one English name for each language. The video starts from the stream address. Translated caption names are added after playback has started. Choosing a translation can take about a minute the first time. A caption already stored on this PC opens immediately. Audio names in the gear are English as well. An original track and a dubbed track keep that word.

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
| Trim | White handles and the white line on the timeline, plus start and end times. Nudge by 1 second or 0.1 seconds, or set the playhead with **Start here** and **End here**. The line and handles show only while Trim is selected |
| Cut | Drag a span to remove. The band shows its length. A short click only moves the playhead |
| Parts | The list of changed stretches. Type a start and end, or drag the audio track. **Sound back** restores that one stretch |

**Trim** is the only tool that shows the white line and the white handles. **Cut** is the only tool that arms remove-part. Every other tool leaves the timeline on the playhead. An **Audio** track sits under that timeline. When the file has captions, those lines sit between the timeline and the audio track and follow the zoom. Click a line to put the playhead at its start. A line outside the trim, or inside a removed part, is dimmed. **Part volume** is how loud the next stretch is. It starts at 0%, so a drag still silences that part. **Silence**, **30%**, and **150%** are shortcuts, and the slider goes from 0% to 200%. After a drag, moving the slider changes that stretch until you click empty audio. 0% silences it, and 100% puts the original sound back. The picture keeps playing, and trim still changes which part of the video is kept. The yellow band shows the length and the level, such as 2.0s · 30%. A changed part has to be at least half a second. Quiet parts are blue, loud parts are amber, and silent parts stay red. Click a marked part to put the original sound back. **Parts** lists every changed stretch with its times and percent. Click a row to edit it with the Part volume slider. **Sound back** restores that one stretch. **From** and **To** use the same times as Cut, and **Apply** uses the Part volume slider. A stretch outside the trim is dimmed, because Save keeps only the trimmed picture and its sound. Under the timeline, **−** and **+** zoom from 1× up to 256×, doubling each time, and stop when the timeline would be too wide for the window. Those buttons zoom around the playhead. Ctrl and the mouse wheel zoom toward the pointer. While the video plays, the view stays with the playhead. Rest the pointer on the timeline when you want to look somewhere else. A small movement still moves the playhead. A longer drag selects that span, so zoom in to grab a short part of a long video. The bar scrolls sideways. On a zoomed timeline the wheel scrolls earlier and later, and the arrows beside the zoom buttons move one screen. Left and Right do that too, unless a text box or slider is focused. Drag the playhead or a handle into the edge and the timeline keeps moving. The playhead moves with the pointer, and trim handles can sit half a second apart. Scrolling keeps that place until the playhead is back in view. The playhead, handles, cut marks, and audio marks stay on the real video.

Cut accepts typed times in **From** and **To**. `1:05.4`, `0:12`, `1:02:03`, and a number of seconds such as `90` all work. The span shows as a yellow band. Parts already removed stay red. **Remove** drops the yellow span. **Put back** restores one removed part. Preview skips each removed part with the sound still in step. A removed piece has to be at least half a second, and the video that remains has to be at least half a second.

The volume slider on the bar is only for listening. **Part volume** changes one stretch. **Sound** changes the whole file. Play the video to hear that stretch before you save. Fades are measured after trim, cuts, and speed. The preview shows them. A video with no audio track skips the audio fade on save and the audio track says **No audio**.

**Audio** in the header extracts the sound as MP3 or WAV. You choose the name, then a folder in this app or any folder on this PC. Trim, cuts, audio levels, speed, the saved loudness, and the audio fades are applied. The video file is left where it is. A video with no audio track stops with a clear message.

**Undo** and **Redo** remember speed, sound, audio levels, rotate, fade, crop, trim, and cuts. Ctrl+Z and Ctrl+Y do the same, except while a time box is focused.

**Save** asks for a new file or an overwrite. Overwrite keeps the previous file with your other originals, and bookmarks move onto the new times. A mark inside a removed span is dropped. Save as new names the copy from the changes you made: speed, loudness, `silent`, `level`, rotation, flips, fades, `crop`, `trim`, and `cut`, in that order. A silence uses `silent`. Any other part volume uses `level`. Save as new leaves the original file where it is. A second copy of the same name is `2`, then `3`. Cancel, or leaving with unsaved changes, asks you to stay or leave. Closing the window asks the same question. A save that is still running keeps the window open until it finishes. If an audio part cannot be written, the editor stays open, the bar explains why, and the original file is left where it is.

Save keeps the first caption track. Trim, cut, and speed move the cue times with the picture. Fade, crop, rotate, and changed audio leave the cue times where they are. The captions are written back into the saved MP4. A video with no captions is unchanged. If that caption step cannot finish, the picture is still saved. **SRT** in the header saves that same track as a file, with trim, cut, and speed already applied. It stays hidden when the video has no captions.

## Watch a link

**Open link** on Library plays a direct video address after the check described above. The browser can send a page into the same player.

`extension/` is a Chrome and Edge package. Load that folder unpacked. It is left out of the app build. The toolbar action is **Open in Personal Media Player**.

| Page | What opens |
| --- | --- |
| One direct video | That file. Playback starts at the current time when the stream can seek |
| Several direct videos | A picker, then the video you choose |
| A tab that is itself a video file | That address |
| A page that hides the file, such as YouTube | The page is resolved and played as a stream. The window says **Opening…** |
| The open video or live stream on Dailymotion, Twitch, or Kick | That page is resolved and played. Other videos on the same page stay in the browser for now |
| Nothing the app can play | The action stays off, titled **This page has no direct video file yet.** |

The app keeps one window. A second open uses that window. A page that needs a license shows **This video is protected by a license and stays in the browser.** The extension asks only for the tab address.

On a stream, **Edit**, **Trim**, **Restore original**, and the still-frame button are hidden. **Playlist** appears when that stream was opened from a playlist. The list opens beside the player, and closing it leaves a small card on the video. Opening a video that is not in that playlist hides the list, that card, and the Playlist button together. **Words** stays. The saved-words list stays closed until you open it. Bookmarks appear when the stream was opened from a page. **Save a copy** appears on a YouTube page. Chapters from that page are listed beside the player when the lookup includes them. **Chapters** shows or hides that list without stopping playback, and a page with no chapters hides the button too. **Save a copy** adds that YouTube page to the Download queue, starts the download, and leaves the stream playing. **Add to playlist** saves that same page address in a playlist. A live stream shows **Live** and leaves the timeline and section repeat off. The short buffer is not the length of the video. Another stream that cannot seek keeps play, pause, volume, speed, and full screen, and leaves the timeline and section repeat off. The line under the title says **Streaming**, or **Streaming. This video cannot seek.** Reaching the end and pressing play looks a page up again. A direct file is checked again and starts from the beginning.

Playback settings on a resolved stream can change quality and audio language. Captions for that page start with the tracks found with the stream. Translations are added after the video has started. Download still shows the full list before you add the video. **Settings** can prefer the original audio or one language, and a subtitle language or no subtitles. The extension and a playlist use those choices when the page opens. A missing audio language plays the original track. A missing subtitle language leaves subtitles off. A saved word still opens with the languages stored on that word.

## Download

Paste a YouTube, Shorts, `youtu.be`, or YouTube Music link and press **Look up**. The page lists each quality with its size. An estimate is marked **about**. **Audio only** is included when the clip has a separate audio track. **Add to queue** stays off until the text in the box matches the looked-up link exactly, including capital letters.

A playlist address, or a link that has `list=` and no video id, lists up to 200 episodes. Check the ones to add and pick one quality for all of them: Best available, 1080p, 720p, 480p, 360p, or Audio only. **Add to queue** puts each chosen episode on its own. Captions are chosen on a single video. A normal watch link stays one video, including a watch link that also carries `list=`.

When a single video has subtitles, Look up asks whether to include one. Uploaded captions come first, then YouTube’s automatic captions, including translations, with one English name per language. A translation can take about a minute. **No subtitles** leaves them out. An audio-only download stays without subtitles. The preview and the saved video use the same **CC** control and the same hover meanings as the player.

The queue downloads one item at a time and keeps going after you leave this page. Each row shows the video’s picture when the link has one, then waiting, a download percentage, paused, ready, failed, or cancelled. Audio only still shows that picture. A link with no picture shows a video icon.

| Button | What it does |
| --- | --- |
| Pause | Stops that item and keeps the part already saved |
| Resume | Continues that same download. A kept partial file does not start again until you press this |
| Cancel | Drops the item and deletes its partial file |
| Retry | Puts a failed or cancelled item back in line |
| Preview | Opens a finished file beside the queue. The picture fills that side, and the playback bar sits along the bottom |

**Save** asks for a name and a place. **A folder in this app** puts it in Library, in an album you already have, or in a new album. **A folder on this PC** opens the system save dialog for any folder and any name. Video is an `.mp4`. Audio only is an `.m4a`. **Discard** deletes the preview without saving.

**Save selected**, next to the queue, saves the finished downloads you check. Choose one folder in this app, or one folder on this PC. Each file keeps its download name. When that name is already in the folder on this PC, the app asks before replacing it. **Replace** overwrites those files. **Keep existing files** saves the rest and leaves the matching downloads ready. **Cancel** saves nothing. If a file cannot be saved, it stays ready and the others still save. The message names the files that were saved and the files that failed. **Save** on the preview still saves that one file, and you can rename it. A download that is still waiting, running, paused, or failed has no checkbox.

**History** is the bar under the queue. **Show** opens the saved downloads and **Hide** closes them, so the cards do not stay on screen. Drag the cards sideways to scroll them. The bar at the bottom still works. That choice lasts while the app is open. Each card shows the picture, the file name, the link, the quality, the date, and the folder. **Open** plays it in the app. **Folder** selects it in File Explorer. **Remove** drops the card only. The saved file stays where it is.

Leaving Download does not stop the queue. Pause, resume, cancel, and save stay on this page. A finished file stays ready until you save or discard it. Closing the app pauses the active download and any that are still waiting, and keeps the partial file for **Resume**.

## Merge

**Add videos** takes clips from this PC, or from Library, Videos, Recordings, and the albums you created. Audio-only files are not listed. You can join up to 24 clips. **Edit** on a clip opens that clip in the editor. A trim or fade already set on it is kept. Saving puts that video back in the same spot, and the other clips stay as they are. **Cancel** returns to that same list. If another clip uses the overwritten file, it plays the new video too, and its in and out points are pulled in to fit. **Merge** joins two or more videos.

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

**Include system audio** records the default speakers. **Microphone** records the default microphone. Either switch can be on alone, and both can be on together. Each stays off until you turn it on. Silence at the start of a take is kept, so the picture and the sound stay lined up. When both are on, the microphone is mixed into the speaker track for the whole take, including while the speakers are quiet. That microphone choice stays on this PC.

Stop opens a preview on the same video bar. You can trim the ends before saving. Save copies the recording into the library and into the Recordings album. Discard deletes an unsaved take. Closing the page, or closing the window, with an unsaved take or a recording still running asks you to stay or leave.

## Settings

Settings chooses light, dark, or the Windows theme, shows the library folder, and stores the screenshot cursor and delay. **Online videos** stores the default audio and subtitles for the extension and for playlists. The shortcut list is shown there as a reminder.

**Export backup** saves playlists, including watched marks, saved words, bookmarks, playback positions, favorites, library links, and settings to a file you choose. Videos, photos, albums, recordings, and downloads are not copied. Connected folders are remembered on this PC and stay out of that file. The waiting queue, the open video, and the microphone switch on Recordings stay on this PC too. Files already found are saved with the library links. The backup is written to a temporary file and replaces the chosen file only after that file is complete. **Restore backup** checks each saved record, lists what will be replaced, and waits until you choose **Restore**. Saved data that is not in that file is removed. The copy from before the restore stays in the app folder as `recovery.pmpbackup`. The media files stay where they are. The list of connected folders on this PC stays as it is.
