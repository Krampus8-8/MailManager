import { fillSignupFields, isSignupPage, readProfile } from "./autofill.js";

const input = document.querySelector("#profile");
const status = document.querySelector("#status");
const fill = document.querySelector("#fill");
const clear = document.querySelector("#clear");
document.querySelector("#version").textContent = `Расширение ${chrome.runtime.getManifest().version}`;
const retention = 5 * 60 * 1000;
let pending;
let expiration;
let running = false;
let revision = 0;

async function activeTab() {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    return tab;
}

function reset(message) {
    revision++;
    clearTimeout(expiration);
    pending = undefined;
    input.value = "";
    fill.disabled = true;
    clear.disabled = true;
    status.textContent = message;
}

async function acceptProfile(text) {
    if (!text.trim()) {
        reset("Буфер обмена пуст. Скопируйте данные в Mail Manager и сразу вставьте их сюда.");
        return;
    }
    let profile;
    try {
        profile = readProfile(text.trim());
    } catch {
        reset("Формат не распознан. Используйте кнопку «Копировать для браузера» в Mail Manager.");
        return;
    }

    reset("Данные приняты. Откройте шаг регистрации и нажмите «Заполнить текущий шаг».");
    pending = { profile, tabId: null, deadline: Date.now() + retention };
    expiration = setTimeout(() => reset("Срок истёк. Скопируйте данные ещё раз."), retention);
    input.placeholder = "Данные приняты";
    fill.disabled = running;
    clear.disabled = false;
}

input.addEventListener("input", () => {
    const text = input.value;
    input.value = "";
    if (text) void acceptProfile(text);
});

input.addEventListener("paste", event => {
    const text = event.clipboardData?.getData("text/plain");
    if (text) {
        event.preventDefault();
        void acceptProfile(text);
    }
});

document.querySelector("#paste").addEventListener("click", async () => {
    try {
        await acceptProfile(await navigator.clipboard.readText());
    } catch {
        status.textContent = "Браузер запретил чтение буфера. Нажмите поле и вставьте данные через Ctrl+V.";
    }
});
fill.addEventListener("click", async () => {
    const current = pending;
    if (!current || running) {
        return;
    }
    if (Date.now() >= current.deadline) {
        reset("Срок истёк. Скопируйте данные из Mail Manager ещё раз.");
        return;
    }

    running = true;
    fill.disabled = true;
    try {
        const tab = await activeTab();
        if (pending !== current || Date.now() >= current.deadline) {
            return;
        }
        if (tab?.id == null || (current.tabId !== null && tab.id !== current.tabId) || !isSignupPage(tab.url)) {
            status.textContent = "Вернитесь на ту же вкладку регистрации Gmail.";
            return;
        }

        current.tabId = tab.id;
        const [result] = await chrome.scripting.executeScript({
            target: { tabId: tab.id, frameIds: [0] },
            func: fillSignupFields,
            args: [tab.url, current.profile]
        });
        if (pending === current) {
            status.textContent = result?.result > 0
                ? "Проверьте заполненные поля и продолжите на сайте Google."
                : "На этом шаге нет пустых полей для заполнения. Продолжите вручную.";
        }
    } catch {
        if (pending === current) {
            status.textContent = "Нажмите значок Mail Manager на вкладке Gmail и повторите. Поля также можно скопировать по одному.";
        }
    } finally {
        running = false;
        fill.disabled = !pending;
    }
});

clear.addEventListener("click", () => reset("Данные удалены из панели."));
chrome.tabs.onRemoved.addListener(tabId => {
    if (pending?.tabId === tabId) {
        reset("Вкладка регистрации закрыта. Скопируйте данные заново для новой вкладки.");
    }
});
