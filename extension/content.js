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

function listVideos() {
  const files = [];
  const blobs = [];
  walk(document, files, blobs);
  const items = files.map((video, index) => ({
    kind: "file",
    src: video.currentSrc,
    label: labelFor(video, index + 1),
    currentTime: timeOf(video)
  }));

  if (blobs.length > 0) {
    const active = activeBlob(blobs);
    items.push({
      kind: "page",
      src: pageAddress(),
      label: pageLabel(),
      currentTime: timeOf(active)
    });
  }

  for (const embed of listEmbeds()) {
    items.push(embed);
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
  return typeof time === "number" && isFinite(time) && time > 0 ? time : 0;
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
    if (!isEmbed(src) || pageKey(src) === here || seen.has(pageKey(src))) {
      continue;
    }

    if (frame.offsetWidth > 0 && frame.offsetHeight > 0 && (frame.offsetWidth < 200 || frame.offsetHeight < 120)) {
      continue;
    }

    seen.add(pageKey(src));
    const title = (frame.getAttribute("title") || "").replace(/\s+/g, " ").trim();
    found.push({
      kind: "page",
      src,
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
