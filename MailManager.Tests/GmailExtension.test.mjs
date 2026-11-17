import assert from "node:assert/strict";
import test from "node:test";
import { fillSignupFields, isSignupPage, readProfile } from "../GmailExtension/autofill.js";

const firstStep = "https://accounts.google.com/lifecycle/steps/signup/name";
const profile = { firstName: "Анна", lastName: "Морозова", login: "anna1234", password: "Example1!" };
const details = { birthDate: "2000-02-29", gender: "female", phoneNumber: "+12025550123" };

test("registration is scoped to the official signup path", () => {
    assert.equal(isSignupPage(firstStep), true);
    assert.equal(isSignupPage("https://accounts.google.com/signup/v2/webcreateaccount"), true);
    assert.equal(isSignupPage("https://accounts.google.com/v3/signin/identifier"), false);
    assert.equal(isSignupPage("https://accounts.google.com.example/signup"), false);
    assert.equal(isSignupPage("http://accounts.google.com/signup"), false);
});

test("birthday fills native selects and scoped menus without replacing existing choices", async () => {
    class Field {
        #value = "";
        get value() { return this.#value; }
        set value(value) { this.#value = value; }
        matches() { return false; }
        checkVisibility() { return true; }
        dispatchEvent() {}
    }

    const day = Object.assign(new Field(), { type: "number" });
    const year = Object.assign(new Field(), { type: "text" });
    const month = Object.assign(new Field(), { options: [{ matches: () => false, value: "2" }], selectedOptions: [] });
    let open = false;
    let gender = "";
    const combo = Object.assign(new Field(), {
        getAttribute: name => ({ "aria-controls": "gender-menu", "aria-expanded": String(open), "data-value": gender })[name],
        click: () => { open = !open; }
    });
    const option = Object.assign(new Field(), {
        getAttribute: name => ({ "aria-selected": String(gender === "1"), "data-value": "1" })[name],
        click: () => { gender = "1"; open = false; }
    });

    const url = "https://accounts.google.com/lifecycle/steps/signup/birthdaygender";
    globalThis.HTMLInputElement = Field;
    globalThis.HTMLSelectElement = Field;
    globalThis.location = new URL(url);
    globalThis.document = {
        getElementById: id => id === "gender-menu" && open ? { querySelectorAll: () => [option] } : null,
        querySelectorAll: selector => {
            if (selector.startsWith('input[name="day"]')) return [day];
            if (selector.startsWith('input[name="year"]')) return [year];
            if (selector.startsWith('select[name="month"]')) return [month];
            if (selector.startsWith('[role="combobox"][id="gender"]')) return [combo];
            return [];
        }
    };

    try {
        const data = readProfile("MailManager-Gmail/2:" + JSON.stringify({ ...profile, ...details }));
        assert.equal(await fillSignupFields(url, data), 4);
        assert.deepEqual([day.value, month.value, year.value, gender], ["29", "2", "2000", "1"]);
        assert.equal(await fillSignupFields(url, data), 0);
    } finally {
        delete globalThis.HTMLInputElement;
        delete globalThis.HTMLSelectElement;
        delete globalThis.location;
        delete globalThis.document;
    }
});

test("clipboard payload round trips Unicode names and rejects unrelated text", () => {
    assert.deepEqual(readProfile("MailManager-Gmail/1:" + JSON.stringify(profile)), profile);
    assert.throws(() => readProfile(JSON.stringify(profile)));
    assert.throws(() => readProfile("MailManager-Gmail/1:" + JSON.stringify({ ...profile, password: "no spaces " })));
    const encode = value => "MailManager-Gmail/2:" + JSON.stringify({ ...profile, ...details, ...value });
    assert.deepEqual(readProfile(encode()), { ...profile, birthday: { day: 29, month: 2, year: 2000 },
        gender: details.gender, phoneNumber: details.phoneNumber });
    assert.throws(() => readProfile(encode({ birthDate: "2001-02-29" })));
    assert.throws(() => readProfile(encode({ phoneNumber: "123456" })));
    assert.throws(() => readProfile(encode({ gender: "other" })));
});

test("the panel accepts both clipboard formats and explains an empty clipboard", async () => {
    const elements = new Map(["#profile", "#status", "#fill", "#clear", "#version"].map(id => [id, {
        listeners: {},
        addEventListener(name, listener) { this.listeners[name] = listener; }
    }]));
    globalThis.document = { querySelector: selector => elements.get(selector) };
    globalThis.chrome = {
        runtime: { getManifest: () => ({ version: "1.2.1" }) },
        tabs: { query: async () => [{ id: 1, url: firstStep }], onRemoved: { addListener() {} } }
    };
    const paste = text => elements.get("#profile").listeners.paste({
        preventDefault() {},
        clipboardData: { getData: () => text }
    });

    try {
        await import("../GmailExtension/panel.js");
        assert.equal(elements.get("#version").textContent, "Расширение 1.2.1");
        await paste("");
        assert.match(elements.get("#status").textContent, /Буфер обмена пуст/u);
        await paste(JSON.stringify(profile));
        assert.match(elements.get("#status").textContent, /Формат не распознан/u);
        await paste("MailManager-Gmail/1:" + JSON.stringify(profile));
        assert.equal(elements.get("#fill").disabled, false);
        await paste("MailManager-Gmail/2:" + JSON.stringify({ ...profile, ...details }));
        assert.equal(elements.get("#fill").disabled, false);
    } finally {
        elements.get("#clear").listeners.click?.();
        delete globalThis.document;
        delete globalThis.chrome;
    }
});

test("filling fills the phone but preserves manual input and skips an SMS code", async () => {
    class Input {
        #value;
        constructor(name, type = "text", value = "", visible = true) {
            Object.assign(this, { name, type, visible, events: [] });
            this.#value = value;
        }
        get value() { return this.#value; }
        set value(value) { this.#value = value; }
        matches() { return false; }
        checkVisibility() { return this.visible; }
        dispatchEvent(event) { this.events.push(event.type); }
    }

    const firstName = new Input("firstName");
    const editedName = new Input("lastName", "text", "Введённая фамилия");
    const username = new Input("Username");
    const password = new Input("Passwd", "password");
    const confirmation = new Input("PasswdAgain", "password");
    const hidden = new Input("password", "password", "", false);
    const phone = new Input("phoneNumber", "tel");
    const code = new Input("PhoneNumber", "tel");
    code.autocomplete = "one-time-code";
    const elements = [firstName, editedName, username, password, confirmation, hidden, phone, code];

    globalThis.HTMLInputElement = Input;
    globalThis.location = new URL(firstStep);
    globalThis.document = {
        querySelectorAll: selector => elements.filter(input => selector.includes('[name="' + input.name + '"]'))
    };

    try {
        assert.equal(await fillSignupFields("https://accounts.google.com/signup", profile), 0);
        assert.equal(await fillSignupFields(firstStep, { ...profile, phoneNumber: details.phoneNumber }), 5);
        assert.equal(firstName.value, profile.firstName);
        assert.equal(editedName.value, "Введённая фамилия");
        assert.equal(username.value, profile.login);
        assert.equal(password.value, profile.password);
        assert.equal(confirmation.value, profile.password);
        assert.equal(hidden.value, "");
        assert.equal(phone.value, details.phoneNumber);
        assert.equal(code.value, "");
        assert.deepEqual(password.events, ["input", "change"]);
        assert.equal(await fillSignupFields(firstStep, profile), 0);
    } finally {
        delete globalThis.HTMLInputElement;
        delete globalThis.location;
        delete globalThis.document;
    }
});
