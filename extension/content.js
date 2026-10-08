const EMBED_HOSTS = new Set([
  "youtube.com",
  "m.youtube.com",
  "music.youtube.com",
  "youtube-nocookie.com",
  "youtu.be",
  "vimeo.com",
  "player.vimeo.com",
  "dailymotion.com",
  "dai.ly"
]);

function hasMainPlayer(videos, specific) {
  return videos.some((video) => isMainPlayer(video, specific));
}

function isMainPlayer(video, specific) {
  const width = video.offsetWidth || 0;
  const height = video.offsetHeight || 0;
  if (width >= 200 && height >= 120) {
    return true;
  }

  if (specific) {
    return true;
  }

  if (video.videoWidth >= 200) {
    return true;
  }

  return !video.paused && video.currentTime > 0;
}

function hasOpenStreamFrame(pageUrl) {
  const frames = [];
  collectFrames(document, frames);
  for (const frame of frames) {
    const target = canonicalStream(frame.src || "");
    if (!target) {
      continue;
    }

    const width = frame.offsetWidth || 0;
    const height = frame.offsetHeight || 0;
    if ((width >= 200 && height >= 120) || pageKey(target.url) === pageKey(pageUrl)) {
      return true;
    }
  }

  return false;
}

function embedTarget(url) {
  const stream = canonicalStream(url);
  if (stream) {
    return stream.url;
  }

  return isEmbed(url) ? url : null;
}

function isPlaceholder(video) {
  const src = (video && (video.currentSrc || video.src)) || "";
  return /black_2s\.mp4(?:$|[?#])/i.test(src) || /\/ads\/black/i.test(src);
}

function clipAddress(value) {
  const stream = canonicalStream(value);
  return stream && /\/clips\//i.test(stream.url) ? stream.url : null;
}

function selectedClip() {
  if (!document.querySelector) {
    return null;
  }

  const marked = document.querySelector("a[aria-current='page'][href*='/clips/clip_'], a[aria-selected='true'][href*='/clips/clip_']");
  return marked ? clipAddress(marked.href) : null;
}

function clipNearPlayer(videos) {
  for (const video of videos) {
    if (!isMainPlayer(video, true) || typeof video.closest !== "function") {
      continue;
    }

    const link = video.closest("a[href*='/clips/clip_']");
    const clip = link ? clipAddress(link.href) : null;
    if (clip) {
      return clip;
    }
  }

  return null;
}

function kickPageChannel() {
  let url;
  try {
    url = new URL(location.href);
  } catch (error) {
    return null;
  }

  const host = bareHost(url.hostname);
  if (host !== "kick.com" && host !== "player.kick.com") {
    return null;
  }

  const channel = segments(url.pathname)[0];
  return kickName(channel) ? channel : null;
}

// The clips grid does not change the address when a clip opens. Remember the card
// that was clicked, because the dialog itself does not contain the clip id.
let openedClipId = null;

function rememberClipClick(event) {
  const id = clipIdAround(event && event.target);
  if (id) {
    openedClipId = id;
  }
}

function clipIdAround(node) {
  while (node && node !== document) {
    if (typeof node.querySelectorAll === "function") {
      const links = node.querySelectorAll("a[href*='/clips/clip_']");
      if (links.length === 1) {
        const link = links[0];
        const href = link.href || (link.getAttribute && link.getAttribute("href")) || "";
        const clip = clipAddress(absolutePageUrl(href));
        const id = clip && clip.match(/\/(clip_[A-Za-z0-9]+)(?:$|[/?#])/i);
        return id ? id[1] : null;
      }
    }

    node = node.parentElement || null;
  }

  return null;
}

function absolutePageUrl(href) {
  try {
    return new URL(href, location.href).href;
  } catch (error) {
    return "";
  }
}

// A clip opened on the clips page stays on that page. The player is a dialog with a
// blob video, and the clip id is the playback request, not the page address.
function clipOpenedInPage() {
  if (!document.querySelectorAll || !kickPageChannel()) {
    return null;
  }

  const dialogs = document.querySelectorAll("[role='dialog'][data-state='open']");
  let playing = false;
  for (const dialog of dialogs) {
    if (!dialog.querySelectorAll) {
      continue;
    }

    for (const video of dialog.querySelectorAll("video")) {
      if (isPlaceholder(video)) {
        continue;
      }

      const width = video.offsetWidth || 0;
      const height = video.offsetHeight || 0;
      if ((width >= 200 && height >= 120) || (!video.paused && video.currentTime > 0 && width >= 200)) {
        playing = true;
      }
    }
  }

  if (!playing) {
    return null;
  }

  const channel = kickPageChannel();
  let id = null;
  if (typeof performance !== "undefined" && performance.getEntriesByType) {
    const entries = performance.getEntriesByType("resource");
    for (let index = 0; index < entries.length; index++) {
      const name = entries[index] && entries[index].name || "";
      const api = name.match(/\/api\/v1\/clips\/(clip_[A-Za-z0-9]+)/i);
      const media = name.match(/\/(clip_[A-Za-z0-9]+)\/playlist\.m3u8/i);
      const found = (api && api[1]) || (media && media[1]);
      if (found) {
        id = found;
      }
    }
  }

  // The dialog's playback request names the open clip. A content script sometimes
  // cannot see that request, so the card clicked to open the dialog is the fallback.
  if (!id) {
    id = openedClipId;
  }

  return id ? "https://kick.com/" + channel + "/clips/" + id : null;
}

function listVideos() {
  const files = [];
  const blobs = [];
  walk(document, files, blobs);
  const stream = canonicalStream(location.href);
  const players = files.concat(blobs).filter((video) => !isPlaceholder(video));
  // A watch, video, or clip address is the open item. The player on these sites often
  // lives in a frame this script cannot read, or behind an ad placeholder.
  const openHere = stream && (stream.specific || hasMainPlayer(players, stream.specific) || hasOpenStreamFrame(stream.url));
  const items = [];

  if (openHere) {
    const openPlayers = players.filter((video) => isMainPlayer(video, stream.specific));
    const active = openPlayers.length > 0 ? activeBlob(openPlayers) : null;
    items.push({
      kind: "page",
      src: stream.url,
      label: pageLabel(),
      currentTime: stream.specific ? timeOf(active) : 0
    });
  } else if (!stream) {
    const clip = clipOpenedInPage() || clipNearPlayer(players) || selectedClip();
    if (clip) {
      items.push({
        kind: "page",
        src: clip,
        label: pageLabel(),
        currentTime: 0
      });
    } else if (!knownStreamHost(location.href)) {
      const visibleFiles = files.filter((video) => !isPlaceholder(video));
      for (let index = 0; index < visibleFiles.length; index++) {
        const video = visibleFiles[index];
        items.push({
          kind: "file",
          src: video.currentSrc,
          label: labelFor(video, index + 1),
          currentTime: timeOf(video)
        });
      }

      if (blobs.length > 0) {
        const active = activeBlob(blobs);
        items.push({
          kind: "page",
          src: pageAddress(),
          label: pageLabel(),
          currentTime: timeOf(active)
        });
      }
    }

    if (!clip) {
      for (const embed of listEmbeds()) {
        items.push(embed);
      }
    }
  }

  return withUniqueLabels(items).map((video, index) => ({
    kind: video.kind === "page" ? "page" : "file",
    src: video.src,
    label: video.label,
    currentTime: video.currentTime,
    id: index
  }));
}

function walk(root, files, blobs) {
  if (!root || typeof root.querySelectorAll !== "function") {
    return;
  }

  watchRoot(root);
  for (const video of root.querySelectorAll("video")) {
    const src = video.currentSrc || "";
    if (/^https?:\/\//i.test(src)) {
      files.push(video);
    } else if (/^blob:/i.test(src) || video.srcObject) {
      blobs.push(video);
    }
  }

  for (const element of root.querySelectorAll("*")) {
    if (element.shadowRoot) {
      walk(element.shadowRoot, files, blobs);
    }
  }
}

const watched = new WeakSet();

function watchRoot(root) {
  if (!root || root === document || root === document.documentElement || watched.has(root)) {
    return;
  }

  watched.add(root);
  const observer = new MutationObserver(schedule);
  observer.observe(root, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ["src", "title"]
  });
}

function timeOf(video) {
  const time = video && video.currentTime;
  if (typeof time !== "number" || !isFinite(time) || time <= 0) {
    return 0;
  }

  // A live player reports an endless length. That clock is not a position in the saved video.
  if (video.duration === Infinity) {
    return 0;
  }

  return time;
}

function activeBlob(videos) {
  let best = videos[0];
  for (const video of videos) {
    if (!video.paused && video.currentTime > 0) {
      return video;
    }

    if (video.currentTime > best.currentTime) {
      best = video;
    }
  }

  return best;
}

function pageAddress() {
  const url = new URL(location.href);
  url.hash = "";
  return url.href;
}

function pageLabel() {
  const title = (document.title || "").replace(/\s+/g, " ").trim();
  return title ? clip(title) : "This page";
}

function listEmbeds() {
  const found = [];
  const seen = new Set();
  const frames = [];
  collectFrames(document, frames);
  const here = pageKey(location.href);
  for (const frame of frames) {
    const src = frame.src || "";
    const target = embedTarget(src);
    if (!target || pageKey(target) === here || seen.has(pageKey(target))) {
      continue;
    }

    if (frame.offsetWidth > 0 && frame.offsetHeight > 0 && (frame.offsetWidth < 200 || frame.offsetHeight < 120)) {
      continue;
    }

    seen.add(pageKey(target));
    const title = (frame.getAttribute("title") || "").replace(/\s+/g, " ").trim();
    found.push({
      kind: "page",
      src: target,
      label: title ? clip(title) : "Embedded video",
      currentTime: 0
    });
  }

  return found;
}

function collectFrames(root, frames) {
  if (!root || typeof root.querySelectorAll !== "function") {
    return;
  }

  for (const frame of root.querySelectorAll("iframe")) {
    frames.push(frame);
  }

  for (const element of root.querySelectorAll("*")) {
    if (element.shadowRoot) {
      collectFrames(element.shadowRoot, frames);
    }
  }
}

function isEmbed(url) {
  let parsed;
  try {
    parsed = new URL(url);
  } catch (error) {
    return false;
  }

  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    return false;
  }

  const host = parsed.hostname.toLowerCase();
  const bare = host.startsWith("www.") ? host.slice(4) : host;
  if (EMBED_HOSTS.has(host) || EMBED_HOSTS.has(bare) || bare.endsWith(".youtube.com") || bare.endsWith(".youtube-nocookie.com") || bare.endsWith(".vimeo.com") || bare.endsWith(".dailymotion.com")) {
    return true;
  }

  const path = parsed.pathname.toLowerCase();
  return path.includes("/embed/") || path.includes("/player/");
}

function pageKey(url) {
  try {
    const parsed = new URL(url);
    return parsed.origin + parsed.pathname + parsed.search;
  } catch (error) {
    return url || "";
  }
}

function labelFor(video, index) {
  const title = (video.getAttribute("title") || video.getAttribute("aria-label") || "").replace(/\s+/g, " ").trim();
  if (title) {
    return clip(title);
  }

  try {
    const path = new URL(video.currentSrc).pathname.split("/").filter(Boolean).pop() || "";
    const name = decodeURIComponent(path).trim();
    if (name && /[0-9A-Za-z]/.test(name) && name.length <= 60) {
      return name;
    }
  } catch (error) {
    // A nameless address still gets a numbered label.
  }

  return "Video " + index;
}

function clip(text) {
  return text.length <= 80 ? text : text.slice(0, 79) + "…";
}

function withUniqueLabels(videos) {
  const counts = new Map();
  for (const video of videos) {
    counts.set(video.label, (counts.get(video.label) || 0) + 1);
  }

  const seen = new Map();
  return videos.map((video) => {
    if (counts.get(video.label) < 2) {
      return video;
    }

    const number = (seen.get(video.label) || 0) + 1;
    seen.set(video.label, number);
    return { ...video, label: clip(video.label + " (" + number + ")") };
  });
}

let lastKey = null;

function publish() {
  let videos;
  try {
    videos = listVideos();
  } catch (error) {
    return;
  }

  const key = videos.map((video) => video.kind + "\n" + video.src + "\n" + video.label).join("\n");
  if (key === lastKey) {
    return;
  }

  lastKey = key;
  try {
    const sent = chrome.runtime.sendMessage({ type: "videos", page: location.href, videos });
    if (sent && typeof sent.catch === "function") {
      sent.catch(() => {});
    }
  } catch (error) {
    // The extension was reloaded. This page keeps the old script until it is refreshed.
  }
}

let scheduled = 0;

function schedule() {
  if (scheduled) {
    return;
  }

  scheduled = setTimeout(() => {
    scheduled = 0;
    publish();
  }, 200);
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (!message || message.type !== "list") {
    return;
  }

  sendResponse({ videos: listVideos() });
});

publish();
let lastHref = location.href;
const addressWatch = setInterval(() => {
  if (location.href === lastHref) {
    return;
  }

  lastHref = location.href;
  schedule();
}, 400);
if (addressWatch && typeof addressWatch.unref === "function") {
  addressWatch.unref();
}
document.addEventListener("click", rememberClipClick, true);
document.addEventListener("loadedmetadata", schedule, true);
document.addEventListener("loadstart", schedule, true);
document.addEventListener("emptied", schedule, true);
window.addEventListener("pageshow", publish);

if (document.documentElement) {
  const observer = new MutationObserver(schedule);
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ["src", "title"]
  });
}
