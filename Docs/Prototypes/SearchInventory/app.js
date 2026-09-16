"use strict";
const $ = (id) => document.getElementById(id);
const definitions = {
  bandage: {
    name: "急救绷带",
    icon: "bandage",
    w: 1,
    h: 1,
    rarity: "common",
    quality: "普通",
    description: "密封防水的基础医疗物资，可用于处理轻微创伤。",
  },
  battery: {
    name: "工业电池",
    icon: "battery",
    w: 1,
    h: 2,
    rarity: "uncommon",
    quality: "优良",
    description: "仍有余电的工业电池，可为基地设备提供能源。",
  },
  scrap: {
    name: "合金零件",
    icon: "scrap",
    w: 1,
    h: 1,
    rarity: "common",
    quality: "普通",
    description: "拆卸下来的通用机械零件，可用于基础制作与维修。",
  },
  pump: {
    name: "水泵部件",
    icon: "pump",
    w: 2,
    h: 2,
    rarity: "rare",
    quality: "稀有",
    description: "保存完整的水泵核心部件，是建设净水设施的重要材料。",
  },
  chip: {
    name: "控制电路",
    icon: "chip",
    w: 2,
    h: 1,
    rarity: "epic",
    quality: "珍贵",
    description: "防水封装的精密控制电路，可用于修复高级设备。",
  },
  tank: {
    name: "备用气瓶",
    icon: "tank",
    w: 1,
    h: 2,
    rarity: "uncommon",
    quality: "优良",
    description: "便于携带的小型密封气瓶，可作为后续行动的氧气储备。",
  },
};
const seedItems = [
  { id: "b1", type: "bandage", owner: "pocket1", x: 0, y: 0, revealed: true },
  { id: "b2", type: "bandage", owner: "bag", x: 1, y: 0, revealed: true },
  { id: "b3", type: "battery", owner: "rig-left", x: 0, y: 0, revealed: true },
  { id: "b4", type: "scrap", owner: "bag", x: 0, y: 1, revealed: true },
  { id: "b5", type: "tank", owner: "bag", x: 4, y: 0, revealed: true },
  { id: "l1", type: "scrap", owner: "loot", x: 0, y: 0, revealed: false },
  { id: "l2", type: "battery", owner: "loot", x: 1, y: 0, revealed: false },
  { id: "l3", type: "pump", owner: "loot", x: 2, y: 0, revealed: false },
  { id: "l4", type: "bandage", owner: "loot", x: 4, y: 0, revealed: false },
  { id: "l5", type: "chip", owner: "loot", x: 4, y: 1, revealed: false },
];
let items = [],
  selectedId = "b1",
  panelOpen = true,
  containerOpen = true;
let searching = false,
  searchStart = 0,
  searchTimer = null,
  toastTimer = null;
let drag = null,
  pointerCandidate = null;
const searchDuration = 1500;
const sprite = (name) =>
  `<svg aria-hidden="true" viewBox="0 0 64 64"><use href="#icon-${name}"/></svg>`;
const dims = (item) => {
  const d = definitions[item.type];
  return item.rotated ? { w: d.h, h: d.w } : { w: d.w, h: d.h };
};
const grid = (owner) => $(owner + "-grid");
const storage = {
  bag: {cols:5,rows:10,label:"背包"},
  "rig-main": {cols:2,rows:2,label:"胸挂主袋"},
  "rig-left": {cols:1,rows:2,label:"胸挂侧袋"},
  "rig-right": {cols:1,rows:2,label:"胸挂侧袋"},
  pocket1: {cols:1,rows:1,label:"口袋"}, pocket2: {cols:1,rows:1,label:"口袋"},
  pocket3: {cols:1,rows:1,label:"口袋"}, pocket4: {cols:1,rows:1,label:"口袋"},
  safe: {cols:3,rows:3,label:"保险箱"}, loot: {cols:6,rows:4,label:"容器"}
};
const playerStores = Object.keys(storage).filter(owner => owner !== "loot");
const columns = owner => storage[owner].cols;
const rows = owner => storage[owner].rows;
const revealedItems = (owner) =>
  items.filter((i) => i.owner === owner && i.revealed);
const remaining = () => items.filter((i) => !i.revealed).sort((a,b) => a.y - b.y || a.x - b.x);

function syncGridSize() {
  if (!panelOpen) return;
  const lootColumn = document.querySelector(".loot-column");
  const style = getComputedStyle(lootColumn);
  const lootWidth = lootColumn.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
  const bagScroll = $("bag-scroll");
  const bagColumn = document.querySelector(".bag-column");
  const bagStyle = getComputedStyle(bagColumn);
  const scrollbarWidth = bagScroll.offsetWidth - bagScroll.clientWidth - 2;
  const bagWidth = bagColumn.clientWidth - parseFloat(bagStyle.paddingLeft) - parseFloat(bagStyle.paddingRight) - scrollbarWidth - 2;
  const cell = Math.floor(Math.min((bagWidth - 58) / 5, lootWidth / 6));
  if (cell > 0) {
    $("inventory").style.setProperty("--shared-cell", `${cell}px`);
    bagScroll.style.width = `${cell * 5 + 58 + scrollbarWidth + 2}px`;
  }
}
function metric(owner) {
  const rect = grid(owner).getBoundingClientRect();
  const cell = rect.width / columns(owner);
  return { rect, cell, step: cell };
}
function positionElement(el, item, owner = item.owner) {
  const { step } = metric(owner),
    { w, h } = dims(item);
  Object.assign(el.style, {
    left: `${item.x * step}px`,
    top: `${item.y * step}px`,
    width: `${w * step}px`,
    height: `${h * step}px`,
  });
}
function fits(item, owner, x, y, rotated = item.rotated) {
  const { w, h } = dims({ ...item, rotated });
  if (x < 0 || y < 0 || x + w > columns(owner) || y + h > rows(owner)) return false;
  return !items.some((other) => {
    if (other.id === item.id || other.owner !== owner) return false;
    const s = dims(other);
    return (
      x < other.x + s.w &&
      x + w > other.x &&
      y < other.y + s.h &&
      y + h > other.y
    );
  });
}
function cardMarkup(item) {
  const d = definitions[item.type],
    s = dims(item);
  return `<span class="item-name">${d.name}</span>${sprite(d.icon)}<span class="item-bottom"><span class="item-rarity">${d.quality}</span><span class="item-size">${s.w}×${s.h}</span></span>`;
}
function createCard(item) {
  const d = definitions[item.type],
    s = dims(item);
  const el = document.createElement("button");
  el.type = "button";
  el.className = `item ${d.rarity}${item.rotated ? " rotated" : ""}${selectedId === item.id ? " selected" : ""}`;
  el.dataset.itemId = item.id;
  el.setAttribute(
    "aria-label",
    `${d.name}，${d.quality}，${s.w}乘${s.h}格，${"在" + storage[item.owner].label}`,
  );
  el.setAttribute("aria-pressed", String(selectedId === item.id));
  el.innerHTML = cardMarkup(item);
  positionElement(el, item);
  el.addEventListener("pointerdown", (e) => {
    if (e.button !== 0 || e.ctrlKey || e.metaKey) return;
    pointerCandidate = {
      id: item.id,
      x: e.clientX,
      y: e.clientY,
      pointerId: e.pointerId,
    };
  });
  el.addEventListener("click", (e) => {
    if (drag) return;
    selectedId = item.id;
    updateSelection();
    if (e.ctrlKey || e.metaKey) quickTransfer(item);
  });
  el.addEventListener("focus", () => {
    if (!drag) {
      selectedId = item.id;
      updateSelection();
    }
  });
  el.addEventListener("mouseenter", () => {
    if (!drag) showDetails(item);
  });
  el.addEventListener("mouseleave", () => {
    if (!drag) showDetails(items.find((i) => i.id === selectedId));
  });
  el.addEventListener("keydown", (e) => {
    if (e.key.toLowerCase() === "r" && !drag) {
      e.preventDefault();
      e.stopPropagation();
      rotateSelected(item);
    }
  });
  return el;
}
function renderGrid(owner, revealId) {
  syncGridSize();
  const el = grid(owner);
  el.replaceChildren();
  for (let n = 0; n < columns(owner) * rows(owner); n++) {
    const cell = document.createElement("div");
    cell.className = "grid-cell";
    el.append(cell);
  }
  if (owner === "loot") {
    const pending = remaining();
    for (const item of pending) {
      const active = searching && item.id === pending[0].id;
      const shadow = document.createElement("div");
      shadow.className = "unknown-item" + (active ? " searching-item" : "");
      shadow.setAttribute("aria-label", active ? "正在搜索的物品" : "未搜索物品");
      positionElement(shadow, item);
      if (active) {
        shadow.innerHTML = `<svg class="item-search-icon" viewBox="0 0 32 32" aria-hidden="true"><circle cx="13" cy="13" r="8"/><path d="m19 19 8 8"/></svg><span class="item-search-percent">00%</span><span class="item-search-track"><i></i></span>`;
      }
      el.append(shadow);
    }
  }
  for (const item of revealedItems(owner)) {
    const card = createCard(item);
    if (item.id === revealId) card.classList.add("reveal");
    el.append(card);
  }
  if (
    !revealedItems(owner).length &&
    (owner === "loot" && !remaining().length)
  ) {
    const message = document.createElement("div");
    message.className = "grid-placeholder";
    message.textContent = owner === "bag" ? "背包为空" : "容器为空";
    el.append(message);
  }
}
function render(revealId) {
  for (const owner of playerStores) renderGrid(owner, revealId);
  renderGrid("loot", revealId);
  const occupied = items
    .filter((i) => i.owner !== "loot")
    .reduce((n, i) => {
      const { w, h } = dims(i);
      return n + w * h;
    }, 0);
  $("capacity-label").textContent = `${occupied} / 71`;
  $("capacity-fill").style.width = `${(occupied / 71) * 100}%`;
  updateSearchStatus();
  updateSelection();
}
function updateSelection() {
  document.querySelectorAll(".item[data-item-id]").forEach((el) => {
    const selected = el.dataset.itemId === selectedId;
    el.classList.toggle("selected", selected);
    el.setAttribute("aria-pressed", String(selected));
  });
  showDetails(items.find((i) => i.id === selectedId));
}
function showDetails(item) {
  if (!item || !item.revealed) {
    $("detail-icon").replaceChildren();
    $("detail-name").textContent = "选择一件物品";
    $("detail-rarity").textContent = "—";
    $("detail-size").textContent = "";
    $("detail-description").textContent =
      "点击物品查看用途，拖拽可调整摆放位置。";
    $("transfer-selected").textContent = "转移物品";
    $("transfer-selected").disabled = true;
    return;
  }
  const d = definitions[item.type],
    { w, h } = dims(item);
  $("detail-icon").innerHTML = sprite(d.icon);
  $("detail-name").textContent = d.name;
  $("detail-rarity").textContent = d.quality;
  $("detail-size").textContent = `${w} × ${h}`;
  $("detail-description").textContent = d.description;
  const button = $("transfer-selected");
  button.dataset.itemId = item.id;
  button.innerHTML =
    item.owner !== "loot"
      ? "移入容器 <span>↗</span>"
      : "放入背包 <span>↙</span>";
  button.disabled = !containerOpen;
}
function toast(message) {
  clearTimeout(toastTimer);
  $("toast").textContent = message;
  $("toast").classList.add("visible");
  toastTimer = setTimeout(() => $("toast").classList.remove("visible"), 2500);
}
function quickTransfer(item) {
  if (!containerOpen) {
    toast("靠近并打开容器后才能转移物品");
    return;
  }
  const to = item.owner !== "loot" ? "loot" : "bag";
  for (let y = 0; y < rows(to); y++)
    for (let x = 0; x < columns(to); x++)
      if (fits(item, to, x, y)) {
        item.owner = to;
        item.x = x;
        item.y = y;
        render();
        toast(
          `${definitions[item.type].name}已${to === "bag" ? "放入背包" : "移入容器"}`,
        );
        return;
      }
  toast("没有足够的连续空间，请先整理或旋转物品");
}
function rotateSelected(item) {
  if (!item) return;
  if (fits(item, item.owner, item.x, item.y, !item.rotated)) {
    item.rotated = !item.rotated;
    render();
  } else toast("旋转后空间不足，请拖到空位再旋转");
}
function updateSearchStatus() {
  const done = !remaining().length,
    found = items.filter((i) => i.id.startsWith("l") && i.revealed).length;
  $("search-label").textContent = done
    ? revealedItems("loot").length
      ? "搜索完成"
      : "容器为空"
    : searching
      ? "正在搜索"
      : "搜索已暂停";
  $("found-count").textContent = `已发现 ${found} 件`;
  $("search-light").classList.toggle("active", searching);
  $("toggle-search").textContent = done
    ? "已搜索全部"
    : searching
      ? "暂停搜索"
      : "继续搜索";
  $("toggle-search").disabled = done;
  $("progress-caption").textContent = done
    ? "全部物资已辨认"
    : searching
      ? "辨认箱内物资"
      : "等待继续搜索";
  $("search-help").textContent = done
    ? "搜索完成，可继续整理和转移物资。"
    : "物品逐件揭示，已发现物品可随时拿取。";
  if (done) {
    $("search-fill").style.width = "100%";
    $("progress-percent").textContent = "100%";
  }
}
function startSearch() {
  if (searching || !remaining().length || !panelOpen || !containerOpen) return;
  searching = true;
  searchStart = performance.now();
  renderGrid("loot");
  updateSearchStatus();
  searchTimer = setInterval(() => {
    const progress = Math.min(
      (performance.now() - searchStart) / searchDuration,
      1,
    );
    const active = document.querySelector(".searching-item");
    if (active) {
      active.querySelector(".item-search-percent").textContent = `${Math.floor(progress * 100)}%`;
      active.querySelector(".item-search-track i").style.width = `${progress * 100}%`;
    }
    $("search-fill").style.width = `${progress * 100}%`;
    $("progress-percent").textContent = `${Math.floor(progress * 100)
      .toString()
      .padStart(2, "0")}%`;
    if (progress >= 1) {
      const next = remaining()[0];
      if (next) {
        next.revealed = true;
        searchStart = performance.now();
        render(next.id);
      }
      if (!remaining().length) {
        searching = false;
        clearInterval(searchTimer);
        searchTimer = null;
        updateSearchStatus();
      }
    }
  }, 50);
}
function pauseSearch() {
  searching = false;
  clearInterval(searchTimer);
  searchTimer = null;
  if (remaining().length) {
    $("search-fill").style.width = "0%";
    $("progress-percent").textContent = "00%";
  }
  renderGrid("loot");
  updateSearchStatus();
}
function setPanel(open, withContainer = true) {
  cancelDrag();
  panelOpen = open;
  containerOpen = withContainer;
  $("inventory").hidden = !open;
  $("closed-state").hidden = open;
  $("no-container").hidden = withContainer;
  document
    .querySelector(".loot-column")
    .classList.toggle("inventory-only", !withContainer);
  $("page-mode").textContent = withContainer ? "容器搜索" : "背包整理";
  if (!open || !withContainer) pauseSearch();
  if (open) {
    if (
      !withContainer &&
      items.find((i) => i.id === selectedId)?.owner === "loot"
    )
      selectedId = items.find((i) => i.owner === "bag")?.id || null;
    render();
    if (withContainer) startSearch();
    $("close-panel").focus({ preventScroll: true });
  } else $("reopen").focus({ preventScroll: true });
}
function reset() {
  cancelDrag();
  clearInterval(searchTimer);
  searchTimer = null;
  searching = false;
  items = seedItems.map((i) => ({ ...i, rotated: false }));
  selectedId = "b1";
  $("search-fill").style.width = "0%";
  $("progress-percent").textContent = "00%";
  setPanel(true, true);
}

// A drag is a preview until a valid drop commits the single ownership change.
function beginDrag(e) {
  const item = items.find((i) => i.id === pointerCandidate.id);
  if (!item) return;
  selectedId = item.id;
  drag = {
    item,
    rotated: item.rotated,
    x: e.clientX,
    y: e.clientY,
    target: null,
  };
  const ghost = $("drag-ghost");
  ghost.hidden = false;
  ghost.replaceChildren(createCard({ ...item, rotated: drag.rotated }));
  updateSelection();
  document
    .querySelector(`[data-item-id="${item.id}"]`)
    ?.classList.add("dragging-source");
  updateDrag(e.clientX, e.clientY);
}
function updateDrag(x, y) {
  if (!drag) return;
  drag.x = x;
  drag.y = y;
  document.querySelectorAll(".drop-preview").forEach((el) => el.remove());
  const size = dims({ ...drag.item, rotated: drag.rotated }),
    { step } = metric(drag.item.owner);
  const ghost = $("drag-ghost");
  Object.assign(ghost.style, {
    left: `${x - step / 2}px`,
    top: `${y - step / 2}px`,
    width: `${size.w * step}px`,
    height: `${size.h * step}px`,
  });
  ghost.firstElementChild.classList.toggle("rotated", drag.rotated);
  ghost.firstElementChild.innerHTML = cardMarkup({
    ...drag.item,
    rotated: drag.rotated,
  });
  drag.target = null;
  for (const owner of containerOpen ? [...playerStores, "loot"] : playerStores) {
    const m = metric(owner);
    const visible = owner !== "loot" ? $("bag-scroll").getBoundingClientRect() : m.rect;
    if (
      x >= visible.left && x < visible.right && y >= visible.top && y < visible.bottom &&
      x >= m.rect.left &&
      x < m.rect.right &&
      y >= m.rect.top &&
      y < m.rect.bottom
    ) {
      const col = Math.floor((x - m.rect.left) / m.step),
        row = Math.floor((y - m.rect.top) / m.step);
      const valid = fits(drag.item, owner, col, row, drag.rotated);
      drag.target = { owner, x: col, y: row, valid };
      const preview = document.createElement("div");
      preview.className = "drop-preview" + (valid ? "" : " invalid");
      positionElement(
        preview,
        { ...drag.item, x: col, y: row, rotated: drag.rotated },
        owner,
      );
      grid(owner).append(preview);
      break;
    }
  }
}
function cancelDrag() {
  drag = null;
  pointerCandidate = null;
  $("drag-ghost").hidden = true;
  $("drag-ghost").replaceChildren();
  document.querySelectorAll(".drop-preview").forEach((el) => el.remove());
  document
    .querySelectorAll(".dragging-source")
    .forEach((el) => el.classList.remove("dragging-source"));
}
document.addEventListener(
  "pointermove",
  (e) => {
    if (
      pointerCandidate &&
      !drag &&
      Math.hypot(
        e.clientX - pointerCandidate.x,
        e.clientY - pointerCandidate.y,
      ) > 5
    )
      beginDrag(e);
    if (drag) {
      e.preventDefault();
      updateDrag(e.clientX, e.clientY);
    }
  },
  { passive: false },
);
document.addEventListener("pointerup", () => {
  if (drag) {
    const { target, item, rotated } = drag;
    if (target?.valid) {
      Object.assign(item, {
        owner: target.owner,
        x: target.x,
        y: target.y,
        rotated,
      });
    } else toast(target ? "此处无法放置，物品已回到原位" : "已取消移动");
    cancelDrag();
    render();
  }
  pointerCandidate = null;
});
document.addEventListener("pointercancel", cancelDrag);
window.addEventListener("blur", () => {
  cancelDrag();
  if (searching) pauseSearch();
});
document.addEventListener("visibilitychange", () => {
  if (document.hidden && searching) pauseSearch();
});
document.addEventListener("keydown", (e) => {
  if (e.repeat) return;
  const key = e.key.toLowerCase();
  if (e.key === "Escape") {
    e.preventDefault();
    if (drag) {
      cancelDrag();
      return;
    }
    if (panelOpen) setPanel(false);
  } else if (e.key === "Tab") {
    e.preventDefault();
    setPanel(!panelOpen, false);
  } else if (key === "e" && (!panelOpen || !containerOpen)) {
    e.preventDefault();
    setPanel(true, true);
  } else if (key === "r" && panelOpen) {
    e.preventDefault();
    if (drag) {
      drag.rotated = !drag.rotated;
      updateDrag(drag.x, drag.y);
    } else rotateSelected(items.find((i) => i.id === selectedId));
  }
});
$("toggle-search").onclick = () => (searching ? pauseSearch() : startSearch());
$("transfer-selected").onclick = () => {
  const item = items.find(
    (i) => i.id === $("transfer-selected").dataset.itemId,
  );
  if (item) {
    selectedId = item.id;
    quickTransfer(item);
  }
};
$("close-panel").onclick = () => setPanel(false);
$("reopen").onclick = () => setPanel(true, true);
$("open-container").onclick = () => setPanel(true, true);
$("open-bag").onclick = () => setPanel(true, false);
$("reset-demo").onclick = () => {
  reset();
  toast("演示已重置，重新搜索维修箱");
};
let lastWidth = 0;
new ResizeObserver((entries) => {
  const width = entries[0].contentRect.width;
  if (panelOpen && Math.abs(width - lastWidth) > 0.5) {
    lastWidth = width;
    cancelDrag();
    render();
  }
}).observe($("inventory"));
reset();

$("bag-scroll").addEventListener("scroll", () => { if (drag) updateDrag(drag.x, drag.y); });
