import { fillSignupFields, isSignupPage, readProfile } from "./autofill.js";

const input = document.querySelector("#profile");
const status = document.querySelector("#status");
const fill = document.querySelector("#fill");
const clear = document.querySelector("#clear");
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

input.addEventListener("input", () => {
    input.value = "";
});

input.addEventListener("paste", async event => {
    event.preventDefault();
    const text = event.clipboardData?.getData("text/plain") ?? "";
    reset("Проверяем вкладку регистрации…");
    const request = revision;
    try {
        const profile = readProfile(text);
        const tab = await activeTab();
        if (request !== revision) {
            return;
        }
        if (tab?.id == null || !isSignupPage(tab.url)) {
            status.textContent = "Откройте регистрацию Gmail и нажмите значок Mail Manager на этой вкладке.";
            return;
        }

        pending = { tabId: tab.id, profile, deadline: Date.now() + retention };
        expiration = setTimeout(() => reset("Срок истёк. Скопируйте данные из Mail Manager ещё раз."), retention);
        status.textContent = "Данные готовы. Нажмите «Заполнить текущий шаг».";
        fill.disabled = running;
        clear.disabled = false;
    } catch {
        if (request === revision) {
            status.textContent = "Скопируйте данные кнопкой «Копировать для браузера» в Mail Manager.";
        }
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
        if (tab?.id !== current.tabId || !isSignupPage(tab.url)) {
            status.textContent = "Вернитесь на ту же вкладку регистрации Gmail.";
            return;
        }

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
