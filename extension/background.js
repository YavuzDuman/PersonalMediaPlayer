importScripts("stream-page.js");

const ACTION_TITLE = "Open in Personal Media Player";
const NO_VIDEO_TITLE = "This page has no direct video file yet.";

function isWebAddress(url) {
  return /^https?:\/\//i.test(url || "");
}

function pageKey(url) {
  try {
    const parsed = new URL(url);
    return parsed.origin + parsed.pathname + parsed.search;
  } catch (error) {
    return url || "";
  }
}

function promise(result) {
  if (result && typeof result.then === "function") {
    return result.then(() => {}, () => {});
  }

  return Promise.resolve();
}

// mode is pending, direct, videos, or empty. videos holds the latest list.
const pageState = new Map();
const refreshGeneration = new Map();

function showPlain(tabId) {
  return Promise.all([
    promise(chrome.action.disable(tabId)),
    promise(chrome.action.setTitle({ tabId, title: ACTION_TITLE })),
    promise(chrome.action.setPopup({ tabId, popup: "" }))
  ]).catch(() => {});
}

function showPending(tabId, key) {
  pageState.set(tabId, { key, mode: "pending", videos: [] });
  return showPlain(tabId);
}

function showDirect(tabId, key) {
  pageState.set(tabId, { key, mode: "direct", videos: [] });
  return Promise.all([
    promise(chrome.action.enable(tabId)),
    promise(chrome.action.setTitle({ tabId, title: ACTION_TITLE })),
    promise(chrome.action.setPopup({ tabId, popup: "" }))
  ]).catch(() => {});
}

function listedVideos(videos) {
  if (!Array.isArray(videos)) {
    return [];
  }

  return videos
    .filter((video) => video && isWebAddress(video.src) && typeof video.label === "string")
    .map((video) => ({
      src: video.src,
      label: video.label,
      currentTime: video.currentTime,
      id: video.id,
      kind: video.kind === "page" ? "page" : "file"
    }));
}

function showList(tabId, key, videos) {
  const list = listedVideos(videos);
  pageState.set(tabId, { key, mode: list.length === 0 ? "empty" : "videos", videos: list });
  const title = list.length === 0 ? NO_VIDEO_TITLE : ACTION_TITLE;
  const popup = list.length > 1 ? "picker.html" : "";
  const toggle = list.length === 0 ? chrome.action.disable(tabId) : chrome.action.enable(tabId);
  return Promise.all([
    promise(toggle),
    promise(chrome.action.setTitle({ tabId, title })),
    promise(chrome.action.setPopup({ tabId, popup }))
  ]).catch(() => {});
}

function askVideos(tabId) {
  return new Promise((resolve) => {
    try {
      chrome.tabs.sendMessage(tabId, { type: "list" }, (response) => {
        if (chrome.runtime.lastError || !response || !Array.isArray(response.videos)) {
          resolve(null);
          return;
        }

        resolve(listedVideos(response.videos));
      });
    } catch (error) {
      resolve(null);
    }
  });
}

function refreshTab(tabId, url, settled) {
  if (tabId === undefined) {
    return Promise.resolve();
  }

  if (!isWebAddress(url)) {
    pageState.delete(tabId);
    return showPlain(tabId);
  }

  const generation = (refreshGeneration.get(tabId) || 0) + 1;
  refreshGeneration.set(tabId, generation);
  const key = pageKey(url);
  return askVideos(tabId).then((videos) => {
    if (refreshGeneration.get(tabId) !== generation) {
      return;
    }

    if (videos === null) {
      const known = pageState.get(tabId);
      if (!settled && known && known.key === key && known.mode !== "pending") {
        return;
      }

      const stream = canonicalStream(url);
      if (stream && stream.specific) {
        return showList(tabId, key, [{
          kind: "page",
          src: stream.url,
          label: "This page",
          currentTime: 0,
          id: 0
        }]);
      }

      // Chrome does not run the content script on a video file, so that tab address is the media.
      // An HTML watch page is not a video file. Sending it without a lookup says it is not a video.
      return directMedia(url) && settled ? showDirect(tabId, key) : (settled ? showPlain(tabId) : showPending(tabId, key));
    }

    return showList(tabId, key, videos);
  });
}

function noteVideos(tabId, url, videos) {
  const generation = (refreshGeneration.get(tabId) || 0) + 1;
  refreshGeneration.set(tabId, generation);
  return showList(tabId, pageKey(url), videos);
}

function refreshTabs() {
  chrome.action.disable();
  chrome.tabs.query({}, (tabs) => {
    for (const tab of tabs) {
      if (tab.id !== undefined) {
        void refreshTab(tab.id, tab.url, true);
      }
    }
  });
}

chrome.runtime.onInstalled.addListener(refreshTabs);
chrome.runtime.onStartup.addListener(refreshTabs);

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (!changeInfo.url && changeInfo.status !== "complete") {
    return;
  }

  const url = (tab && tab.url) || changeInfo.url || "";
  void refreshTab(tabId, url, changeInfo.status === "complete");
});

chrome.tabs.onActivated.addListener((activeInfo) => {
  chrome.tabs.get(activeInfo.tabId, (tab) => {
    if (chrome.runtime.lastError || !tab || tab.id === undefined) {
      return;
    }

    void refreshTab(tab.id, tab.url, true);
  });
});

chrome.runtime.onMessage.addListener((message, sender) => {
  if (!message) {
    return;
  }

  if (message.type === "videos") {
    const tab = sender && sender.tab;
    if (!tab || tab.id === undefined || !isWebAddress(tab.url)) {
      return;
    }

    if (message.page && pageKey(message.page) !== pageKey(tab.url)) {
      return;
    }

    void noteVideos(tab.id, tab.url, message.videos);
    return;
  }

  if (message.type !== "open" || !fromPicker(sender)) {
    return;
  }

  const tabId = message.tabId;
  if (!tabId || !isWebAddress(message.mediaUrl)) {
    return;
  }

  chrome.tabs.get(tabId, (tab) => {
    if (chrome.runtime.lastError || !tab || tab.id === undefined || !isWebAddress(tab.url)) {
      return;
    }

    const resolve = message.kind === "page";
    openTarget(tab.id, protocolUrl(message.mediaUrl, resolve ? null : tab.url, message.currentTime, resolve));
  });
});

function fromPicker(sender) {
  return !!sender && !sender.tab && typeof sender.url === "string" && sender.url.includes("/picker.html");
}

function protocolUrl(mediaUrl, referrer, startSeconds, resolve) {
  let target = "personalmediaplayer://play/?url=" + encodeURIComponent(mediaUrl);
  if (!resolve && isWebAddress(referrer)) {
    target += "&referrer=" + encodeURIComponent(referrer);
  }

  const seconds = Number(startSeconds);
  if (Number.isFinite(seconds) && seconds > 0 && seconds <= 7 * 24 * 60 * 60) {
    target += "&t=" + encodeURIComponent((Math.round(seconds * 1000) / 1000).toFixed(3));
  }

  if (resolve) {
    target += "&resolve=1";
  }

  return target;
}

// The browser question is a dialog owned by the handoff tab. Closing that tab
// dismisses the question. Chrome closes the tab itself after Open is chosen.
const handoffReturn = new Map();

chrome.tabs.onRemoved.addListener((tabId) => {
  pageState.delete(tabId);
  refreshGeneration.delete(tabId);
  const returnTo = handoffReturn.get(tabId);
  if (returnTo === undefined) {
    return;
  }

  handoffReturn.delete(tabId);
  chrome.tabs.update(returnTo, { active: true }, () => {
    void chrome.runtime.lastError;
  });
});

function openTarget(returnTo, target) {
  chrome.tabs.create({ url: target, active: true, openerTabId: returnTo }, (created) => {
    if (chrome.runtime.lastError || !created || created.id === undefined) {
      return;
    }

    handoffReturn.set(created.id, returnTo);
  });
}

chrome.action.onClicked.addListener((tab) => {
  if (!tab.id || !isWebAddress(tab.url)) {
    return;
  }

  const tabId = tab.id;
  const pageUrl = tab.url;
  chrome.tabs.sendMessage(tabId, { type: "list" }, (response) => {
    if (chrome.runtime.lastError || !response || !Array.isArray(response.videos)) {
      const stream = canonicalStream(pageUrl);
      if (stream) {
        openTarget(tabId, protocolUrl(stream.url, null, 0, true));
        return;
      }

      if (directMedia(pageUrl)) {
        openTarget(tabId, protocolUrl(pageUrl));
      }
      return;
    }

    const videos = listedVideos(response.videos);
    if (videos.length === 0) {
      void showList(tabId, pageKey(pageUrl), []);
      return;
    }

    if (videos.length > 1) {
      void showList(tabId, pageKey(pageUrl), videos);
      return;
    }

    const chosen = videos[0];
    const resolve = chosen.kind === "page";
    openTarget(tabId, protocolUrl(chosen.src, resolve ? null : pageUrl, chosen.currentTime, resolve));
  });
});
