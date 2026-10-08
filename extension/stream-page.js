// Routes that are not a channel. A channel name can otherwise look like one of these pages.
const TWITCH_RESERVED = new Set([
  "about", "activate", "admin", "affiliates", "annual-pass", "api", "app", "bits", "blog",
  "brand", "broadcast", "browse", "clips", "collection", "collections", "communities",
  "community", "content", "creatorcamp", "dashboard", "directory", "download", "downloads",
  "drops", "embed", "event", "events", "extensions", "following", "friends", "gift", "help",
  "home", "inventory", "jobs", "legal", "login", "messages", "mobile", "moderation", "p",
  "partners", "popout", "prime", "privacy", "products", "profile", "safety", "search",
  "security", "settings", "signup", "store", "subs", "subscriptions", "support", "team",
  "turbo", "user", "videos", "wallet"
]);

const KICK_RESERVED = new Set([
  "about", "auth", "blog", "browse", "categories", "category", "community-guidelines",
  "dashboard", "dmca", "following", "home", "privacy", "privacy-policy", "search",
  "settings", "terms", "terms-of-service", "transactions", "video"
]);

function canonicalStream(value) {
  let url;
  try {
    url = new URL(value);
  } catch (error) {
    return null;
  }

  if (url.protocol !== "http:" && url.protocol !== "https:") {
    return null;
  }

  const host = bareHost(url.hostname);
  return dailymotionStream(host, url) || twitchStream(host, url) || kickStream(host, url);
}

function bareHost(hostname) {
  const host = (hostname || "").toLowerCase();
  return host.startsWith("www.") ? host.slice(4) : host;
}

function knownStreamHost(value) {
  let url;
  try {
    url = new URL(value);
  } catch (error) {
    return false;
  }

  const host = bareHost(url.hostname);
  return host === "dai.ly"
    || host === "dailymotion.com"
    || host.endsWith(".dailymotion.com")
    || host === "twitch.tv"
    || host === "m.twitch.tv"
    || host === "go.twitch.tv"
    || host === "player.twitch.tv"
    || host === "clips.twitch.tv"
    || host === "kick.com"
    || host === "player.kick.com";
}

function directMedia(value) {
  let url;
  try {
    url = new URL(value);
  } catch (error) {
    return false;
  }

  return /\.(mp4|webm|mkv|mov|m4v|m3u8|m3u)$/i.test(url.pathname);
}

function streamResult(url, specific) {
  return { url: url, specific: specific };
}

function dailymotionStream(host, url) {
  if (host === "dai.ly") {
    return dailymotionVideo(firstSegment(url.pathname));
  }

  if (host !== "dailymotion.com" && !host.endsWith(".dailymotion.com")) {
    return null;
  }

  const video = url.pathname.match(/\/(?:embed\/)?video\/([^/?#]+)/i);
  if (video) {
    return dailymotionVideo(video[1]);
  }

  if (/\/player(?:\/[\da-z]+)?\.html$/i.test(url.pathname)) {
    return dailymotionVideo(url.searchParams.get("video"));
  }

  return null;
}

function dailymotionVideo(raw) {
  if (!raw) {
    return null;
  }

  const id = raw.split("_")[0];
  if (!/^x[a-z0-9]+$/i.test(id)) {
    return null;
  }

  return streamResult("https://www.dailymotion.com/video/" + id, true);
}

function twitchStream(host, url) {
  if (host === "player.twitch.tv") {
    const video = (url.searchParams.get("video") || "").replace(/^v/i, "");
    if (/^\d+$/.test(video)) {
      return streamResult("https://www.twitch.tv/videos/" + video, true);
    }

    return twitchChannel(url.searchParams.get("channel"));
  }

  if (host === "clips.twitch.tv") {
    const query = url.searchParams.get("clip");
    if (query) {
      return twitchClip(query);
    }

    const parts = segments(url.pathname);
    if (parts.length === 0 || parts[0].toLowerCase() === "embed") {
      return null;
    }

    return twitchClip(parts[parts.length - 1]);
  }

  if (host !== "twitch.tv" && host !== "m.twitch.tv" && host !== "go.twitch.tv") {
    return null;
  }

  const parts = segments(url.pathname);
  if (parts.length === 0) {
    return null;
  }

  if (parts[0].toLowerCase() === "videos" && /^\d+$/.test(parts[1] || "")) {
    return streamResult("https://www.twitch.tv/videos/" + parts[1], true);
  }

  if (parts.length >= 3 && /^v(?:ideo)?$/i.test(parts[1]) && /^\d+$/.test(parts[2])) {
    return streamResult("https://www.twitch.tv/videos/" + parts[2], true);
  }

  if (parts.length === 2 && parts[0].toLowerCase() === "clip") {
    return twitchClip(parts[1]);
  }

  if (parts.length === 3 && parts[1].toLowerCase() === "clip") {
    return twitchClip(parts[2]);
  }

  if (parts.length === 2 && parts[0].toLowerCase() === "popout") {
    return twitchChannel(parts[1]);
  }

  if (parts.length === 1 || (parts.length === 2 && parts[1].toLowerCase() === "home")) {
    return twitchChannel(parts[0]);
  }

  return null;
}

function twitchChannel(name) {
  if (!name || !/^[A-Za-z0-9_]{1,25}$/.test(name) || TWITCH_RESERVED.has(name.toLowerCase())) {
    return null;
  }

  return streamResult("https://www.twitch.tv/" + name, false);
}

function twitchClip(slug) {
  if (!slug || /[/?#&]/.test(slug)) {
    return null;
  }

  return streamResult("https://www.twitch.tv/clip/" + slug, true);
}

function kickStream(host, url) {
  const player = host === "player.kick.com";
  if (!player && host !== "kick.com") {
    return null;
  }

  const parts = segments(url.pathname);
  if (parts.length === 0 || !kickName(parts[0])) {
    return null;
  }

  const channel = parts[0];
  if (parts.length >= 3 && parts[1].toLowerCase() === "videos" && /^[\w-]+$/.test(parts[2])) {
    return streamResult("https://kick.com/" + channel + "/videos/" + parts[2], true);
  }

  if (parts.length >= 3 && parts[1].toLowerCase() === "clips") {
    return kickClip(channel, parts[2]);
  }

  const queryClip = url.searchParams.get("clip");
  if (queryClip && parts.length === 1) {
    return kickClip(channel, queryClip);
  }

  if (parts.length === 1) {
    return streamResult("https://kick.com/" + channel, false);
  }

  return null;
}

function kickName(name) {
  return /^[\w-]+$/.test(name || "") && !KICK_RESERVED.has(name.toLowerCase());
}

function kickClip(channel, id) {
  if (!/^clip_[\w-]+$/i.test(id || "")) {
    return null;
  }

  return streamResult("https://kick.com/" + channel + "/clips/" + id, true);
}

function segments(pathname) {
  return (pathname || "").split("/").filter(Boolean);
}

function firstSegment(pathname) {
  return segments(pathname)[0] || "";
}
