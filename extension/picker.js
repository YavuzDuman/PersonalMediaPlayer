const NO_VIDEO = "This page has no direct video file yet.";
const list = document.getElementById("list");

function showEmpty(message) {
  list.replaceChildren();
  const note = document.createElement("p");
  note.id = "empty";
  note.textContent = message;
  list.appendChild(note);
}

function showVideos(tab, videos) {
  list.replaceChildren();
  for (const video of videos) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = video.label || video.src;
    button.title = video.label || video.src;
    button.addEventListener("click", () => choose(tab, video));
    list.appendChild(button);
  }

  const first = list.querySelector("button");
  if (first) {
    first.focus();
  }
}

function choose(tab, video) {
  chrome.tabs.sendMessage(tab.id, { type: "list" }, (response) => {
    let picked = video;
    if (!chrome.runtime.lastError && response && Array.isArray(response.videos)) {
      const match = response.videos.find((item) => item && item.id === video.id && item.src === video.src)
        || response.videos.find((item) => item && item.src === video.src);
      if (!match) {
        showEmpty(NO_VIDEO);
        return;
      }

      picked = match;
    }

    chrome.runtime.sendMessage({
      type: "open",
      tabId: tab.id,
      mediaUrl: picked.src,
      currentTime: picked.currentTime,
      kind: picked.kind
    }, () => {
      window.close();
    });
  });
}

chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
  const tab = tabs && tabs[0];
  if (chrome.runtime.lastError || !tab || tab.id === undefined) {
    showEmpty(NO_VIDEO);
    return;
  }

  chrome.tabs.sendMessage(tab.id, { type: "list" }, (response) => {
    if (chrome.runtime.lastError || !response || !Array.isArray(response.videos) || response.videos.length === 0) {
      showEmpty(NO_VIDEO);
      return;
    }

    showVideos(tab, response.videos);
  });
});
